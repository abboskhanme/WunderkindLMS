using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using static SchoolLms.Application.Services.QuestionBankService;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Test bazasi — the question banks of the entrance exam (admin side).
/// <c>docs/modules/admission-and-testing.md</c> §5.1, §6.2. Unit: B1.
/// Rules live in <see cref="QuestionBankService"/>; this class orders them,
/// saves, and writes the audit row.
///
/// <para>
/// <b>Permission — <c>admission</c>, reads included.</b> Every other admin
/// controller lets any <c>staff</c> account READ (<see cref="AdminPermAttribute"/>).
/// Not this one: <c>GET {id}/questions</c> returns the answer key of a live
/// entrance exam, so <c>GatedRead = true</c> makes a staff account without
/// <c>admission</c> get 403 on every GET here too (§4.3). admin / superadmin pass.
/// </para>
///
/// <para>
/// <b>Errors</b> are <c>{ code, message }</c>: 400 <c>validation</c>, 404
/// <c>bank_not_found</c> / <c>subject_not_found</c>, 409 <c>bank_exists</c> (a live
/// bank for this grade × subject) and 409 <c>bank_in_use</c> (an exam section
/// points at the bank). The client shows <c>message</c> as is.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("admission", GatedRead = true)]
[Route("api/admin/admission/banks")]
public class AdmissionBanksController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>
    /// The bank register, paged (§6 envelope). Live banks only; <c>search</c>
    /// matches the subject name. <c>limit</c> is capped at 200.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdmissionPageDto<BankDto>>> GetAll(
        [FromQuery] int? page, [FromQuery] int? limit, [FromQuery] string? search,
        [FromQuery] int? grade, [FromQuery] string? subjectId, CancellationToken ct = default) =>
        await ListBanksAsync(db, page, limit, search, grade, subjectId, ct);

    /// <summary>One bank with its question count and state.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<BankDto>> GetOne(string id, CancellationToken ct = default)
    {
        var bank = await GetBankAsync(db, id, ct);
        if (bank is null) return BankNotFound();
        return bank;
    }

    /// <summary>
    /// A new bank. Grade and subject are fixed from here on — the PUT carries
    /// only the three settings. 409 <c>bank_exists</c> when a live bank for the
    /// same grade × subject is already there (<c>ux_question_banks_live</c>).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BankDto>> Create(BankCreateRequest p, CancellationToken ct = default)
    {
        if (CreateProblem(p) is { } problem) return Invalid(problem);
        var grade = p.Grade!.Value;
        var subjectId = p.SubjectId!.Trim();

        var subjectName = await db.Subjects.AsNoTracking()
            .Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        if (subjectName is null)
            return NotFound(new { code = Codes.SubjectNotFound, message = SubjectNotFoundMessage });
        if (await LiveBankExistsAsync(db, grade, subjectId, ct)) return BankExists();

        var bank = new QuestionBank
        {
            Grade = grade,
            SubjectId = subjectId,
            QuestionsPerTest = p.QuestionsPerTest,
            TimeLimitMin = p.TimeLimitMin,
            PointsPerCorrect = p.PointsPerCorrect,
            CreatedByUserId = CurrentUserId,
        };
        db.QuestionBanks.Add(bank);

        audit.Record(AuditEntityBank, bank.Id, "create",
            $"Test bazasi yaratildi: {BankTitle(grade, subjectName)}", after: Snapshot(bank));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsLiveBankViolation(ex))
        {
            // The race: another admin created the same bank between the check and the insert.
            return BankExists();
        }

        return QuestionBankService.ToDto(bank, subjectName, questionsCount: 0);
    }

    /// <summary>
    /// The settings strip. All three values are always sent; <c>null</c>
    /// clears one (the bank turns <c>unconfigured</c>). Editing the points never
    /// rescores a finished exam — publish copied them into the section (§5.6).
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<BankDto>> UpdateSettings(
        string id, BankSettingsRequest p, CancellationToken ct = default)
    {
        var current = await GetBankAsync(db, id, ct);
        if (current is null) return BankNotFound();
        if (SettingsProblem(p.QuestionsPerTest, p.TimeLimitMin, p.PointsPerCorrect) is { } problem)
            return Invalid(problem);

        var bank = await db.QuestionBanks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bank is null) return BankNotFound();

        // No change — no write and no audit row: the journal must not say
        // "changed" where nothing happened (the SurveysController.SetActive rule).
        var changed = bank.QuestionsPerTest != p.QuestionsPerTest
            || bank.TimeLimitMin != p.TimeLimitMin
            || bank.PointsPerCorrect != p.PointsPerCorrect;
        if (changed)
        {
            var before = Snapshot(bank);
            bank.QuestionsPerTest = p.QuestionsPerTest;
            bank.TimeLimitMin = p.TimeLimitMin;
            bank.PointsPerCorrect = p.PointsPerCorrect;

            audit.Record(AuditEntityBank, bank.Id, "update",
                $"Test bazasi sozlamalari o'zgardi: {BankTitle(bank.Grade, current.SubjectName)}",
                before: before, after: Snapshot(bank));

            await db.SaveChangesAsync(ct);
        }

        return QuestionBankService.ToDto(bank, current.SubjectName, current.QuestionsCount);
    }

    /// <summary>
    /// Deletes the bank with its questions and options (<c>on delete cascade</c>).
    /// 409 <c>bank_in_use</c> if any exam section points at it — draft, published,
    /// closed or cancelled alike: the section keeps the exam's history.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        var current = await GetBankAsync(db, id, ct);
        if (current is null) return BankNotFound();
        if (await BankInUseAsync(db, id, ct)) return BankInUse();

        var bank = await db.QuestionBanks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bank is null) return BankNotFound();
        db.QuestionBanks.Remove(bank);

        audit.Record(AuditEntityBank, bank.Id, "delete",
            $"Test bazasi o'chirildi: {BankTitle(bank.Grade, current.SubjectName)} "
            + $"({current.QuestionsCount} ta savol bilan)",
            before: Snapshot(bank));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // The race: a section took the bank (or a paper drew one of its
            // questions) between the check and the delete. RESTRICT held it.
            return BankInUse();
        }

        return NoContent();
    }

    /// <summary>
    /// One page of the bank's questions, in display order — <b>with the answer
    /// key</b> (<c>options[].isCorrect</c>). This is the response §4.3 gates reads for.
    /// </summary>
    [HttpGet("{id}/questions")]
    public async Task<ActionResult<AdmissionPageDto<QuestionDto>>> Questions(
        string id, [FromQuery] int? page, [FromQuery] int? limit, [FromQuery] string? search,
        CancellationToken ct = default)
    {
        if (!await db.QuestionBanks.AsNoTracking().AnyAsync(b => b.Id == id, ct)) return BankNotFound();
        return await ListQuestionsAsync(db, id, page, limit, search, ct);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>The caller's user id; <c>null</c> (not "") so the FK to <c>users</c> stays valid.</summary>
    private string? CurrentUserId
    {
        get
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
    }

    private BadRequestObjectResult Invalid(string message) =>
        BadRequest(new { code = Codes.Validation, message });

    private NotFoundObjectResult BankNotFound() =>
        NotFound(new { code = Codes.BankNotFound, message = BankNotFoundMessage });

    private ConflictObjectResult BankExists() =>
        Conflict(new { code = Codes.BankExists, message = BankExistsMessage });

    private ConflictObjectResult BankInUse() =>
        Conflict(new { code = Codes.BankInUse, message = BankInUseMessage });
}
