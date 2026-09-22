using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Test bazasi — the admission question bank: rules, queries, DTO mapping.
//  docs/modules/admission-and-testing.md §5.1–§5.3, §6.2, §8.2. Unit: B1.
//  Controllers: AdmissionBanksController, AdmissionQuestionsController.
// ===========================================================================
//
//  WHY STATIC, AND NOT IN DI
//  -------------------------
//  `Program.cs` belongs to another unit in this wave (B3 adds the exam rate
//  policy and the sweep), so no new service can be registered. The project
//  pattern for that case is `SurveyService` / `CertificateService`: a static
//  class that takes `IAppDbContext`. The controller gets `AppDbContext` from DI,
//  owns the save and the audit row, and asks this class for the rules.
//
//  THE DATABASE HOLDS THE RULE, THIS CLASS EXPLAINS IT
//  ---------------------------------------------------
//  Every rule below also exists in the schema (`ExamModel.cs`):
//    * one live bank per grade × subject      — `ux_question_banks_live`
//    * at most one correct option per question — `ux_question_options_one_correct`
//    * a bank used by an exam cannot go        — `exam_sections.bank_id` RESTRICT
//    * an answered question / option cannot go — `exam_answers` FKs RESTRICT
//  The database answers with 23505 / 23503, which a clerk reads as "500". So the
//  service checks FIRST and returns an Uzbek sentence and a stable `code`; the
//  constraint stays the last guard for the race (two admins at once), and the
//  controller maps that race onto the same 409 via `IsLiveBankViolation` /
//  `IsForeignKeyViolation`.
//
//  "AT LEAST ONE CORRECT" IS OURS
//  ------------------------------
//  Postgres cannot say "at least one row where is_correct" across rows without
//  a deferred trigger (§5.3). `CheckQuestion` is that rule's only home — every
//  write path (POST, PUT, and the Excel import via its own row check) goes
//  through a check that demands exactly one.
//
//  THE ANSWER KEY
//  --------------
//  `QuestionDto` carries `isCorrect`. It is served only by the two controllers
//  gated with `[AdminPerm("admission", GatedRead = true)]` (§4.3). Nothing
//  candidate-facing may call `ListQuestionsAsync` / `GetQuestionAsync` (§7.7).
// ===========================================================================

/// <summary>
/// The derived bank state (§3.1 screen 3, §8.1 rule 3). Closed list; the client
/// renders it and never recomputes it.
/// </summary>
public static class QuestionBankState
{
    /// <summary>At least one of the three settings is missing — publish would refuse it.</summary>
    public const string Unconfigured = "unconfigured";

    /// <summary>Configured, but the bank has fewer questions than one sitting draws.</summary>
    public const string NotEnough = "notEnough";

    /// <summary>Configured and big enough — an exam can draw from it.</summary>
    public const string Ready = "ready";
}

/// <summary>Rules, queries and DTO mapping of the question bank. No <c>HttpContext</c>.</summary>
public static class QuestionBankService
{
    // =====================================================================
    //  Constants
    // =====================================================================

    /// <summary>
    /// <c>audit_logs.entity_type</c> of a bank — create, settings, delete and
    /// the Excel import (an import is an action on the bank). A local literal (the
    /// <see cref="SurveyService.AuditEntity"/> precedent): <c>AuditService.cs</c>
    /// is a shared file in this wave. Rows are searched by this text — never change it.
    /// </summary>
    public const string AuditEntityBank = "QuestionBank";

    /// <summary><c>audit_logs.entity_type</c> of one question — create, edit, delete.</summary>
    public const string AuditEntityQuestion = "Question";

    /// <summary>Index names from <c>ExamModel.cs</c> — how a 23505 is recognised.</summary>
    public const string LiveBankIndex = "ux_question_banks_live";
    public const string OneCorrectIndex = "ux_question_options_one_correct";

    /// <summary>
    /// The RESTRICT key from a candidate's answer to the option they chose
    /// (migration <c>AdmissionAndExams</c>) — how a 23503 caused by an answer is
    /// told apart from one caused by a vanished parent row.
    /// </summary>
    public const string ChosenOptionForeignKey = "fk_exam_answers_question_options_selected_option_id";

    /// <summary>§5.1 <c>ck_question_banks_grade</c>.</summary>
    public const int MinGrade = 0;
    public const int MaxGrade = 11;

    /// <summary>§5.1 <c>ck_question_banks_time_limit_min</c>.</summary>
    public const int MaxTimeLimitMin = 600;

    /// <summary>
    /// The largest value <c>numeric(6,2)</c> holds. Checked here because Postgres
    /// would round a third decimal silently and overflow a fifth digit with 22003.
    /// </summary>
    public const decimal MaxPointsPerCorrect = 9999.99m;

    /// <summary>§5.3 — between 2 and 6 options, lettered A–F.</summary>
    public const int MinOptions = 2;
    public const int MaxOptions = 6;
    public const string OptionLetters = "ABCDEF";

    /// <summary>§6 pagination: default 50, cap 200.</summary>
    public const int DefaultPageLimit = 50;
    public const int MaxPageLimit = 200;

    /// <summary>The only image a question may carry: a file from <c>POST /api/admin/uploads</c>.</summary>
    public const string UploadsPrefix = "/uploads/";

    /// <summary>Stable error codes — the <c>code</c> of every <c>{ code, message }</c> body.</summary>
    public static class Codes
    {
        public const string Validation = "validation";
        public const string BankNotFound = "bank_not_found";
        public const string SubjectNotFound = "subject_not_found";
        public const string QuestionNotFound = "question_not_found";
        public const string BankExists = "bank_exists";
        public const string BankInUse = "bank_in_use";
        public const string QuestionAnswered = "question_answered";
        public const string BankInPublishedExam = "bank_in_published_exam";
        public const string OptionAnswered = "option_answered";
        public const string QuestionChanged = "question_changed";
    }

    // ----- Messages. Where the client has a fallback for the same case, the words match. -----

    public const string GradeRequiredMessage = "Sinfni tanlang";
    public const string GradeRangeMessage = "Sinf 0 dan 11 gacha bo'lishi kerak";
    public const string SubjectRequiredMessage = "Fanni tanlang";
    public const string SubjectNotFoundMessage = "Tanlangan fan topilmadi — fanlar ro'yxatini yangilang";
    public const string QuestionsPerTestMessage = "Testdagi savollar soni — 1 yoki undan katta butun son";
    public const string TimeLimitMessage = "Vaqt — 1 dan 600 gacha daqiqa";
    public const string PointsMessage = "Ball — 0 dan katta son, ko'pi bilan 2 xonali kasr (masalan 1,5)";

    public const string BankNotFoundMessage = "Baza topilmadi — u o'chirilgan bo'lishi mumkin";
    public const string BankIdRequiredMessage = "Savol qaysi bazaga qo'shilishi ko'rsatilmagan";
    public const string BankExistsMessage = "Bu sinf va fan uchun baza allaqachon bor";
    public const string BankInUseMessage = "Bu baza imtihonda ishlatilgan — uni o'chirib bo'lmaydi";

    public const string QuestionNotFoundMessage = "Savol topilmadi — u allaqachon o'chirilgan bo'lishi mumkin";
    public const string TextRequiredMessage = "Savol matnini kiriting";
    public const string TooFewOptionsMessage = "Kamida 2 ta variant bo'lsin";
    public const string TooManyOptionsMessage = "Ko'pi bilan 6 ta variant";
    public const string NoCorrectMessage = "To'g'ri javobni belgilang";
    public const string ManyCorrectMessage = "Faqat bitta to'g'ri javob bo'lishi mumkin";
    public const string BadImageUrlMessage = "Rasm havolasi noto'g'ri — rasmni qaytadan yuklang";
    public const string DuplicateOptionIdMessage = "Variantlar ro'yxati buzilgan — sahifani yangilang";

    public const string QuestionAnsweredMessage =
        "Bu savol imtihonda nomzod varag'iga tushgan — uni o'chirib bo'lmaydi";
    public const string BankInPublishedExamMessage =
        "Bu savolning bazasi e'lon qilingan imtihonda ishlatilmoqda — imtihon yopilgach "
        + "yoki bekor qilingach o'chirish mumkin";
    public const string OptionAnsweredMessage =
        "Olib tashlangan variantni imtihonda tanlaganlar bor — uni o'chirib bo'lmaydi, "
        + "matnini tahrirlang";
    public const string QuestionChangedMessage = "Savol boshqa joyda o'zgargan — sahifani yangilang";

    public static string BlankOptionMessage(int index) =>
        $"{OptionLetter(index)} variant bo'sh — to'ldiring yoki olib tashlang";

    public static string DuplicateOptionMessage(int first, int second) =>
        $"Bir xil variant: {OptionLetter(first)} va {OptionLetter(second)}";

    // =====================================================================
    //  Small pure helpers
    // =====================================================================

    /// <summary>0 → "A" … 5 → "F".</summary>
    public static string OptionLetter(int index) =>
        index >= 0 && index < OptionLetters.Length ? OptionLetters[index].ToString() : (index + 1).ToString();

    /// <summary>"5-sinf · Matematika" / "Nol sinf · Ingliz tili" — the wording of the screens.</summary>
    public static string BankTitle(int grade, string subjectName) =>
        $"{(grade == 0 ? "Nol sinf" : $"{grade}-sinf")} · {subjectName}";

    /// <summary>
    /// §6 paging, clamped rather than refused: page &lt; 1 → 1; limit missing or
    /// &lt; 1 → 50; limit &gt; 200 → 200. The page is capped so <c>Skip</c> cannot overflow.
    /// </summary>
    public static (int Page, int Limit) Paging(int? page, int? limit)
    {
        var l = limit is null or < 1 ? DefaultPageLimit : Math.Min(limit.Value, MaxPageLimit);
        var p = page is null or < 1 ? 1 : Math.Min(page.Value, int.MaxValue / l);
        return (p, l);
    }

    /// <summary>
    /// The bank state (§8.1 rule 3). <b>All three settings</b> must be set for
    /// <c>ready</c>, not only <c>questionsPerTest</c> as §3.1's badge sentence
    /// says: publish refuses a bank without a time limit or points, and a green
    /// "Tayyor" badge on a bank that publish rejects would be a lie.
    /// B2's publish check may call this to stay in step with the badge.
    /// </summary>
    public static string StateOf(int? questionsPerTest, int? timeLimitMin, decimal? pointsPerCorrect, int questionsCount)
    {
        if (questionsPerTest is null || timeLimitMin is null || pointsPerCorrect is null)
            return QuestionBankState.Unconfigured;
        return questionsCount < questionsPerTest ? QuestionBankState.NotEnough : QuestionBankState.Ready;
    }

    /// <summary>
    /// A question image: a file we stored (<c>/uploads/…</c>, what
    /// <c>UploadsController</c> returns) and nothing else — it becomes an
    /// <c>&lt;img src&gt;</c> on the public exam page. <c>..</c> is refused so the
    /// path cannot step out of the uploads directory.
    /// </summary>
    public static bool IsUploadUrl(string url) =>
        url.StartsWith(UploadsPrefix, StringComparison.Ordinal)
        && !url.Contains("..", StringComparison.Ordinal)
        && !url.Any(char.IsWhiteSpace);

    // =====================================================================
    //  Bank — validation
    // =====================================================================

    /// <summary>§5.1 checks of the three settings. <c>null</c> = fine (null itself is allowed).</summary>
    public static string? SettingsProblem(int? questionsPerTest, int? timeLimitMin, decimal? pointsPerCorrect)
    {
        if (questionsPerTest is < 1) return QuestionsPerTestMessage;
        if (timeLimitMin is < 1 or > MaxTimeLimitMin) return TimeLimitMessage;
        if (pointsPerCorrect is { } points
            && (points <= 0 || points > MaxPointsPerCorrect || decimal.Round(points, 2) != points))
            return PointsMessage;
        return null;
    }

    /// <summary>The whole create body, before any database call.</summary>
    public static string? CreateProblem(BankCreateRequest p)
    {
        if (p.Grade is null) return GradeRequiredMessage;
        if (p.Grade is < MinGrade or > MaxGrade) return GradeRangeMessage;
        if (string.IsNullOrWhiteSpace(p.SubjectId)) return SubjectRequiredMessage;
        return SettingsProblem(p.QuestionsPerTest, p.TimeLimitMin, p.PointsPerCorrect);
    }

    /// <summary>Is there already a live (non-archived) bank for this grade × subject.</summary>
    public static Task<bool> LiveBankExistsAsync(
        IAppDbContext db, int grade, string subjectId, CancellationToken ct = default) =>
        db.QuestionBanks.AsNoTracking()
            .AnyAsync(b => !b.IsArchived && b.Grade == grade && b.SubjectId == subjectId, ct);

    /// <summary>
    /// Does any exam section point at the bank — whatever the exam's status.
    /// A draft exam counts too: it chose this bank, and <c>exam_sections.bank_id</c>
    /// is RESTRICT, so the delete would fail anyway.
    /// </summary>
    public static Task<bool> BankInUseAsync(IAppDbContext db, string bankId, CancellationToken ct = default) =>
        db.ExamSections.AsNoTracking().AnyAsync(s => s.BankId == bankId, ct);

    // =====================================================================
    //  Bank — queries
    // =====================================================================

    /// <summary>
    /// The bank register (§6.2): live banks only, grade then subject name.
    /// <paramref name="search"/> matches the subject name, case-insensitively.
    /// Two SQL statements whatever the page size — the question count is a
    /// correlated <c>count(*)</c> inside the page query, not a query per row.
    /// </summary>
    public static async Task<AdmissionPageDto<BankDto>> ListBanksAsync(
        IAppDbContext db, int? page, int? limit, string? search, int? grade, string? subjectId,
        CancellationToken ct = default)
    {
        var (p, l) = Paging(page, limit);

        var q = BankRows(db).Where(b => !b.IsArchived);
        if (grade is { } g) q = q.Where(b => b.Grade == g);
        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            var sid = subjectId.Trim();
            q = q.Where(b => b.SubjectId == sid);
        }
        var term = (search ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0) q = q.Where(b => b.SubjectName.ToLower().Contains(term));

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(b => b.Grade).ThenBy(b => b.SubjectName).ThenBy(b => b.Id)
            .Skip((p - 1) * l).Take(l)
            .ToListAsync(ct);

        return new AdmissionPageDto<BankDto>(rows.Select(ToDto).ToList(), total, p, l);
    }

    /// <summary>One bank, archived or not. <c>null</c> = no such bank.</summary>
    public static async Task<BankDto?> GetBankAsync(IAppDbContext db, string id, CancellationToken ct = default)
    {
        var row = await BankRows(db).FirstOrDefaultAsync(b => b.Id == id, ct);
        return row is null ? null : ToDto(row);
    }

    /// <summary>
    /// A tracked bank whose subject name and question count the caller already
    /// holds (create: 0; settings PUT: read before the change) — no extra query.
    /// </summary>
    public static BankDto ToDto(QuestionBank b, string subjectName, int questionsCount) =>
        new(b.Id, b.Grade, b.SubjectId, subjectName, questionsCount,
            b.QuestionsPerTest, b.TimeLimitMin, b.PointsPerCorrect,
            StateOf(b.QuestionsPerTest, b.TimeLimitMin, b.PointsPerCorrect, questionsCount));

    /// <summary>
    /// The bank row with its subject name and question count. A member-init
    /// class, not a record, so the list can keep composing <c>Where</c> /
    /// <c>OrderBy</c> on its members in SQL.
    /// </summary>
    private sealed class BankRow
    {
        public string Id { get; init; } = "";
        public int Grade { get; init; }
        public string SubjectId { get; init; } = "";
        public string SubjectName { get; init; } = "";
        public bool IsArchived { get; init; }
        public int QuestionsCount { get; init; }
        public int? QuestionsPerTest { get; init; }
        public int? TimeLimitMin { get; init; }
        public decimal? PointsPerCorrect { get; init; }
    }

    private static IQueryable<BankRow> BankRows(IAppDbContext db) =>
        from b in db.QuestionBanks.AsNoTracking()
        join s in db.Subjects.AsNoTracking() on b.SubjectId equals s.Id
        select new BankRow
        {
            Id = b.Id,
            Grade = b.Grade,
            SubjectId = b.SubjectId,
            SubjectName = s.Name,
            IsArchived = b.IsArchived,
            QuestionsCount = db.Questions.Count(q => q.BankId == b.Id),
            QuestionsPerTest = b.QuestionsPerTest,
            TimeLimitMin = b.TimeLimitMin,
            PointsPerCorrect = b.PointsPerCorrect,
        };

    private static BankDto ToDto(BankRow r) =>
        new(r.Id, r.Grade, r.SubjectId, r.SubjectName, r.QuestionsCount,
            r.QuestionsPerTest, r.TimeLimitMin, r.PointsPerCorrect,
            StateOf(r.QuestionsPerTest, r.TimeLimitMin, r.PointsPerCorrect, r.QuestionsCount));

    // =====================================================================
    //  Question — validation
    // =====================================================================

    /// <summary>A save request after trimming and checking — safe to write.</summary>
    /// <param name="Options">In the order they are stored: index = <c>order</c> (0 = A).</param>
    public sealed record CheckedQuestion(string Text, string? ImageUrl, IReadOnlyList<CheckedOption> Options);

    /// <summary>One checked option. <paramref name="Id"/> is only meaningful on update.</summary>
    public sealed record CheckedOption(string? Id, string Text, bool IsCorrect);

    /// <summary>
    /// The §5.3 rules for one question, in the order the form shows them: text,
    /// 2–6 options, no blank option, no two identical options (after trim,
    /// case-sensitive — the same comparison as the form and the import), exactly
    /// one correct, the image an upload. The first broken rule is returned as
    /// the Uzbek sentence; otherwise the trimmed question.
    /// </summary>
    public static (CheckedQuestion? Value, string? Problem) CheckQuestion(
        string? text, string? imageUrl, IReadOnlyList<QuestionOptionInput>? options)
    {
        var cleanText = (text ?? "").Trim();
        if (cleanText.Length == 0) return (null, TextRequiredMessage);

        var input = options ?? [];
        if (input.Count < MinOptions) return (null, TooFewOptionsMessage);
        if (input.Count > MaxOptions) return (null, TooManyOptionsMessage);

        var checkedOptions = new List<CheckedOption>(input.Count);
        var firstIndexByText = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < input.Count; i++)
        {
            // A JSON `null` element reads as a blank option, not a 500.
            if (input[i] is not { } option || string.IsNullOrWhiteSpace(option.Text))
                return (null, BlankOptionMessage(i));

            var optionText = option.Text.Trim();
            if (firstIndexByText.TryGetValue(optionText, out var first))
                return (null, DuplicateOptionMessage(first, i));
            firstIndexByText[optionText] = i;

            var id = string.IsNullOrWhiteSpace(option.Id) ? null : option.Id.Trim();
            checkedOptions.Add(new CheckedOption(id, optionText, option.IsCorrect));
        }

        var correct = checkedOptions.Count(o => o.IsCorrect);
        if (correct == 0) return (null, NoCorrectMessage);
        if (correct > 1) return (null, ManyCorrectMessage);

        var ids = checkedOptions.Where(o => o.Id is not null).Select(o => o.Id!).ToList();
        if (ids.Count != ids.Distinct(StringComparer.Ordinal).Count()) return (null, DuplicateOptionIdMessage);

        string? cleanImage = null;
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            cleanImage = imageUrl.Trim();
            if (!IsUploadUrl(cleanImage)) return (null, BadImageUrlMessage);
        }

        return (new CheckedQuestion(cleanText, cleanImage, checkedOptions), null);
    }

    // =====================================================================
    //  Question — writes (the controller saves; these only stage changes)
    // =====================================================================

    /// <summary>
    /// The next display order in the bank — new questions go to the end.
    /// Not unique by design (<c>ix_questions_bank_order</c> is a plain index):
    /// two concurrent adds may share an order, and the list breaks the tie by
    /// creation time.
    /// </summary>
    public static async Task<int> NextOrderAsync(IAppDbContext db, string bankId, CancellationToken ct = default) =>
        (await db.Questions.AsNoTracking().Where(q => q.BankId == bankId)
            .MaxAsync(q => (int?)q.Order, ct) ?? -1) + 1;

    /// <summary>A new question with its options, ready for <c>db.Questions.Add</c>.</summary>
    public static Question NewQuestion(string bankId, int order, CheckedQuestion value)
    {
        var question = new Question
        {
            BankId = bankId,
            Text = value.Text,
            ImageUrl = value.ImageUrl,
            Order = order,
        };
        for (var i = 0; i < value.Options.Count; i++)
        {
            question.Options.Add(new QuestionOption
            {
                QuestionId = question.Id,
                Text = value.Options[i].Text,
                IsCorrect = value.Options[i].IsCorrect,
                Order = i,
            });
        }
        return question;
    }

    /// <summary>The PUT plan for one question's existing options.</summary>
    /// <param name="UnknownId">The body names an option this question does not
    /// have — somebody else changed the question since the form was opened.</param>
    /// <param name="Removed">Existing options the body left out — to be deleted.</param>
    /// <param name="LosingKey">The kept option that is correct now and will not
    /// be (at most one) — its flag is cleared in the first pass.</param>
    public sealed record OptionPlan(
        bool UnknownId, IReadOnlyList<QuestionOption> Removed, IReadOnlyList<QuestionOption> LosingKey);

    /// <summary>Compares a checked PUT body with the question's stored options.</summary>
    public static OptionPlan PlanOptions(Question question, CheckedQuestion value)
    {
        var existingIds = question.Options.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var keptIds = value.Options.Where(o => o.Id is not null).Select(o => o.Id!).ToHashSet(StringComparer.Ordinal);
        // CheckQuestion guarantees exactly one; null = a new option takes the key.
        var finalCorrectId = value.Options.Single(o => o.IsCorrect).Id;

        var unknown = keptIds.Any(id => !existingIds.Contains(id));
        var removed = question.Options.Where(o => !keptIds.Contains(o.Id)).ToList();
        var losingKey = question.Options
            .Where(o => o.IsCorrect && keptIds.Contains(o.Id) && o.Id != finalCorrectId)
            .ToList();
        return new OptionPlan(unknown, removed, losingKey);
    }

    /// <summary>
    /// PUT, step 1 of 2 — stage the removals and clear the flag of the option
    /// that loses the key. Save after this, <b>inside the same transaction</b>,
    /// before step 2.
    ///
    /// <para>
    /// <b>Why two steps.</b> <c>ux_question_options_one_correct</c> is checked per
    /// statement, not at commit. Moving the key from A to B in one
    /// <c>SaveChanges</c> lets EF send "B = true" before "A = false", and the
    /// index sees two correct rows for an instant — 23505. Releasing A first
    /// makes every intermediate state legal; the transaction hides it from
    /// readers. When the key does not move, nothing is cleared and this step
    /// writes only the removals (or nothing at all).
    /// </para>
    /// <para>
    /// A removed option leaves <see cref="Question.Options"/> as well as the
    /// set: left in the collection, the next <c>DetectChanges</c> would find a
    /// detached entity under a tracked question and insert it again.
    /// </para>
    /// </summary>
    public static void StageRemovalsAndKeyRelease(IAppDbContext db, Question question, OptionPlan plan)
    {
        foreach (var option in plan.Removed)
        {
            question.Options.Remove(option);
            db.QuestionOptions.Remove(option);
        }
        foreach (var option in plan.LosingKey)
            option.IsCorrect = false;
    }

    /// <summary>
    /// PUT, step 2 of 2 — the final text, image and options. Array position
    /// becomes <c>order</c>; an option with an id is edited in place (so answers
    /// that chose it keep pointing at it), one without is inserted.
    /// </summary>
    public static void ApplyQuestion(IAppDbContext db, Question question, CheckedQuestion value)
    {
        question.Text = value.Text;
        question.ImageUrl = value.ImageUrl;

        var existing = question.Options.ToDictionary(o => o.Id, StringComparer.Ordinal);
        for (var i = 0; i < value.Options.Count; i++)
        {
            var input = value.Options[i];
            if (input.Id is not null && existing.TryGetValue(input.Id, out var option))
            {
                option.Text = input.Text;
                option.IsCorrect = input.IsCorrect;
                option.Order = i;
            }
            else
            {
                db.QuestionOptions.Add(new QuestionOption
                {
                    QuestionId = question.Id,
                    Text = input.Text,
                    IsCorrect = input.IsCorrect,
                    Order = i,
                });
            }
        }
    }

    /// <summary>
    /// Is the question on any attempt's paper — answered or not yet. §6.2 says
    /// "answered", but a drawn-and-unanswered row pins the question just the
    /// same (<c>exam_answers.question_id</c> is RESTRICT), so the check follows
    /// the constraint, not the wording.
    /// </summary>
    public static Task<bool> IsOnAnyPaperAsync(IAppDbContext db, string questionId, CancellationToken ct = default) =>
        db.ExamAnswers.AsNoTracking().AnyAsync(a => a.QuestionId == questionId, ct);

    /// <summary>
    /// §8.2: the bank cannot shrink under a live exam — is the bank used by a
    /// section of a <c>published</c> exam. Draft, closed and cancelled exams do
    /// not count.
    /// </summary>
    public static Task<bool> BankFeedsPublishedExamAsync(
        IAppDbContext db, string bankId, CancellationToken ct = default) =>
        (from s in db.ExamSections.AsNoTracking()
         join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
         where s.BankId == bankId && e.Status == ExamStatus.Published
         select s.Id).AnyAsync(ct);

    /// <summary>Has any candidate chosen one of these options.</summary>
    public static Task<bool> AnyOptionChosenAsync(
        IAppDbContext db, IReadOnlyCollection<string> optionIds, CancellationToken ct = default) =>
        optionIds.Count == 0
            ? Task.FromResult(false)
            : db.ExamAnswers.AsNoTracking()
                .AnyAsync(a => a.SelectedOptionId != null && optionIds.Contains(a.SelectedOptionId), ct);

    // =====================================================================
    //  Question — queries
    // =====================================================================

    /// <summary>
    /// One page of a bank's questions in display order, <b>answer key included</b>.
    /// <paramref name="search"/> matches the question text, case-insensitively.
    /// </summary>
    public static async Task<AdmissionPageDto<QuestionDto>> ListQuestionsAsync(
        IAppDbContext db, string bankId, int? page, int? limit, string? search, CancellationToken ct = default)
    {
        var (p, l) = Paging(page, limit);

        var q = db.Questions.AsNoTracking().Where(x => x.BankId == bankId);
        var term = (search ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0) q = q.Where(x => x.Text.ToLower().Contains(term));

        var total = await q.CountAsync(ct);
        var items = await ProjectAsync(
            q.OrderBy(x => x.Order).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((p - 1) * l).Take(l), ct);

        return new AdmissionPageDto<QuestionDto>(items, total, p, l);
    }

    /// <summary>One question with its options. <c>null</c> = no such question.</summary>
    public static async Task<QuestionDto?> GetQuestionAsync(IAppDbContext db, string id, CancellationToken ct = default) =>
        (await ProjectAsync(db.Questions.AsNoTracking().Where(x => x.Id == id), ct)).FirstOrDefault();

    /// <summary>
    /// Questions → DTOs in ONE statement (options are a collection projection,
    /// not a query per question). Projected onto an anonymous shape first and
    /// mapped in memory, so EF never has to translate a record constructor.
    /// </summary>
    private static async Task<List<QuestionDto>> ProjectAsync(IQueryable<Question> questions, CancellationToken ct)
    {
        var rows = await questions
            .Select(x => new
            {
                x.Id,
                x.BankId,
                x.Text,
                x.ImageUrl,
                x.Order,
                Options = x.Options
                    .OrderBy(o => o.Order)
                    .Select(o => new { o.Id, o.Text, o.IsCorrect, o.Order })
                    .ToList(),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new QuestionDto(
                r.Id, r.BankId, r.Text, r.ImageUrl, r.Order,
                r.Options.Select(o => new QuestionOptionDto(o.Id, o.Text, o.IsCorrect, o.Order)).ToList()))
            .ToList();
    }

    // =====================================================================
    //  Audit snapshots — what the journal shows
    // =====================================================================

    public static object Snapshot(QuestionBank b) => new
    {
        b.Grade,
        b.SubjectId,
        b.QuestionsPerTest,
        b.TimeLimitMin,
        b.PointsPerCorrect,
        b.IsArchived,
    };

    /// <summary>
    /// The question as the journal shows it, the correct letter included — the
    /// audit log is admin-only (<c>AuditController</c>), and "who changed the
    /// answer key" is exactly what it is for.
    /// </summary>
    public static object Snapshot(string text, string? imageUrl, IEnumerable<(string Text, bool IsCorrect)> options)
    {
        var list = options.ToList();
        var correct = list.FindIndex(o => o.IsCorrect);
        return new
        {
            Text = text,
            ImageUrl = imageUrl,
            Options = list.Select((o, i) => $"{OptionLetter(i)}) {o.Text}").ToList(),
            Correct = correct < 0 ? null : OptionLetter(correct),
        };
    }

    public static object Snapshot(Question q) =>
        Snapshot(q.Text, q.ImageUrl, q.Options.OrderBy(o => o.Order).Select(o => (o.Text, o.IsCorrect)));

    public static object Snapshot(CheckedQuestion q) =>
        Snapshot(q.Text, q.ImageUrl, q.Options.Select(o => (o.Text, o.IsCorrect)));

    /// <summary>A question as one line of an audit summary: first 60 characters, no line breaks.</summary>
    public static string QuestionLabel(string text)
    {
        var line = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 60 ? line : line[..60] + "…";
    }

    // =====================================================================
    //  Database errors → the same codes (the race)
    // =====================================================================

    /// <summary>A second live bank for the same grade × subject slipped past the check (23505).</summary>
    public static bool IsLiveBankViolation(DbUpdateException ex) => HasSqlState(ex, "23505", LiveBankIndex);

    /// <summary>Two correct options for one question — only a concurrent edit gets here (23505).</summary>
    public static bool IsOneCorrectViolation(DbUpdateException ex) => HasSqlState(ex, "23505", OneCorrectIndex);

    /// <summary>
    /// Any foreign key refused the write (23503). After a delete: an exam
    /// section took the bank, or an attempt drew / answered the question, between
    /// the check and the delete. After an insert: the parent bank vanished.
    /// Each caller knows which of the two its own statement can hit.
    /// </summary>
    public static bool IsForeignKeyViolation(DbUpdateException ex) => HasSqlState(ex, "23503", null);

    /// <summary>
    /// Deleting an option failed because a candidate chose it (23503 on
    /// <see cref="ChosenOptionForeignKey"/>). Separate from
    /// <see cref="IsForeignKeyViolation"/> because a question edit can also hit
    /// a 23503 for a different reason — the question itself deleted meanwhile —
    /// and the two need different sentences.
    /// </summary>
    public static bool IsChosenOptionViolation(DbUpdateException ex) =>
        HasSqlState(ex, "23503", ChosenOptionForeignKey);

    private static bool HasSqlState(DbUpdateException ex, string sqlState, string? contains)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is DbException db && db.SqlState == sqlState
                && (contains is null || inner.Message.Contains(contains, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }
}
