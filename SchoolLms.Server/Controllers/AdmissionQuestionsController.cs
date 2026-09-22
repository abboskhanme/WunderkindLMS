using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;
using static SchoolLms.Application.Services.QuestionBankService;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Test bazasi — questions: add, edit, delete, Excel template and import.
/// <c>docs/modules/admission-and-testing.md</c> §5.2–§5.3, §6.2, §8.2. Unit: B1.
/// Reading a bank's questions is <c>GET /api/admin/admission/banks/{id}/questions</c>
/// (<see cref="AdmissionBanksController"/>).
///
/// <para>
/// <b>Permission — <c>admission</c>, reads included</b> (<c>GatedRead</c>, §4.3):
/// every response here carries <c>isCorrect</c>, and the template GET is
/// gated with the rest rather than carved out.
/// </para>
///
/// <para>
/// <b>Errors</b> are <c>{ code, message }</c>: 400 <c>validation</c>; 404
/// <c>bank_not_found</c> / <c>question_not_found</c>; 409 <c>question_answered</c>
/// (the question is on an attempt's paper), <c>bank_in_published_exam</c>
/// (§8.2 — the bank cannot shrink under a live exam), <c>option_answered</c> (an
/// edit drops an option a candidate chose) and <c>question_changed</c> (a
/// concurrent edit).
/// </para>
///
/// <para>
/// <b>Editing an answered question is allowed</b> — text, image and even the
/// key. A wrong key found mid-season must be fixable, and §3.1 has a "recompute
/// the result" action for exactly that. Finished scores do not move on their
/// own: they are stored numbers. Only dropping an option somebody chose is
/// refused, because <c>exam_answers.selected_option_id</c> is RESTRICT.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("admission", GatedRead = true)]
[Route("api/admin/admission/questions")]
public class AdmissionQuestionsController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>
    /// A new question at the end of the bank. Options are stored in array
    /// order (index 0 = A); ids in the body are ignored on create.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<QuestionDto>> Create(QuestionCreateRequest p, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(p.BankId)) return Invalid(BankIdRequiredMessage);
        var (value, problem) = CheckQuestion(p.Text, p.ImageUrl, p.Options);
        if (value is null) return Invalid(problem!);

        var bankId = p.BankId.Trim();
        if (!await db.QuestionBanks.AsNoTracking().AnyAsync(b => b.Id == bankId, ct)) return BankNotFound();

        var question = NewQuestion(bankId, await NextOrderAsync(db, bankId, ct), value);
        db.Questions.Add(question);

        audit.Record(AuditEntityQuestion, question.Id, "create",
            $"Test bazasiga savol qo'shildi: «{QuestionLabel(question.Text)}»", after: Snapshot(value));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // The bank was deleted between the check and the insert.
            return BankNotFound();
        }

        return await QuestionOrNotFoundAsync(question.Id, ct);
    }

    /// <summary>
    /// Edits a question. The option list is the whole new list: an option with
    /// an <c>id</c> is edited in place, one without is added, a stored option
    /// left out is deleted — unless a candidate chose it (409 <c>option_answered</c>).
    ///
    /// <para>
    /// Two <c>SaveChanges</c> in one transaction, because
    /// <c>ux_question_options_one_correct</c> would reject the moment the key
    /// moves from one option to another in a single batch — see
    /// <see cref="QuestionBankService.StageRemovalsAndKeyRelease"/>.
    /// </para>
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<QuestionDto>> Update(
        string id, QuestionUpdateRequest p, CancellationToken ct = default)
    {
        var question = await db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (question is null) return QuestionNotFound();

        var (value, problem) = CheckQuestion(p.Text, p.ImageUrl, p.Options);
        if (value is null) return Invalid(problem!);

        var plan = PlanOptions(question, value);
        if (plan.UnknownId) return QuestionChanged();
        if (await AnyOptionChosenAsync(db, plan.Removed.Select(o => o.Id).ToList(), ct)) return OptionAnswered();

        var before = Snapshot(question);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            StageRemovalsAndKeyRelease(db, question, plan);
            await db.SaveChangesAsync(ct);

            ApplyQuestion(db, question, value);
            audit.Record(AuditEntityQuestion, question.Id, "update",
                $"Test bazasidagi savol tahrirlandi: «{QuestionLabel(question.Text)}»",
                before: before, after: Snapshot(value));
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsChosenOptionViolation(ex))
        {
            // A candidate chose a dropped option between the check and the delete.
            return OptionAnswered();
        }
        catch (DbUpdateException ex) when (
            ex is DbUpdateConcurrencyException || IsOneCorrectViolation(ex) || IsForeignKeyViolation(ex))
        {
            // Another admin edited or deleted this question at the same moment
            // (a new option under a deleted question is a 23503 too).
            return QuestionChanged();
        }

        return await QuestionOrNotFoundAsync(id, ct);
    }

    /// <summary>
    /// Deletes a question with its options. 409 when it is on any attempt's
    /// paper (<c>question_answered</c>) or when its bank feeds a <c>published</c>
    /// exam (<c>bank_in_published_exam</c>, §8.2) — allowed again once that exam
    /// is closed or cancelled.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        var question = await db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (question is null) return QuestionNotFound();

        if (await IsOnAnyPaperAsync(db, id, ct))
            return Conflict(new { code = Codes.QuestionAnswered, message = QuestionAnsweredMessage });
        // Not locked against a publish that commits between this check and the
        // delete — a window of milliseconds. Closing it needs one row lock on the
        // bank taken by both this delete and B2's publish; it is not taken today.
        if (await BankFeedsPublishedExamAsync(db, question.BankId, ct))
            return Conflict(new { code = Codes.BankInPublishedExam, message = BankInPublishedExamMessage });

        db.Questions.Remove(question);
        audit.Record(AuditEntityQuestion, question.Id, "delete",
            $"Test bazasidan savol o'chirildi: «{QuestionLabel(question.Text)}»",
            before: Snapshot(question));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // An attempt drew the question between the check and the delete.
            return Conflict(new { code = Codes.QuestionAnswered, message = QuestionAnsweredMessage });
        }

        return NoContent();
    }

    /// <summary>
    /// <c>savollar_shablon.xlsx</c> for one bank: the <c>Savollar</c> sheet
    /// (headers only) and the <c>Yo'riqnoma</c> rules sheet naming the bank.
    /// </summary>
    [HttpGet("import/template")]
    public async Task<IActionResult> Template([FromQuery] string? bankId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bankId)) return Invalid(BankIdRequiredMessage);
        var bank = await GetBankAsync(db, bankId.Trim(), ct);
        if (bank is null) return BankNotFound();

        return File(
            QuestionImportService.Template(BankTitle(bank.Grade, bank.SubjectName)),
            QuestionImportService.XlsxMime,
            QuestionImportService.TemplateFileName);
    }

    /// <summary>
    /// Excel import — one endpoint with a dry run (§6.2). <c>bankId</c> and
    /// <c>dryRun</c> are multipart FORM fields next to <c>file</c>.
    /// <c>dryRun=true</c> (also the default, so a request that forgets the field
    /// never writes) reads and reports; <c>dryRun=false</c> reads the same file
    /// again and writes every valid row in one transaction. A row with any
    /// error is skipped whole.
    ///
    /// <para>
    /// 400 <c>validation</c> for a file that cannot be used at all: none,
    /// not <c>.xlsx</c>, unreadable, wrong columns, too many rows. A header-only
    /// sheet is NOT an error — it comes back as <c>totalRows: 0</c> and the
    /// screen explains it.
    /// </para>
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(QuestionImportService.MaxUploadBytes)]
    public async Task<ActionResult<QuestionImportResultDto>> Import(
        IFormFile? file, [FromForm] string? bankId, [FromForm] bool dryRun = true,
        CancellationToken ct = default)
    {
        if (QuestionImportService.RejectFile(file?.FileName, file?.Length ?? 0) is { } rejected)
            return Invalid(rejected);
        if (string.IsNullOrWhiteSpace(bankId)) return Invalid(BankIdRequiredMessage);

        var bank = await GetBankAsync(db, bankId.Trim(), ct);
        if (bank is null) return BankNotFound();

        QuestionImportPlan plan;
        await using (var stream = file!.OpenReadStream())
            plan = QuestionImportService.Read(stream);
        if (plan.FatalMessage is { } fatal) return Invalid(fatal);

        if (dryRun || plan.Valid.Count == 0) return plan.ToResult(file.FileName, imported: 0);

        var questions = QuestionImportService.ToQuestions(bank.Id, await NextOrderAsync(db, bank.Id, ct), plan);
        db.Questions.AddRange(questions);

        audit.Record(AuditEntityBank, bank.Id, "import",
            $"Excel'dan {questions.Count} ta savol yuklandi: {BankTitle(bank.Grade, bank.SubjectName)} "
            + $"(fayl: {file.FileName}, o'tkazib yuborilgan qator: {plan.ErrorCount})",
            after: new { file.FileName, plan.TotalRows, Imported = questions.Count, Skipped = plan.ErrorCount });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // The bank was deleted while the file was being read.
            return BankNotFound();
        }

        return plan.ToResult(file.FileName, imported: questions.Count);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private async Task<ActionResult<QuestionDto>> QuestionOrNotFoundAsync(string id, CancellationToken ct)
    {
        var dto = await GetQuestionAsync(db, id, ct);
        if (dto is null) return QuestionNotFound();
        return dto;
    }

    private BadRequestObjectResult Invalid(string message) =>
        BadRequest(new { code = Codes.Validation, message });

    private NotFoundObjectResult BankNotFound() =>
        NotFound(new { code = Codes.BankNotFound, message = BankNotFoundMessage });

    private NotFoundObjectResult QuestionNotFound() =>
        NotFound(new { code = Codes.QuestionNotFound, message = QuestionNotFoundMessage });

    private ConflictObjectResult QuestionChanged() =>
        Conflict(new { code = Codes.QuestionChanged, message = QuestionChangedMessage });

    private ConflictObjectResult OptionAnswered() =>
        Conflict(new { code = Codes.OptionAnswered, message = OptionAnsweredMessage });
}
