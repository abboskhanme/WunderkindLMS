using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Exams — exam types, exams, participants, manual result entry and the
//  results register.
//  Spec: docs/modules/admission-and-testing.md §3.2, §5.4–§5.8, §6.3,
//  §8.1, §8.4, §8.5, §13 Q4/Q7. Unit: B2.
// ===========================================================================
//
//  WHY STATIC, AND WHY NOT IN DI
//  -----------------------------
//  `Program.cs` belongs to another unit in this wave (§11: the rate policy and
//  the sweep of B3), so no new registration is possible. The project already
//  has the pattern for that: `SurveyService`, `CertificateService` — a static
//  class that takes `IAppDbContext` as a parameter; the controller passes its
//  scoped `AppDbContext`. Nothing here touches `HttpContext`: the caller is an
//  `ExamActor`, so a test (or B3's background sweep) can call every method.
//
//  ERRORS
//  ------
//  Every refusal is an `ExamError` — status, machine `code`, Uzbek `message`
//  (§6 "Errors"), optionally the full list of problems. The database enforces
//  the same rules (CHECKs, unique indexes, FKs); the service checks first so
//  the user reads a sentence instead of a SQLSTATE, and the database stays the
//  last word in a race (two admins at once → 409, never a 500).
//
//  WHAT IS NOT HERE
//  ----------------
//  * Online delivery — invitations, attempts, the public page, force-finish,
//    reset, the attempt review (unit B3). B3 reuses `ExamScoringService` and
//    `WriteResult` below, so there is still one scoring path (§2.1).
//  * The candidate list and the enrol button (unit B5, `LeadsController`).
//  * Question banks (unit B1). Readiness of a bank is re-derived here from the
//    rows (§3.1) rather than borrowed from B1's service: the two units are
//    written in parallel, and the rule is three comparisons.
//  * Closing an exam. `closed` is set by the nightly job when `closes_at`
//    passes (§5.5) — B3's sweep; §6.3 has no endpoint for it.
// ===========================================================================

/// <summary>
/// Who is acting — the audit trail's actor, and <c>scored_by_user_id</c> /
/// <c>revoked_by_user_id</c> / <c>created_by_user_id</c> where a row records it.
/// The controller builds it from the JWT.
/// </summary>
public readonly record struct ExamActor(string? UserId, string Name);

/// <summary>
/// One refusal: HTTP status, machine code, Uzbek message and — when several
/// problems were found at once — all of them.
/// </summary>
public sealed record ExamError(int Status, string Code, string Message, IReadOnlyList<string>? Errors = null)
{
    public static ExamError BadRequest(string code, string message, IReadOnlyList<string>? errors = null) =>
        new(400, code, message, errors);

    public static ExamError Forbidden(string message) => new(403, "forbidden", message);

    public static ExamError NotFound(string code, string message) => new(404, code, message);

    public static ExamError Conflict(string code, string message, IReadOnlyList<string>? errors = null) =>
        new(409, code, message, errors);

    /// <summary>The JSON body: <c>{ code, message }</c>, plus <c>errors</c> when there is a list.</summary>
    public object ToBody() => Errors is { Count: > 0 }
        ? new { code = Code, message = Message, errors = Errors }
        : new { code = Code, message = Message };
}

/// <summary>A value, or the reason there is none.</summary>
public readonly record struct ExamOutcome<T>(T? Value, ExamError? Error)
{
    public static implicit operator ExamOutcome<T>(T value) => new(value, null);
    public static implicit operator ExamOutcome<T>(ExamError error) => new(default, error);
}

/// <summary>
/// The rules of the Imtihonlar module (§6.3). One public method per endpoint;
/// the controllers only translate HTTP.
/// </summary>
public static class ExamService
{
    // =====================================================================
    //  Audit labels. Wiring moves them into AuditService as EntityExam,
    //  EntityExamType, EntityExamParticipant, EntityExamResult — that file is
    //  shared and not this unit's to edit (listed in the B2 hand-off). The
    //  VALUES must not change: audit rows are found by this text.
    // =====================================================================

    /// <summary>Exam created / edited / published / cancelled.</summary>
    public const string AuditEntityExam = "Exam";

    /// <summary>Exam type catalogue.</summary>
    public const string AuditEntityExamType = "ExamType";

    /// <summary>Participants added to / removed from an exam.</summary>
    public const string AuditEntityExamParticipant = "ExamParticipant";

    /// <summary>
    /// A manual result changed (grid save or import) — one row per participant,
    /// with <c>student_id</c> set for pupils, so a pupil's history shows who
    /// typed which score.
    /// </summary>
    public const string AuditEntityExamResult = "ExamResult";

    // =====================================================================
    //  Limits
    // =====================================================================

    /// <summary>§13 Q7 — publish is refused above this many questions per sitting.</summary>
    public const int MaxQuestionsPerExam = 200;

    /// <summary>§6.3 — the entry grid is bare and capped; above it the endpoint says "split the exam".</summary>
    public const int EntryTableCap = 500;

    /// <summary>Largest ceiling a <c>numeric(6,2)</c> section holds.</summary>
    public const decimal MaxSectionScore = 9999.99m;

    /// <summary>
    /// A whole sitting's time limit. The bank's own limit is 1–600
    /// (<c>ck_question_banks_time_limit_min</c>); an exam sums several banks,
    /// so its ceiling is one day.
    /// </summary>
    public const int MaxTimeLimitMin = 1440;

    public const int TitleMaxLength = 200;
    public const int TypeNameMaxLength = 120;
    public const int DescriptionMaxLength = 500;
    public const int ReasonMaxLength = 500;

    /// <summary>§6 paging: default 50, at most 200.</summary>
    public const int DefaultPageLimit = 50;
    public const int MaxPageLimit = 200;

    /// <summary>
    /// Ceiling of one results export — the reasoning of
    /// <see cref="SurveySubmissionQuery.MaxExportRows"/>. The file says so on its
    /// last line when the cap bites.
    /// </summary>
    public const int MaxExportRows = 10_000;

    /// <summary>
    /// Shown for a candidate whose lead no longer exists (enrolled or deleted,
    /// §2.2): the sitting and its score survive, the name does not.
    /// </summary>
    public const string DeletedCandidateName = "Nomzod (o'chirilgan)";

    // =====================================================================
    //  Messages (Uzbek — the client shows the server's sentence as is)
    // =====================================================================

    public const string ExamNotFoundMessage = "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin";
    public const string ExamTypeNotFoundMessage = "Imtihon turi topilmadi — u o'chirilgan bo'lishi mumkin";
    public const string ParticipantNotFoundMessage = "Ishtirokchi topilmadi — sahifani yangilang";
    public const string ValidationMessage = "Ma'lumotlarni tekshiring";

    public const string TypeNameRequiredMessage = "Imtihon turining nomini kiriting";
    public const string TypeNameTakenMessage = "Bu nomli imtihon turi allaqachon bor";
    public const string TypeInUseMessage =
        "Bu tur imtihonlarda ishlatilgan — o'chirib bo'lmaydi. Uni faolsizlantiring.";

    public const string TitleRequiredMessage = "Imtihon nomini kiriting";
    public const string BlockOnlineMessage =
        "Blok test hozircha faqat qog'ozda o'tkaziladi — natijalar qo'lda kiritiladi";
    public const string KindImmutableMessage =
        "Imtihon ko'rinishi va o'tkazilish usulini yaratilgandan keyin o'zgartirib bo'lmaydi";
    public const string NotEditableMessage = "Yopilgan yoki bekor qilingan imtihonni tahrirlab bo'lmaydi";
    public const string SectionsFrozenMessage =
        "Imtihon e'lon qilingan — fanlar ro'yxatini endi o'zgartirib bo'lmaydi";
    public const string SectionsHaveResultsMessage =
        "Imtihonga natija kiritilgan — fanlar ro'yxati va maksimal ballarni endi o'zgartirib bo'lmaydi";

    public const string CancelReasonRequiredMessage = "Bekor qilish sababini yozing";
    public const string CannotCancelMessage = "Bu imtihon yopilgan yoki allaqachon bekor qilingan";

    public const string RosterClosedMessage =
        "Bekor qilingan yoki yopilgan imtihonning ishtirokchilar ro'yxatini o'zgartirib bo'lmaydi";
    public const string NothingToAddMessage = "Qo'shiladigan ishtirokchi tanlanmagan";
    public const string AdmissionTakesLeadsMessage = "Qabul imtihoniga faqat nomzodlar (lidlar) biriktiriladi";
    public const string BlockTakesStudentsMessage = "Blok testga faqat o'quvchilar yoki sinflar qo'shiladi";
    public const string AdmissionPermMessage =
        "Qabul imtihoniga nomzod biriktirish uchun 'Qabul' ruxsati ham kerak";
    public const string LeadNotFoundMessage = "Tanlangan nomzod topilmadi — ro'yxatni yangilang";
    public const string StudentNotFoundMessage = "Tanlangan o'quvchi topilmadi — ro'yxatni yangilang";
    public const string ClassNotFoundMessage = "Tanlangan sinf topilmadi — ro'yxatni yangilang";
    public const string AttemptExistsMessage =
        "Ishtirokchi imtihonni boshlagan — uni ro'yxatdan chiqarib bo'lmaydi";
    public const string ConcurrentChangeMessage =
        "Ma'lumot shu paytda boshqa foydalanuvchi tomonidan o'zgartirildi — sahifani yangilab, qayta urinib ko'ring";

    public const string EntryOnlineMessage =
        "Onlayn imtihon ballarini tizim o'zi hisoblaydi — ularni qo'lda kiritib bo'lmaydi";
    public const string EntryCancelledMessage = "Imtihon bekor qilingan — natijalarni o'zgartirib bo'lmaydi";

    // =====================================================================
    //  Labels (the words of examLabels.ts — one spelling per status)
    // =====================================================================

    public static string ExamStatusLabel(string status) => status switch
    {
        ExamStatus.Draft => "Qoralama",
        ExamStatus.Published => "E'lon qilingan",
        ExamStatus.Closed => "Yopilgan",
        ExamStatus.Cancelled => "Bekor qilingan",
        _ => status,
    };

    public static string ParticipantStatusLabel(string status) => status switch
    {
        ExamParticipantStatus.Assigned => "Kutilmoqda",
        ExamParticipantStatus.InProgress => "Topshirmoqda",
        ExamParticipantStatus.Finished => "Baholangan",
        ExamParticipantStatus.Absent => "Kelmadi",
        ExamParticipantStatus.Cancelled => "Bekor qilingan",
        _ => status,
    };

    // =====================================================================
    //  Exam types (§5.4)
    // =====================================================================

    /// <summary>The whole catalogue, inactive included (the screen has a "faol emas" toggle).</summary>
    public static async Task<List<ExamTypeDto>> ListTypesAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.ExamTypes.AsNoTracking()
            .OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new ExamTypeDto(t.Id, t.Name, t.Description, t.IsActive, t.CreatedAt))
            .ToListAsync(ct);

    public static async Task<ExamOutcome<ExamTypeDto>> CreateTypeAsync(
        IAppDbContext db, ExamActor actor, ExamTypeCreateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (name, description, problem) = ValidateType(request.Name, request.Description);
        if (problem is not null) return problem;
        if (await TypeNameTakenAsync(db, name, exceptId: null, ct)) return TypeNameTaken();

        var type = new ExamType { Name = name, Description = description, IsActive = true };
        db.ExamTypes.Add(type);
        Audit(db, actor, AuditEntityExamType, type.Id, "create", $"Imtihon turi yaratildi: «{type.Name}»",
            after: TypeSnapshot(type));

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return TypeNameTaken(); }

        return ToDto(type);
    }

    public static async Task<ExamOutcome<ExamTypeDto>> UpdateTypeAsync(
        IAppDbContext db, ExamActor actor, string id, ExamTypeUpdateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var type = await db.ExamTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return ExamError.NotFound("exam_type_not_found", ExamTypeNotFoundMessage);

        var (name, description, problem) = ValidateType(request.Name, request.Description);
        if (problem is not null) return problem;
        if (await TypeNameTakenAsync(db, name, exceptId: id, ct)) return TypeNameTaken();

        var before = TypeSnapshot(type);
        type.Name = name;
        type.Description = description;
        if (request.IsActive is { } active) type.IsActive = active;

        Audit(db, actor, AuditEntityExamType, type.Id, "update", $"Imtihon turi tahrirlandi: «{type.Name}»",
            before: before, after: TypeSnapshot(type));

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return TypeNameTaken(); }

        return ToDto(type);
    }

    /// <summary>
    /// Deletes an unused type. A used one is refused with 409 even though the FK
    /// is <c>on delete set null</c>: silently stripping the label from every
    /// exam that carried it is not what "delete" means to the person clicking.
    /// </summary>
    public static async Task<ExamError?> DeleteTypeAsync(
        IAppDbContext db, ExamActor actor, string id, CancellationToken ct = default)
    {
        var type = await db.ExamTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return ExamError.NotFound("exam_type_not_found", ExamTypeNotFoundMessage);

        if (await db.Exams.AsNoTracking().AnyAsync(e => e.ExamTypeId == id, ct))
            return ExamError.Conflict("exam_type_in_use", TypeInUseMessage);

        db.ExamTypes.Remove(type);
        Audit(db, actor, AuditEntityExamType, type.Id, "delete", $"Imtihon turi o'chirildi: «{type.Name}»",
            before: TypeSnapshot(type));
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static (string Name, string Description, ExamError? Problem) ValidateType(string? rawName, string? rawDescription)
    {
        var name = (rawName ?? "").Trim();
        var description = (rawDescription ?? "").Trim();
        if (name.Length == 0)
            return (name, description, ExamError.BadRequest("validation", TypeNameRequiredMessage));
        if (name.Length > TypeNameMaxLength)
            return (name, description, ExamError.BadRequest("validation",
                $"Imtihon turining nomi {TypeNameMaxLength} belgidan oshmasin"));
        if (description.Length > DescriptionMaxLength)
            return (name, description, ExamError.BadRequest("validation",
                $"Izoh {DescriptionMaxLength} belgidan oshmasin"));
        return (name, description, null);
    }

    /// <summary>
    /// Same expression as <c>ux_exam_types_name</c> — <c>lower(btrim(name))</c> —
    /// so "Choraklik" and " choraklik " are one name, as the index says.
    /// </summary>
    private static Task<bool> TypeNameTakenAsync(IAppDbContext db, string name, string? exceptId, CancellationToken ct)
    {
        var key = name.ToLowerInvariant();
        var q = db.ExamTypes.AsNoTracking().Where(t => t.Name.Trim().ToLower() == key);
        if (exceptId is not null) q = q.Where(t => t.Id != exceptId);
        return q.AnyAsync(ct);
    }

    private static ExamError TypeNameTaken() => ExamError.Conflict("exam_type_name_taken", TypeNameTakenMessage);

    private static ExamTypeDto ToDto(ExamType t) => new(t.Id, t.Name, t.Description, t.IsActive, t.CreatedAt);

    private static object TypeSnapshot(ExamType t) => new { t.Name, t.Description, t.IsActive };

    // =====================================================================
    //  Exams — read
    // =====================================================================

    /// <summary><c>GET /api/admin/exams</c> — one page of the register, newest first.</summary>
    public static async Task<ExamOutcome<ExamPageDto<ExamRowDto>>> ListAsync(
        IAppDbContext db, ExamListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, limit) = Paging(query.Page, query.Limit);

        var kind = Clean(query.Kind);
        if (kind is not null && !ExamKind.All.Contains(kind))
            return UnknownFilter("kind", kind, ExamKind.All);
        var status = Clean(query.Status);
        if (status is not null && !ExamStatus.All.Contains(status))
            return UnknownFilter("status", status, ExamStatus.All);
        if (query.From is { } f && query.To is { } t && t < f)
            return ExamError.BadRequest("validation", $"Davr teskari: {f:yyyy-MM-dd} dan {t:yyyy-MM-dd} gacha");

        var q = db.Exams.AsNoTracking();
        if (kind is not null) q = q.Where(e => e.Kind == kind);
        if (status is not null) q = q.Where(e => e.Status == status);
        if (Clean(query.ExamTypeId) is { } typeId) q = q.Where(e => e.ExamTypeId == typeId);
        if (Clean(query.Search)?.ToLowerInvariant() is { } term) q = q.Where(e => e.Title.ToLower().Contains(term));

        // The exam's DAY: `exam_date` (text "YYYY-MM-DD", compares as a date) for
        // a paper exam, the date of `opens_at` for an online one.
        if (query.From is { } from)
        {
            var day = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var start = from.ToDateTime(TimeOnly.MinValue);
            q = q.Where(e => (e.ExamDate != null && string.Compare(e.ExamDate, day) >= 0)
                || (e.ExamDate == null && e.OpensAt >= start));
        }
        if (query.To is { } to)
        {
            var day = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(e => (e.ExamDate != null && string.Compare(e.ExamDate, day) <= 0)
                || (e.ExamDate == null && e.OpensAt < endExclusive));
        }

        var total = await q.CountAsync(ct);
        var exams = await q.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id)
            .Skip((page - 1) * limit).Take(limit)
            .ToListAsync(ct);

        var rows = await RowsAsync(db, exams, ct);
        return new ExamPageDto<ExamRowDto>(rows.Select(r => r.Row).ToList(), total, page, limit);
    }

    /// <summary><c>GET /api/admin/exams/{id}</c> — the exam with its sections and counts.</summary>
    public static async Task<ExamDto?> GetAsync(IAppDbContext db, string id, CancellationToken ct = default)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null) return null;

        var sections = await SectionDtosAsync(db, id, ct);
        var (row, _) = (await RowsAsync(db, [exam], ct))[0];
        return new ExamDto
        {
            Id = row.Id, Title = row.Title, Kind = row.Kind, Delivery = row.Delivery, Status = row.Status,
            ExamTypeId = row.ExamTypeId, ExamTypeName = row.ExamTypeName, Grade = row.Grade,
            ExamDate = row.ExamDate, OpensAt = row.OpensAt, ClosesAt = row.ClosesAt,
            TimeLimitMin = row.TimeLimitMin, CreatedAt = row.CreatedAt,
            SectionCount = sections.Count, ParticipantCount = row.ParticipantCount,
            FinishedCount = row.FinishedCount, AbsentCount = row.AbsentCount,
            Sections = sections,
        };
    }

    /// <summary>
    /// Register rows for a set of exams — four grouped queries whatever the
    /// page size (type names, section counts, participant counts by status).
    /// </summary>
    private static async Task<List<(ExamRowDto Row, Exam Exam)>> RowsAsync(
        IAppDbContext db, IReadOnlyList<Exam> exams, CancellationToken ct)
    {
        var ids = exams.Select(e => e.Id).ToList();
        var typeIds = exams.Where(e => e.ExamTypeId != null).Select(e => e.ExamTypeId!).Distinct().ToList();

        var typeNames = typeIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.ExamTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var sectionCounts = await db.ExamSections.AsNoTracking()
            .Where(s => ids.Contains(s.ExamId))
            .GroupBy(s => s.ExamId)
            .Select(g => new { ExamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ExamId, x => x.Count, ct);

        var byStatus = await db.ExamParticipants.AsNoTracking()
            .Where(p => ids.Contains(p.ExamId))
            .GroupBy(p => new { p.ExamId, p.Status })
            .Select(g => new { g.Key.ExamId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return exams.Select(e =>
        {
            var mine = byStatus.Where(x => x.ExamId == e.Id).ToList();
            var row = new ExamRowDto
            {
                Id = e.Id, Title = e.Title, Kind = e.Kind, Delivery = e.Delivery, Status = e.Status,
                ExamTypeId = e.ExamTypeId,
                ExamTypeName = e.ExamTypeId is { } tid ? typeNames.GetValueOrDefault(tid) : null,
                Grade = e.Grade, ExamDate = e.ExamDate, OpensAt = e.OpensAt, ClosesAt = e.ClosesAt,
                TimeLimitMin = e.TimeLimitMin, CreatedAt = e.CreatedAt,
                SectionCount = sectionCounts.GetValueOrDefault(e.Id),
                ParticipantCount = mine.Sum(x => x.Count),
                FinishedCount = mine.Where(x => x.Status == ExamParticipantStatus.Finished).Sum(x => x.Count),
                AbsentCount = mine.Where(x => x.Status == ExamParticipantStatus.Absent).Sum(x => x.Count),
            };
            return (row, e);
        }).ToList();
    }

    private static async Task<List<ExamSectionDto>> SectionDtosAsync(IAppDbContext db, string examId, CancellationToken ct)
    {
        var rows = await (
            from s in db.ExamSections.AsNoTracking()
            join subject in db.Subjects.AsNoTracking() on s.SubjectId equals subject.Id
            join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
            where s.ExamId == examId
            orderby s.Order, s.Id
            select new { Section = s, SubjectName = subject.Name, e.Delivery, e.Status })
            .ToListAsync(ct);

        return rows.Select(x => new ExamSectionDto(
            x.Section.Id, x.Section.SubjectId, x.SubjectName, x.Section.BankId, x.Section.QuestionCount,
            x.Section.PointsPerCorrect,
            // An online section's ceiling is written at publish; before that the
            // stored 0 is a placeholder, and showing "maks. 0" would be a lie.
            x.Delivery == ExamDelivery.Online && x.Status == ExamStatus.Draft ? null : x.Section.MaxScore,
            x.Section.Order)).ToList();
    }

    // =====================================================================
    //  Exams — create / update
    // =====================================================================

    /// <summary><c>POST /api/admin/exams</c> — a new draft.</summary>
    public static async Task<ExamOutcome<ExamDto>> CreateAsync(
        IAppDbContext db, ExamActor actor, ExamUpsertRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = Clean(request.Kind);
        var delivery = Clean(request.Delivery);
        if (kind is null || !ExamKind.All.Contains(kind))
            return ExamError.BadRequest("validation", "Imtihon ko'rinishini tanlang: 'block' yoki 'admission'");
        if (delivery is null || !ExamDelivery.All.Contains(delivery))
            return ExamError.BadRequest("validation", "O'tkazilish usulini tanlang: 'manual' yoki 'online'");
        // §13 Q4: the block test is a paper exam for now. The schema would take
        // an online one, but there is no pupil-facing page to sit it on (§9.3).
        if (kind == ExamKind.Block && delivery == ExamDelivery.Online)
            return ExamError.BadRequest("validation", BlockOnlineMessage);

        var (header, headerErrors) = await ValidateHeaderAsync(db, request, kind, delivery, ExamStatus.Draft, ct);
        var (sections, sectionErrors) = await ValidateSectionsAsync(db, request.Sections, delivery, header.Grade, ct);
        if (headerErrors.Concat(sectionErrors).ToList() is { Count: > 0 } errors)
            return ExamError.BadRequest("validation", errors[0], errors);

        var exam = new Exam
        {
            Kind = kind,
            Delivery = delivery,
            Status = ExamStatus.Draft,
            CreatedByUserId = actor.UserId,
        };
        ApplyHeader(exam, header);
        foreach (var s in sections)
        {
            exam.Sections.Add(new ExamSection
            {
                ExamId = exam.Id, SubjectId = s.SubjectId, BankId = s.BankId,
                QuestionCount = s.QuestionCount, MaxScore = s.MaxScore, Order = s.Order,
            });
        }
        db.Exams.Add(exam);
        Audit(db, actor, AuditEntityExam, exam.Id, "create", $"Imtihon yaratildi: «{exam.Title}»",
            after: ExamSnapshot(exam));
        await db.SaveChangesAsync(ct);

        return (await GetAsync(db, exam.Id, ct))!;
    }

    /// <summary>
    /// <c>PUT /api/admin/exams/{id}</c>. The header (title, type, date, window,
    /// time limit) stays editable while the exam is live; the sections are
    /// frozen once it is published (§5.5) — and, for a draft paper exam, once
    /// any score has been typed against them, since a changed ceiling would
    /// leave stored points above it. A request that sends the frozen sections
    /// back unchanged (the form does exactly that) is accepted.
    /// </summary>
    public static async Task<ExamOutcome<ExamDto>> UpdateAsync(
        IAppDbContext db, ExamActor actor, string id, ExamUpsertRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exam = await db.Exams.Include(e => e.Sections).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamNotFoundMessage);
        if (exam.Status is ExamStatus.Closed or ExamStatus.Cancelled)
            return ExamError.Conflict("exam_not_editable", NotEditableMessage);

        var kind = Clean(request.Kind) ?? exam.Kind;
        var delivery = Clean(request.Delivery) ?? exam.Delivery;
        if (kind != exam.Kind || delivery != exam.Delivery)
            return ExamError.BadRequest("validation", KindImmutableMessage);

        var (header, headerErrors) = await ValidateHeaderAsync(db, request, kind, delivery, exam.Status, ct);

        var hasResults = exam.Status == ExamStatus.Draft && await HasScoresAsync(db, exam.Id, ct);
        var frozen = exam.Status != ExamStatus.Draft || hasResults;
        List<SectionPlan> sections = [];
        if (frozen)
        {
            // The grade picks the banks of an admission exam, so it is part of
            // the frozen set.
            var gradeChanged = exam.Kind == ExamKind.Admission && header.Grade != exam.Grade;
            if (gradeChanged || SectionsDiffer(exam, request.Sections))
            {
                return exam.Status == ExamStatus.Draft
                    ? ExamError.Conflict("sections_have_results", SectionsHaveResultsMessage)
                    : ExamError.Conflict("sections_frozen", SectionsFrozenMessage);
            }
        }
        else
        {
            var (plan, sectionErrors) = await ValidateSectionsAsync(db, request.Sections, delivery, header.Grade, ct);
            headerErrors.AddRange(sectionErrors);
            sections = plan;
        }
        if (headerErrors.Count > 0) return ExamError.BadRequest("validation", headerErrors[0], headerErrors);

        var before = ExamSnapshot(exam);
        ApplyHeader(exam, header);
        if (!frozen) ReplaceSections(db, exam, sections);

        Audit(db, actor, AuditEntityExam, exam.Id, "update", $"Imtihon tahrirlandi: «{exam.Title}»",
            before: before, after: ExamSnapshot(exam));
        await db.SaveChangesAsync(ct);

        return (await GetAsync(db, exam.Id, ct))!;
    }

    /// <summary>The validated header of an upsert — everything except the sections.</summary>
    private sealed record HeaderPlan(
        string Title, string? ExamTypeId, int? Grade, string? ExamDate,
        DateTime? OpensAt, DateTime? ClosesAt, int? TimeLimitMin);

    /// <summary>One validated section, in final order.</summary>
    private sealed record SectionPlan(string SubjectId, string? BankId, int? QuestionCount, decimal MaxScore, int Order);

    private static async Task<(HeaderPlan Plan, List<string> Errors)> ValidateHeaderAsync(
        IAppDbContext db, ExamUpsertRequest r, string kind, string delivery, string status, CancellationToken ct)
    {
        var errors = new List<string>();

        var title = (r.Title ?? "").Trim();
        if (title.Length == 0) errors.Add(TitleRequiredMessage);
        else if (title.Length > TitleMaxLength) errors.Add($"Imtihon nomi {TitleMaxLength} belgidan oshmasin");

        var typeId = Clean(r.ExamTypeId);
        if (typeId is not null && !await db.ExamTypes.AsNoTracking().AnyAsync(t => t.Id == typeId, ct))
            errors.Add("Tanlangan imtihon turi topilmadi — ro'yxatni yangilang");

        int? grade = null;
        if (kind == ExamKind.Admission)
        {
            if (r.Grade is not { } g || g < 0 || g > 11) errors.Add("Qaysi sinfga qabul ekanini tanlang (0–11)");
            else grade = g;
        }

        string? examDate = null;
        DateTime? opensAt = null, closesAt = null;
        int? timeLimit = null;
        if (delivery == ExamDelivery.Manual)
        {
            var raw = Clean(r.ExamDate);
            if (raw is null) errors.Add("Imtihon sanasini tanlang");
            else if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                errors.Add("Imtihon sanasi YYYY-MM-DD ko'rinishida bo'lishi kerak");
            else examDate = raw;
        }
        else
        {
            opensAt = ToWallClock(r.OpensAt);
            closesAt = ToWallClock(r.ClosesAt);
            timeLimit = r.TimeLimitMin;
            if (opensAt is { } o && closesAt is { } c && c <= o)
                errors.Add("Yopilish vaqti ochilish vaqtidan keyin bo'lishi kerak");
            if (timeLimit is { } m && (m < 1 || m > MaxTimeLimitMin))
                errors.Add($"Imtihon vaqti 1 dan {MaxTimeLimitMin} daqiqagacha bo'lishi kerak");
            // A draft may still lack its window (§8.1 fills the time limit in at
            // publish); a published online exam may not (`ck_exams_online_window`).
            if (status != ExamStatus.Draft && (opensAt is null || closesAt is null || timeLimit is null))
                errors.Add("E'lon qilingan onlayn imtihonning ochilish, yopilish vaqti va davomiyligi bo'lishi shart");
        }

        return (new HeaderPlan(title, typeId, grade, examDate, opensAt, closesAt, timeLimit), errors);
    }

    /// <summary>
    /// Validates and normalises the sections: sorted by <c>order</c> (ties keep
    /// the request order), renumbered 0…n-1. Two queries: subjects, banks.
    /// </summary>
    private static async Task<(List<SectionPlan> Plan, List<string> Errors)> ValidateSectionsAsync(
        IAppDbContext db, IReadOnlyList<ExamSectionInput>? input, string delivery, int? grade, CancellationToken ct)
    {
        var errors = new List<string>();
        var ordered = (input ?? []).Select((s, i) => (s, i)).OrderBy(x => x.s.Order).ThenBy(x => x.i)
            .Select(x => x.s).ToList();

        var subjectIds = ordered.Select(s => Clean(s.SubjectId)).OfType<string>().Distinct().ToList();
        var bankIds = ordered.Select(s => Clean(s.BankId)).OfType<string>().Distinct().ToList();
        var subjects = subjectIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var banks = bankIds.Count == 0 || delivery != ExamDelivery.Online
            ? new Dictionary<string, QuestionBank>()
            : await db.QuestionBanks.AsNoTracking().Where(b => bankIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct);

        var plan = new List<SectionPlan>(ordered.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var n = i + 1;
            var subjectId = Clean(s.SubjectId);

            if (delivery == ExamDelivery.Online)
            {
                var bankId = Clean(s.BankId);
                if (bankId is null) { errors.Add($"{n}-fan: test bazasini tanlang"); continue; }
                if (!banks.TryGetValue(bankId, out var bank)) { errors.Add($"{n}-fan: test bazasi topilmadi"); continue; }
                if (bank.IsArchived) { errors.Add($"{n}-fan: test bazasi arxivlangan"); continue; }
                if (grade is { } g && bank.Grade != g)
                {
                    errors.Add($"{n}-fan: test bazasi boshqa sinf uchun ({bank.Grade}-sinf)");
                    continue;
                }
                // The bank decides the subject; a request that names a
                // different one is contradicting itself.
                if (subjectId is not null && subjectId != bank.SubjectId)
                {
                    errors.Add($"{n}-fan: tanlangan fan test bazasining faniga mos emas");
                    continue;
                }
                subjectId = bank.SubjectId;
                if (s.QuestionCount is { } qc && qc < 1)
                {
                    errors.Add($"{n}-fan: savollar soni kamida 1 bo'lsin");
                    continue;
                }
                if (!seen.Add(subjectId)) { errors.Add($"{n}-fan takrorlangan"); continue; }
                // The ceiling is written at publish (§8.1); 0 until then.
                plan.Add(new SectionPlan(subjectId, bankId, s.QuestionCount, 0m, plan.Count));
            }
            else
            {
                if (subjectId is null) { errors.Add($"{n}-fan: fanni tanlang"); continue; }
                if (!subjects.ContainsKey(subjectId)) { errors.Add($"{n}-fan: fan topilmadi"); continue; }
                if (!seen.Add(subjectId)) { errors.Add($"{n}-fan takrorlangan"); continue; }
                if (s.MaxScore is not { } rawMax) { errors.Add($"{n}-fan: maksimal ballni kiriting"); continue; }
                var max = ExamScoringService.RoundPoints(rawMax);
                if (max <= 0m || max > MaxSectionScore)
                {
                    errors.Add($"{n}-fan: maksimal ball 0 dan katta va {ExamScoringService.Format(MaxSectionScore)} dan oshmasin");
                    continue;
                }
                plan.Add(new SectionPlan(subjectId, null, null, max, plan.Count));
            }
        }

        return (plan, errors);
    }

    /// <summary>
    /// Does the request describe different sections from the stored ones?
    /// Compared in order: subject, and bank + question count (online) or
    /// ceiling (manual). An online section's ceiling is the server's own
    /// (written at publish), so whatever the client echoes back for it is ignored.
    /// </summary>
    private static bool SectionsDiffer(Exam exam, IReadOnlyList<ExamSectionInput>? input)
    {
        var stored = exam.Sections.OrderBy(s => s.Order).ThenBy(s => s.Id).ToList();
        var sent = (input ?? []).Select((s, i) => (s, i)).OrderBy(x => x.s.Order).ThenBy(x => x.i)
            .Select(x => x.s).ToList();
        if (stored.Count != sent.Count) return true;

        for (var i = 0; i < stored.Count; i++)
        {
            var a = stored[i];
            var b = sent[i];
            var subject = Clean(b.SubjectId);
            if (subject is not null && subject != a.SubjectId) return true;
            if (exam.Delivery == ExamDelivery.Online)
            {
                if (Clean(b.BankId) != a.BankId) return true;
                if (b.QuestionCount is { } qc && qc != a.QuestionCount) return true;
            }
            else
            {
                if (subject is null) return true;
                if (b.MaxScore is not { } max || ExamScoringService.RoundPoints(max) != a.MaxScore) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Applies the planned sections to a draft, keyed by subject so a section
    /// that stays keeps its id (and anything already pointing at it).
    /// </summary>
    private static void ReplaceSections(IAppDbContext db, Exam exam, List<SectionPlan> plan)
    {
        var bySubject = exam.Sections.ToDictionary(s => s.SubjectId, StringComparer.Ordinal);
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in plan)
        {
            keep.Add(p.SubjectId);
            if (bySubject.TryGetValue(p.SubjectId, out var s))
            {
                s.BankId = p.BankId;
                s.QuestionCount = p.QuestionCount;
                s.MaxScore = p.MaxScore;
                s.PointsPerCorrect = null;
                s.Order = p.Order;
            }
            else
            {
                exam.Sections.Add(new ExamSection
                {
                    ExamId = exam.Id, SubjectId = p.SubjectId, BankId = p.BankId,
                    QuestionCount = p.QuestionCount, MaxScore = p.MaxScore, Order = p.Order,
                });
            }
        }

        foreach (var gone in exam.Sections.Where(s => !keep.Contains(s.SubjectId)).ToList())
        {
            exam.Sections.Remove(gone);
            db.ExamSections.Remove(gone);
        }
    }

    private static void ApplyHeader(Exam exam, HeaderPlan h)
    {
        exam.Title = h.Title;
        exam.ExamTypeId = h.ExamTypeId;
        exam.Grade = h.Grade;
        exam.ExamDate = h.ExamDate;
        exam.OpensAt = h.OpensAt;
        exam.ClosesAt = h.ClosesAt;
        exam.TimeLimitMin = h.TimeLimitMin;
    }

    private static Task<bool> HasScoresAsync(IAppDbContext db, string examId, CancellationToken ct) =>
        (from score in db.ExamSectionScores.AsNoTracking()
         join section in db.ExamSections.AsNoTracking() on score.SectionId equals section.Id
         where section.ExamId == examId
         select score).AnyAsync(ct);

    // =====================================================================
    //  Publish (§8.1) and cancel (§5.5)
    // =====================================================================

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/publish</c>. Every §8.1 rule is checked
    /// and ALL failures are reported (409, the first one as <c>code</c> and
    /// <c>message</c>, the rest in <c>errors</c>) — fixing one bank at a time
    /// and republishing to discover the next is a poor way to spend a morning.
    ///
    /// <para>
    /// On success, in one save: each online section gets the bank's
    /// <c>points_per_correct</c> copied in and its ceiling written
    /// (<c>question_count × points_per_correct</c>); the exam's time limit is the
    /// sum of the banks' if still empty; <c>status = 'published'</c>. The
    /// sections are frozen from here on (§5.5).
    /// </para>
    /// </summary>
    public static async Task<ExamOutcome<ExamDto>> PublishAsync(
        IAppDbContext db, ExamActor actor, string id, CancellationToken ct = default)
    {
        var exam = await db.Exams.Include(e => e.Sections).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamNotFoundMessage);
        if (exam.Status != ExamStatus.Draft)
            return ExamError.Conflict("not_draft",
                $"Faqat qoralama holatidagi imtihonni e'lon qilish mumkin (hozir: {ExamStatusLabel(exam.Status)})");

        var problems = new List<(string Code, string Message)>();
        var sections = exam.Sections.OrderBy(s => s.Order).ThenBy(s => s.Id).ToList();
        if (sections.Count == 0) problems.Add(("no_sections", "Imtihonda kamida bitta fan bo'lishi kerak"));

        var subjectIds = sections.Select(s => s.SubjectId).Distinct().ToList();
        var subjectNames = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        string NameOf(ExamSection s) => subjectNames.GetValueOrDefault(s.SubjectId) ?? s.SubjectId;

        // What each online section will be published with — applied only when
        // there are no problems at all.
        var resolved = new Dictionary<string, (int Count, decimal PerCorrect, decimal Max)>(StringComparer.Ordinal);
        int? summedMinutes = 0;

        if (exam.Delivery == ExamDelivery.Manual)
        {
            foreach (var s in sections.Where(s => s.MaxScore <= 0m))
                problems.Add(("bad_max_score", $"{NameOf(s)}: maksimal ball 0 dan katta bo'lishi kerak"));
        }
        else
        {
            var bankIds = sections.Where(s => s.BankId != null).Select(s => s.BankId!).Distinct().ToList();
            var banks = await db.QuestionBanks.AsNoTracking().Where(b => bankIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct);
            var counts = await db.Questions.AsNoTracking().Where(q => bankIds.Contains(q.BankId))
                .GroupBy(q => q.BankId)
                .Select(g => new { BankId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BankId, x => x.Count, ct);
            // §8.1 rule 4: 2–6 options and exactly one correct, per question.
            // The database stops two correct options; zero, one option or seven
            // are only stopped by the question form — re-checked here, because
            // a paper drawn from a broken question cannot be graded.
            var broken = await db.Questions.AsNoTracking().Where(q => bankIds.Contains(q.BankId))
                .Select(q => new
                {
                    q.BankId,
                    Options = q.Options.Count(),
                    Correct = q.Options.Count(o => o.IsCorrect),
                })
                .Where(x => x.Options < 2 || x.Options > 6 || x.Correct != 1)
                .GroupBy(x => x.BankId)
                .Select(g => new { BankId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BankId, x => x.Count, ct);

            foreach (var s in sections)
            {
                var name = NameOf(s);
                if (s.BankId is null || !banks.TryGetValue(s.BankId, out var bank))
                {
                    problems.Add(("bank_missing", $"{name}: test bazasi tanlanmagan"));
                    summedMinutes = null;
                    continue;
                }
                if (bank.IsArchived)
                {
                    problems.Add(("bank_archived", $"{name}: test bazasi arxivlangan"));
                    continue;
                }
                if (bank.QuestionsPerTest is not { } perTest || bank.TimeLimitMin is not { } minutes
                    || bank.PointsPerCorrect is not { } perCorrect)
                {
                    problems.Add(("bank_unconfigured",
                        $"{name}: test bazasi sozlanmagan — testdagi savollar soni, vaqt va ball kiritilishi kerak"));
                    summedMinutes = null;
                    continue;
                }

                var inBank = counts.GetValueOrDefault(bank.Id);
                var drawn = s.QuestionCount ?? perTest;
                var needed = Math.Max(perTest, drawn);
                if (inBank < needed)
                {
                    problems.Add(("not_enough_questions",
                        $"{name}: bazada {inBank} ta savol bor, testga {needed} ta kerak"));
                }
                if (broken.GetValueOrDefault(bank.Id) is var bad and > 0)
                {
                    problems.Add(("bad_questions",
                        $"{name}: {bad} ta savolda variantlar noto'g'ri — har savolda 2–6 ta variant va bitta to'g'ri javob bo'lishi kerak"));
                }

                var max = ExamScoringService.RoundPoints(drawn * perCorrect);
                if (max > MaxSectionScore)
                {
                    problems.Add(("section_max_too_large",
                        $"{name}: maksimal ball {ExamScoringService.Format(max)} — {ExamScoringService.Format(MaxSectionScore)} dan oshmasligi kerak"));
                }
                resolved[s.Id] = (drawn, perCorrect, max);
                summedMinutes += minutes;
            }

            var totalQuestions = sections.Sum(s => resolved.TryGetValue(s.Id, out var r) ? r.Count : s.QuestionCount ?? 0);
            if (totalQuestions > MaxQuestionsPerExam)
            {
                problems.Add(("too_many_questions",
                    $"Imtihonda jami {totalQuestions} ta savol — bir imtihonda {MaxQuestionsPerExam} tadan ortiq savol bo'lishi mumkin emas"));
            }

            // §8.1 rule 5.
            if (exam.OpensAt is not { } opens || exam.ClosesAt is not { } closes)
            {
                problems.Add(("window_missing", "Imtihon ochiladigan va yopiladigan vaqtni kiriting"));
            }
            else
            {
                if (closes <= opens)
                    problems.Add(("window_order", "Yopilish vaqti ochilish vaqtidan keyin bo'lishi kerak"));
                if (closes <= AppClock.Now)
                    problems.Add(("window_past", "Yopilish vaqti o'tib ketgan — kelajakdagi vaqtni kiriting"));
            }
            if (exam.TimeLimitMin is null && summedMinutes is > MaxTimeLimitMin)
            {
                problems.Add(("time_limit_too_long",
                    $"Bazalar vaqtining yig'indisi {summedMinutes} daqiqa — imtihon vaqtini qo'lda kiriting (ko'pi bilan {MaxTimeLimitMin})"));
            }
        }

        if (problems.Count > 0)
        {
            return ExamError.Conflict(problems[0].Code, problems[0].Message,
                problems.Select(p => p.Message).ToList());
        }

        var before = ExamSnapshot(exam);
        foreach (var s in sections)
        {
            if (!resolved.TryGetValue(s.Id, out var r)) continue;
            s.QuestionCount = r.Count;
            s.PointsPerCorrect = r.PerCorrect;
            s.MaxScore = r.Max;
        }
        if (exam.Delivery == ExamDelivery.Online) exam.TimeLimitMin ??= summedMinutes;
        exam.Status = ExamStatus.Published;

        Audit(db, actor, AuditEntityExam, exam.Id, "publish", $"Imtihon e'lon qilindi: «{exam.Title}»",
            before: before, after: ExamSnapshot(exam));
        await db.SaveChangesAsync(ct);

        return (await GetAsync(db, exam.Id, ct))!;
    }

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/cancel</c> (§5.5, §8.5). In one save:
    /// the exam becomes <c>cancelled</c>; every participant still
    /// <c>assigned</c> or <c>in_progress</c> becomes <c>cancelled</c> (finished
    /// and absent rows keep their result); every live invitation is revoked;
    /// a candidate whose lead is <c>testing</c> goes back to <c>invited</c> —
    /// and nothing else moves: a <c>tested</c> candidate really was tested.
    /// </summary>
    public static async Task<ExamOutcome<ExamDto>> CancelAsync(
        IAppDbContext db, ExamActor actor, string id, string? rawReason, CancellationToken ct = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamNotFoundMessage);
        if (exam.Status is not (ExamStatus.Draft or ExamStatus.Published))
            return ExamError.Conflict("exam_not_cancellable", CannotCancelMessage);

        var reason = (rawReason ?? "").Trim();
        if (reason.Length == 0) return ExamError.BadRequest("validation", CancelReasonRequiredMessage);
        if (reason.Length > ReasonMaxLength)
            return ExamError.BadRequest("validation", $"Sabab {ReasonMaxLength} belgidan oshmasin");

        var now = AppClock.Now;
        var unfinished = await db.ExamParticipants
            .Where(p => p.ExamId == id
                && (p.Status == ExamParticipantStatus.Assigned || p.Status == ExamParticipantStatus.InProgress))
            .ToListAsync(ct);
        foreach (var p in unfinished) p.Status = ExamParticipantStatus.Cancelled;

        var leadIds = unfinished.Where(p => p.LeadId != null).Select(p => p.LeadId!).ToList();
        if (leadIds.Count > 0)
        {
            var testing = await db.Leads
                .Where(l => leadIds.Contains(l.Id) && l.AdmissionStatus == LeadAdmissionStatus.Testing)
                .ToListAsync(ct);
            foreach (var lead in testing) lead.AdmissionStatus = LeadAdmissionStatus.Invited;
        }

        var live = await (
            from inv in db.ExamInvitations
            join p in db.ExamParticipants on inv.ParticipantId equals p.Id
            where p.ExamId == id && inv.RevokedAt == null
            select inv).ToListAsync(ct);
        foreach (var inv in live)
        {
            inv.RevokedAt = now;
            inv.RevokedByUserId = actor.UserId;
        }

        var before = exam.Status;
        exam.Status = ExamStatus.Cancelled;
        Audit(db, actor, AuditEntityExam, exam.Id, "cancel",
            $"Imtihon bekor qilindi: «{exam.Title}». Sabab: {reason}",
            before: new { Status = before },
            after: new
            {
                exam.Status,
                Reason = reason,
                CancelledParticipants = unfinished.Count,
                RevokedInvitations = live.Count,
            });
        await db.SaveChangesAsync(ct);

        return (await GetAsync(db, exam.Id, ct))!;
    }

    // =====================================================================
    //  Participants (§5.7)
    // =====================================================================

    /// <summary>
    /// A participant with the names a screen needs, joined once for the whole
    /// query. A settable-property class (not a positional record) so EF can
    /// keep composing filters and ordering over it in SQL.
    /// </summary>
    private sealed class ParticipantView
    {
        public ExamParticipant P { get; init; } = null!;
        public string? StudentName { get; init; }
        public string? StudentClassName { get; init; }
        public string? LeadName { get; init; }
        public string? ClassName { get; init; }
    }

    private static IQueryable<ParticipantView> ParticipantViews(IAppDbContext db) =>
        from p in db.ExamParticipants.AsNoTracking()
        join s in db.Students.AsNoTracking() on p.StudentId equals s.Id into sj
        from s in sj.DefaultIfEmpty()
        join l in db.Leads.AsNoTracking() on p.LeadId equals l.Id into lj
        from l in lj.DefaultIfEmpty()
        join c in db.Classes.AsNoTracking() on p.ClassId equals c.Id into cj
        from c in cj.DefaultIfEmpty()
        select new ParticipantView
        {
            P = p,
            StudentName = s == null ? null : s.FullName,
            StudentClassName = s == null ? null : s.ClassName,
            LeadName = l == null ? null : l.FullName,
            ClassName = c == null ? null : c.Name,
        };

    private static string DisplayName(ExamParticipant p, string? studentName, string? leadName) =>
        studentName ?? leadName
        ?? (p.ParticipantKind == ExamParticipantKind.Lead ? DeletedCandidateName : "—");

    /// <summary>The class snapshot of §5.7; a pupil whose snapshot class was deleted falls back to their current class.</summary>
    private static string? DisplayClass(string? className, string? studentClassName) =>
        className ?? (string.IsNullOrWhiteSpace(studentClassName) ? null : studentClassName);

    /// <summary><c>GET /api/admin/exams/{id}/participants</c> — by class, then name.</summary>
    public static async Task<ExamOutcome<ExamPageDto<ExamParticipantRowDto>>> ListParticipantsAsync(
        IAppDbContext db, string examId, ExamParticipantListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId, ct))
            return ExamError.NotFound("exam_not_found", ExamNotFoundMessage);

        var (page, limit) = Paging(query.Page, query.Limit);
        var status = Clean(query.Status);
        if (status is not null && !ExamParticipantStatus.All.Contains(status))
            return UnknownFilter("status", status, ExamParticipantStatus.All);

        var q = ParticipantViews(db).Where(v => v.P.ExamId == examId);
        if (status is not null) q = q.Where(v => v.P.Status == status);
        if (Clean(query.Search)?.ToLowerInvariant() is { } term)
            q = q.Where(v => (v.StudentName ?? v.LeadName ?? "").ToLower().Contains(term));

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(v => v.ClassName ?? v.StudentClassName).ThenBy(v => v.StudentName ?? v.LeadName).ThenBy(v => v.P.Id)
            .Skip((page - 1) * limit).Take(limit)
            .ToListAsync(ct);

        var items = rows.Select(v => new ExamParticipantRowDto(
            v.P.Id, v.P.ParticipantKind, v.P.LeadId, v.P.StudentId,
            DisplayName(v.P, v.StudentName, v.LeadName),
            v.P.ClassId, DisplayClass(v.ClassName, v.StudentClassName),
            v.P.Status, v.P.TotalPoints, v.P.MaxPoints, v.P.Percent, v.P.ScoredAt)).ToList();
        return new ExamPageDto<ExamParticipantRowDto>(items, total, page, limit);
    }

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/participants</c> — idempotent (§6.3): a
    /// person already on the exam is counted in <c>skipped</c>; the partial
    /// unique indexes of §5.7 make a duplicate structurally impossible, and a
    /// race between two admins ends in 409, not in two rows.
    ///
    /// <para>
    /// A block exam takes pupils — named, or every non-archived pupil of the
    /// named classes, each with the class snapshot of §5.7. An admission exam
    /// takes candidates (leads) and moves each lead that is still <c>none</c>
    /// to <c>invited</c> (§8.5); a lead already further along is left alone.
    /// </para>
    /// </summary>
    /// <param name="callerHasAdmission">
    /// §6.3: adding to an admission exam also needs the <c>admission</c>
    /// permission. The controller answers it from the claims.
    /// </param>
    public static async Task<ExamOutcome<ExamAddParticipantsResultDto>> AddParticipantsAsync(
        IAppDbContext db, ExamActor actor, string examId, ExamAddParticipantsRequest request,
        bool callerHasAdmission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamNotFoundMessage);
        if (exam.Status is ExamStatus.Closed or ExamStatus.Cancelled)
            return ExamError.Conflict("roster_closed", RosterClosedMessage);

        var leadIds = Ids(request.LeadIds);
        var studentIds = Ids(request.StudentIds);
        var classIds = Ids(request.ClassIds);
        if (leadIds.Count + studentIds.Count + classIds.Count == 0)
            return ExamError.BadRequest("validation", NothingToAddMessage);

        int added, skipped;
        if (exam.Kind == ExamKind.Admission)
        {
            if (studentIds.Count > 0 || classIds.Count > 0)
                return ExamError.BadRequest("validation", AdmissionTakesLeadsMessage);
            if (!callerHasAdmission) return ExamError.Forbidden(AdmissionPermMessage);

            var leads = await db.Leads.Where(l => leadIds.Contains(l.Id)).ToListAsync(ct);
            if (leads.Count != leadIds.Count) return ExamError.NotFound("lead_not_found", LeadNotFoundMessage);

            var already = (await db.ExamParticipants.AsNoTracking()
                    .Where(p => p.ExamId == examId && p.LeadId != null && leadIds.Contains(p.LeadId))
                    .Select(p => p.LeadId!)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var lead in leads.Where(l => !already.Contains(l.Id)))
            {
                db.ExamParticipants.Add(new ExamParticipant
                {
                    ExamId = examId,
                    ParticipantKind = ExamParticipantKind.Lead,
                    LeadId = lead.Id,
                    Status = ExamParticipantStatus.Assigned,
                });
                if (lead.AdmissionStatus == LeadAdmissionStatus.None)
                    lead.AdmissionStatus = LeadAdmissionStatus.Invited;
            }
            added = leads.Count - already.Count;
            skipped = already.Count;
        }
        else
        {
            if (leadIds.Count > 0) return ExamError.BadRequest("validation", BlockTakesStudentsMessage);

            var classes = classIds.Count == 0
                ? []
                : await db.Classes.AsNoTracking().Where(c => classIds.Contains(c.Id)).ToListAsync(ct);
            if (classes.Count != classIds.Count) return ExamError.NotFound("class_not_found", ClassNotFoundMessage);

            var classNames = classes.Select(c => c.Name).Distinct().ToList();
            var fromClasses = classNames.Count == 0
                ? []
                : await db.Students.AsNoTracking()
                    .Where(s => !s.IsArchived && classNames.Contains(s.ClassName))
                    .Select(s => new { s.Id, s.ClassName, s.IsArchived })
                    .ToListAsync(ct);
            var named = studentIds.Count == 0
                ? []
                : await db.Students.AsNoTracking()
                    .Where(s => studentIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.ClassName, s.IsArchived })
                    .ToListAsync(ct);
            if (named.Count != studentIds.Count) return ExamError.NotFound("student_not_found", StudentNotFoundMessage);

            // Every pupil once; an archived pupil named explicitly is skipped
            // (class expansion never picks one).
            var wanted = fromClasses.Concat(named.Where(s => !s.IsArchived))
                .GroupBy(s => s.Id).Select(g => g.First()).ToList();
            var archivedNamed = named.Count(s => s.IsArchived);

            // Class snapshot (§5.7): pupils link to classes by NAME in this
            // codebase (`Student.ClassName`), so resolve the id through it.
            // A live class wins over an archived one of the same name.
            var neededNames = wanted.Select(s => s.ClassName).Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct().ToList();
            var classIdByName = neededNames.Count == 0
                ? new Dictionary<string, string>()
                : (await db.Classes.AsNoTracking().Where(c => neededNames.Contains(c.Name))
                        .Select(c => new { c.Id, c.Name, c.IsArchived }).ToListAsync(ct))
                    .GroupBy(c => c.Name)
                    .ToDictionary(g => g.Key, g => g.OrderBy(c => c.IsArchived).ThenBy(c => c.Id).First().Id);

            var wantedIds = wanted.Select(s => s.Id).ToList();
            var already = (await db.ExamParticipants.AsNoTracking()
                    .Where(p => p.ExamId == examId && p.StudentId != null && wantedIds.Contains(p.StudentId))
                    .Select(p => p.StudentId!)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var s in wanted.Where(s => !already.Contains(s.Id)))
            {
                db.ExamParticipants.Add(new ExamParticipant
                {
                    ExamId = examId,
                    ParticipantKind = ExamParticipantKind.Student,
                    StudentId = s.Id,
                    ClassId = classIdByName.GetValueOrDefault(s.ClassName),
                    Status = ExamParticipantStatus.Assigned,
                });
            }
            added = wanted.Count - already.Count;
            skipped = already.Count + archivedNamed;
        }

        if (added > 0)
        {
            Audit(db, actor, AuditEntityExamParticipant, examId, "add",
                $"«{exam.Title}» imtihoniga {added} ta ishtirokchi qo'shildi",
                after: new { Added = added, Skipped = skipped, LeadIds = leadIds, StudentIds = studentIds, ClassIds = classIds });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return ExamError.Conflict("concurrent_change", ConcurrentChangeMessage);
            }
        }

        return new ExamAddParticipantsResultDto(added, skipped);
    }

    /// <summary>
    /// <c>DELETE /api/admin/exams/{id}/participants/{pid}</c>. Removes the row
    /// and — by cascade — its scores and invitations. Refused once an online
    /// attempt exists (the answers are evidence; B3's reset is the way back).
    /// A candidate's <c>admission_status</c> is not touched: removing the
    /// sitting does not unmake the invitation (§8.5, "never regresses a fact").
    /// </summary>
    public static async Task<ExamError?> RemoveParticipantAsync(
        IAppDbContext db, ExamActor actor, string examId, string participantId, CancellationToken ct = default)
    {
        var view = await ParticipantViews(db)
            .FirstOrDefaultAsync(v => v.P.Id == participantId && v.P.ExamId == examId, ct);
        if (view is null) return ExamError.NotFound("participant_not_found", ParticipantNotFoundMessage);

        var exam = await db.Exams.AsNoTracking().FirstAsync(e => e.Id == examId, ct);
        if (exam.Status is ExamStatus.Closed or ExamStatus.Cancelled)
            return ExamError.Conflict("roster_closed", RosterClosedMessage);
        if (await db.ExamAttempts.AsNoTracking().AnyAsync(a => a.ParticipantId == participantId, ct))
            return ExamError.Conflict("attempt_exists", AttemptExistsMessage);

        var participant = await db.ExamParticipants.FirstAsync(p => p.Id == participantId, ct);
        var name = DisplayName(participant, view.StudentName, view.LeadName);
        db.ExamParticipants.Remove(participant);
        Audit(db, actor, AuditEntityExamParticipant, examId, "remove",
            $"«{exam.Title}» imtihonidan chiqarildi: {name}",
            before: new
            {
                ParticipantId = participant.Id, participant.ParticipantKind, participant.LeadId,
                participant.StudentId, participant.Status, participant.TotalPoints, participant.MaxPoints,
            },
            studentId: participant.StudentId);
        await db.SaveChangesAsync(ct);
        return null;
    }

    // =====================================================================
    //  Entry table (§6.3 EntryTableDto, §8.4)
    // =====================================================================

    /// <summary>
    /// <c>GET /api/admin/exams/{id}/entry-table</c> — the whole grid, bare.
    /// Above <see cref="EntryTableCap"/> participants: 409 telling the user to
    /// split the exam, never a silently truncated grid (§6.3). Online exams
    /// are shown too (read-only on the client — the engine scores them).
    /// </summary>
    public static async Task<ExamOutcome<ExamEntryTableDto>> GetEntryTableAsync(
        IAppDbContext db, string examId, CancellationToken ct = default)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamNotFoundMessage);

        var count = await db.ExamParticipants.AsNoTracking().CountAsync(p => p.ExamId == examId, ct);
        if (count > EntryTableCap) return EntryTooLarge(count);

        var columns = await ColumnsAsync(db, examId, ct);
        var views = await ParticipantViews(db).Where(v => v.P.ExamId == examId)
            .OrderBy(v => v.ClassName ?? v.StudentClassName).ThenBy(v => v.StudentName ?? v.LeadName).ThenBy(v => v.P.Id)
            .ToListAsync(ct);
        var scores = await ScoresOfExamAsync(db, examId, ct);

        var rows = views.Select(v =>
        {
            var mine = scores.GetValueOrDefault(v.P.Id);
            var cells = columns.ToDictionary(
                c => c.SectionId,
                c => mine is not null && mine.TryGetValue(c.SectionId, out var pts) ? pts : (decimal?)null,
                StringComparer.Ordinal);
            return new ExamEntryRowDto(
                v.P.Id, DisplayName(v.P, v.StudentName, v.LeadName), DisplayClass(v.ClassName, v.StudentClassName),
                v.P.Status, cells, v.P.TotalPoints, v.P.MaxPoints, v.P.Percent);
        }).ToList();

        return new ExamEntryTableDto(exam.Id, exam.Title, exam.ExamDate, columns, rows);
    }

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/entry-table</c> — and the write path of the
    /// Excel import, so both are one implementation (§8.4: "import validates the
    /// same way").
    ///
    /// <para><b>All or nothing.</b> Every row is validated first; one bad cell
    /// is a 400 naming the person and the subject, and nothing is written.</para>
    /// <para><b>Per row:</b> <c>absent</c> → status <c>absent</c>, scores
    /// deleted, summary cleared. Otherwise the listed sections are upserted and
    /// the summary is recomputed over ALL of the participant's scores by
    /// <see cref="ExamScoringService.SummarizeManual"/>; with at least one score
    /// the participant is <c>finished</c>, with none (a pupil un-marked as
    /// absent) back to <c>assigned</c>. <c>scored_by_user_id</c> = the caller.</para>
    /// <para>A row identical to what is stored is accepted and not counted, and
    /// leaves no audit row and no new timestamp.</para>
    /// </summary>
    /// <returns>The number of rows that changed something.</returns>
    public static async Task<ExamOutcome<ExamEntrySaveResultDto>> SaveEntryAsync(
        IAppDbContext db, ExamActor actor, string examId, IReadOnlyList<ExamEntrySaveRow>? rows,
        CancellationToken ct = default)
    {
        var exam = await db.Exams.AsNoTracking().Include(e => e.Sections).FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamNotFoundMessage);
        if (EntryStateProblem(exam) is { } stateProblem) return stateProblem;

        rows ??= [];
        if (rows.Count == 0) return new ExamEntrySaveResultDto(0);
        if (rows.Count > EntryTableCap)
            return ExamError.BadRequest("validation", $"Bir so'rovda ko'pi bilan {EntryTableCap} ta qator saqlanadi");

        // ---- who ----
        var ids = new List<string>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var pid = Clean(rows[i].ParticipantId);
            if (pid is null) return ExamError.BadRequest("validation", $"{i + 1}-qator: ishtirokchi ko'rsatilmagan");
            if (ids.Contains(pid)) return ExamError.BadRequest("validation", $"{i + 1}-qator: ishtirokchi takrorlangan");
            ids.Add(pid);
        }

        var participants = await db.ExamParticipants.Where(p => p.ExamId == examId && ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        if (participants.Count != ids.Count)
            return ExamError.NotFound("participant_not_found", ParticipantNotFoundMessage);
        var names = (await ParticipantViews(db).Where(v => ids.Contains(v.P.Id)).ToListAsync(ct))
            .ToDictionary(v => v.P.Id, v => DisplayName(v.P, v.StudentName, v.LeadName));

        // ---- what ----
        var sections = exam.Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var subjectIds = exam.Sections.Select(s => s.SubjectId).ToList();
        var subjectNames = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var errors = new List<string>();
        var plans = new List<(ExamParticipant P, bool Absent, Dictionary<string, decimal> Points)>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var p = participants[ids[i]];
            var who = names.GetValueOrDefault(p.Id) ?? $"{i + 1}-qator";

            if (p.Status is ExamParticipantStatus.Cancelled or ExamParticipantStatus.InProgress)
            {
                return ExamError.Conflict("participant_locked",
                    $"{who}: {ParticipantStatusLabel(p.Status).ToLowerInvariant()} — natija kiritib bo'lmaydi");
            }

            var points = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var cells = row.Scores ?? [];
            if (row.Absent && cells.Count > 0)
            {
                errors.Add($"{who}: \"Kelmadi\" belgilangan qatorda ball bo'lmasligi kerak");
                continue;
            }
            foreach (var cell in cells)
            {
                var sectionId = Clean(cell.SectionId);
                if (sectionId is null || !sections.TryGetValue(sectionId, out var section))
                {
                    errors.Add($"{who}: noma'lum fan ustuni");
                    continue;
                }
                var subject = subjectNames.GetValueOrDefault(section.SubjectId) ?? section.SubjectId;
                if (points.ContainsKey(sectionId)) { errors.Add($"{who}, {subject}: ball ikki marta yuborilgan"); continue; }
                if (cell.Points is not { } raw) { errors.Add($"{who}, {subject}: ball kiritilmagan"); continue; }

                var value = ExamScoringService.RoundPoints(raw);
                if (ExamScoringService.PointsProblem(value, section.MaxScore) is { } problem)
                {
                    errors.Add($"{who}, {subject}: {problem}");
                    continue;
                }
                points[sectionId] = value;
            }
            plans.Add((p, row.Absent, points));
        }
        if (errors.Count > 0) return ExamError.BadRequest("validation", errors[0], errors);

        // ---- write ----
        var stored = (await db.ExamSectionScores.Where(s => ids.Contains(s.ParticipantId)).ToListAsync(ct))
            .GroupBy(s => s.ParticipantId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var scoringSections = exam.Sections
            .Select(s => new ExamScoringService.Section(s.Id, s.MaxScore, s.PointsPerCorrect)).ToList();
        var leads = await LeadsOfAsync(db, exam, participants.Values, ct);

        var now = AppClock.Now;
        var saved = 0;
        foreach (var (p, absent, points) in plans)
        {
            var current = stored.GetValueOrDefault(p.Id) ?? [];
            var before = ResultSnapshot(p, current);

            if (absent)
            {
                if (p.Status == ExamParticipantStatus.Absent && current.Count == 0) continue;
                foreach (var s in current) db.ExamSectionScores.Remove(s);
                current = [];
                p.Status = ExamParticipantStatus.Absent;
                ClearSummary(p);
                p.ScoredAt = now;
                p.ScoredByUserId = actor.UserId;
            }
            else
            {
                var changed = p.Status == ExamParticipantStatus.Absent
                    || points.Any(kv => current.FirstOrDefault(s => s.SectionId == kv.Key) is not { } s || s.Points != kv.Value);
                if (!changed) continue;

                var merged = current.ToDictionary(s => s.SectionId, s => s.Points, StringComparer.Ordinal);
                foreach (var (sectionId, value) in points) merged[sectionId] = value;

                if (merged.Count > 0)
                {
                    var score = ExamScoringService.SummarizeManual(scoringSections, merged);
                    current = WriteResult(db, p, score, current, actor.UserId, now);
                    if (p.LeadId is { } leadId && leads.TryGetValue(leadId, out var lead)) AdvanceLeadToTested(lead);
                }
                else
                {
                    p.Status = ExamParticipantStatus.Assigned;
                    ClearSummary(p);
                    p.ScoredAt = null;
                    p.ScoredByUserId = null;
                }
            }

            Audit(db, actor, AuditEntityExamResult, p.Id, absent ? "absent" : "score",
                $"«{exam.Title}»: {names.GetValueOrDefault(p.Id)} — "
                + (absent ? "kelmadi" : $"{ExamScoringService.Format(p.TotalPoints ?? 0m)} / {ExamScoringService.Format(p.MaxPoints ?? 0m)} ball"),
                before: before, after: ResultSnapshot(p, current), studentId: p.StudentId);
            saved++;
        }

        if (saved > 0)
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return ExamError.Conflict("concurrent_change", ConcurrentChangeMessage);
            }
        }

        return new ExamEntrySaveResultDto(saved);
    }

    /// <summary>
    /// Writes a score onto a participant: upserts one <c>exam_section_scores</c>
    /// row per <see cref="ExamScoringService.ExamScore.Sections"/> entry
    /// (touching <c>updated_at</c> only where the value changed), sets the
    /// summary columns, <c>status = 'finished'</c>, <c>scored_at</c> and
    /// <c>scored_by_user_id</c>. Section rows NOT in the score are left alone
    /// — the caller decides whether they go.
    ///
    /// <para>
    /// <b>B3 grades through this too</b> (§8.3: one scoring path) —
    /// <c>scoredByUserId = null</c> means "graded by the engine". It does not
    /// save and does not touch the attempt or the lead; the caller does both in
    /// the same unit of work (<see cref="AdvanceLeadToTested"/> for the lead).
    /// </para>
    /// </summary>
    /// <param name="existing">The participant's current section rows, tracked. Empty for a first grading.</param>
    /// <returns>The participant's section rows after the write.</returns>
    public static List<ExamSectionScore> WriteResult(
        IAppDbContext db, ExamParticipant participant, ExamScoringService.ExamScore score,
        IReadOnlyCollection<ExamSectionScore> existing, string? scoredByUserId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(existing);

        var rows = existing.ToDictionary(s => s.SectionId, StringComparer.Ordinal);
        foreach (var s in score.Sections)
        {
            if (rows.TryGetValue(s.SectionId, out var row))
            {
                if (row.Points == s.Points && row.MaxPoints == s.MaxPoints
                    && row.CorrectCount == s.CorrectCount && row.QuestionCount == s.QuestionCount) continue;
                row.Points = s.Points;
                row.MaxPoints = s.MaxPoints;
                row.CorrectCount = s.CorrectCount;
                row.QuestionCount = s.QuestionCount;
                row.UpdatedAt = now;
            }
            else
            {
                row = new ExamSectionScore
                {
                    ParticipantId = participant.Id, SectionId = s.SectionId,
                    Points = s.Points, MaxPoints = s.MaxPoints,
                    CorrectCount = s.CorrectCount, QuestionCount = s.QuestionCount,
                    UpdatedAt = now,
                };
                db.ExamSectionScores.Add(row);
                rows[s.SectionId] = row;
            }
        }

        participant.CorrectCount = score.CorrectCount;
        participant.QuestionCount = score.QuestionCount;
        participant.TotalPoints = score.TotalPoints;
        participant.MaxPoints = score.MaxPoints;
        participant.Percent = score.Percent;
        participant.Status = ExamParticipantStatus.Finished;
        participant.ScoredAt = now;
        participant.ScoredByUserId = scoredByUserId;
        return rows.Values.ToList();
    }

    /// <summary>
    /// §8.5, forward only: a candidate who has a result is <c>tested</c> —
    /// from <c>none</c>, <c>invited</c> or <c>testing</c>. <c>accepted</c> and
    /// <c>rejected</c> are human decisions and are never overwritten.
    /// </summary>
    public static void AdvanceLeadToTested(Lead lead)
    {
        ArgumentNullException.ThrowIfNull(lead);
        if (lead.AdmissionStatus is LeadAdmissionStatus.None or LeadAdmissionStatus.Invited or LeadAdmissionStatus.Testing)
            lead.AdmissionStatus = LeadAdmissionStatus.Tested;
    }

    /// <summary>
    /// Can points be typed into this exam at all? Online exams are scored by
    /// the engine; a cancelled exam is history. Draft, published and closed
    /// paper exams accept entry — the paper is marked after the sitting, and a
    /// typo found later must be fixable.
    /// </summary>
    public static ExamError? EntryStateProblem(Exam exam)
    {
        ArgumentNullException.ThrowIfNull(exam);
        if (exam.Delivery == ExamDelivery.Online) return ExamError.Conflict("exam_online", EntryOnlineMessage);
        if (exam.Status == ExamStatus.Cancelled) return ExamError.Conflict("exam_cancelled", EntryCancelledMessage);
        return null;
    }

    /// <summary>The grid's columns: the exam's sections in order, with subject names.</summary>
    public static async Task<List<ExamEntryColumnDto>> ColumnsAsync(IAppDbContext db, string examId, CancellationToken ct = default) =>
        await (from s in db.ExamSections.AsNoTracking()
               join subject in db.Subjects.AsNoTracking() on s.SubjectId equals subject.Id
               where s.ExamId == examId
               orderby s.Order, s.Id
               select new ExamEntryColumnDto(s.Id, s.SubjectId, subject.Name, s.MaxScore))
            .ToListAsync(ct);

    /// <summary>A participant row of the grid/template, with display name and class — no scores.</summary>
    public sealed record EntryPerson(string ParticipantId, string FullName, string? ClassName, string Status);

    /// <summary>Participants of an exam, by class then name (the grid's and the template's order).</summary>
    public static async Task<List<EntryPerson>> EntryPeopleAsync(IAppDbContext db, string examId, CancellationToken ct = default) =>
        (await ParticipantViews(db).Where(v => v.P.ExamId == examId)
            .OrderBy(v => v.ClassName ?? v.StudentClassName).ThenBy(v => v.StudentName ?? v.LeadName).ThenBy(v => v.P.Id)
            .ToListAsync(ct))
        .Select(v => new EntryPerson(v.P.Id, DisplayName(v.P, v.StudentName, v.LeadName),
            DisplayClass(v.ClassName, v.StudentClassName), v.P.Status))
        .ToList();

    /// <summary><c>participant_id → section_id → points</c> for one exam — one query.</summary>
    public static async Task<Dictionary<string, Dictionary<string, decimal>>> ScoresOfExamAsync(
        IAppDbContext db, string examId, CancellationToken ct = default) =>
        (await (from score in db.ExamSectionScores.AsNoTracking()
                join p in db.ExamParticipants.AsNoTracking() on score.ParticipantId equals p.Id
                where p.ExamId == examId
                select new { score.ParticipantId, score.SectionId, score.Points })
            .ToListAsync(ct))
        .GroupBy(x => x.ParticipantId)
        .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.SectionId, x => x.Points, StringComparer.Ordinal),
            StringComparer.Ordinal);

    public static ExamError EntryTooLarge(int count) => ExamError.Conflict("entry_table_too_large",
        $"Imtihonda {count} ta ishtirokchi bor — jadval {EntryTableCap} tadan ortig'ini ko'rsatmaydi. "
        + "Imtihonni sinflar bo'yicha bir nechta imtihonga bo'ling.");

    private static async Task<Dictionary<string, Lead>> LeadsOfAsync(
        IAppDbContext db, Exam exam, IEnumerable<ExamParticipant> participants, CancellationToken ct)
    {
        if (exam.Kind != ExamKind.Admission) return new Dictionary<string, Lead>();
        var leadIds = participants.Where(p => p.LeadId != null).Select(p => p.LeadId!).Distinct().ToList();
        if (leadIds.Count == 0) return new Dictionary<string, Lead>();
        return await db.Leads.Where(l => leadIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
    }

    private static void ClearSummary(ExamParticipant p)
    {
        p.CorrectCount = null;
        p.QuestionCount = null;
        p.TotalPoints = null;
        p.MaxPoints = null;
        p.Percent = null;
    }

    private static object ResultSnapshot(ExamParticipant p, IEnumerable<ExamSectionScore> scores) => new
    {
        p.Status,
        p.TotalPoints,
        p.MaxPoints,
        p.Percent,
        Scores = scores.OrderBy(s => s.SectionId).Select(s => new { s.SectionId, s.Points }).ToList(),
    };

    // =====================================================================
    //  Results register (screen 8)
    // =====================================================================

    /// <summary>A participant joined to its exam and names — composable in SQL (see <see cref="ParticipantView"/>).</summary>
    private sealed class ResultView
    {
        public ExamParticipant P { get; init; } = null!;
        public Exam E { get; init; } = null!;
        public string? StudentName { get; init; }
        public string? StudentClassName { get; init; }
        public string? LeadName { get; init; }
        public string? ClassName { get; init; }
    }

    /// <summary><c>GET /api/admin/exams/results</c> — one page, newest exam first, then class and name.</summary>
    public static async Task<ExamOutcome<ExamPageDto<ExamResultRowDto>>> ListResultsAsync(
        IAppDbContext db, ExamResultListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, limit) = Paging(query.Page, query.Limit);
        var outcome = ResultsQuery(db, query);
        if (outcome.Error is { } error) return error;

        var q = outcome.Value!;
        var total = await q.CountAsync(ct);
        var views = await Ordered(q).Skip((page - 1) * limit).Take(limit).ToListAsync(ct);
        return new ExamPageDto<ExamResultRowDto>(await ResultRowsAsync(db, views, ct), total, page, limit);
    }

    /// <summary>
    /// The whole filter for the <c>.xlsx</c> export, capped at
    /// <see cref="MaxExportRows"/>; <c>Total</c> stays the real count so the
    /// caller can say the cap bit.
    /// </summary>
    public static async Task<ExamOutcome<(List<ExamResultRowDto> Rows, int Total)>> ExportResultsAsync(
        IAppDbContext db, ExamResultListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var outcome = ResultsQuery(db, query);
        if (outcome.Error is { } error) return error;

        var q = outcome.Value!;
        var total = await q.CountAsync(ct);
        var views = await Ordered(q).Take(MaxExportRows).ToListAsync(ct);
        return (await ResultRowsAsync(db, views, ct), total);
    }

    private static ExamOutcome<IQueryable<ResultView>> ResultsQuery(IAppDbContext db, ExamResultListQuery query)
    {
        var status = Clean(query.Status);
        if (status is not null && !ExamParticipantStatus.All.Contains(status))
            return UnknownFilter("status", status, ExamParticipantStatus.All);

        var q =
            from p in db.ExamParticipants.AsNoTracking()
            join e in db.Exams.AsNoTracking() on p.ExamId equals e.Id
            join s in db.Students.AsNoTracking() on p.StudentId equals s.Id into sj
            from s in sj.DefaultIfEmpty()
            join l in db.Leads.AsNoTracking() on p.LeadId equals l.Id into lj
            from l in lj.DefaultIfEmpty()
            join c in db.Classes.AsNoTracking() on p.ClassId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new ResultView
            {
                P = p,
                E = e,
                StudentName = s == null ? null : s.FullName,
                StudentClassName = s == null ? null : s.ClassName,
                LeadName = l == null ? null : l.FullName,
                ClassName = c == null ? null : c.Name,
            };

        if (Clean(query.ExamId) is { } examId) q = q.Where(v => v.P.ExamId == examId);
        if (Clean(query.ClassId) is { } classId) q = q.Where(v => v.P.ClassId == classId);
        if (Clean(query.SubjectId) is { } subjectId)
            q = q.Where(v => db.ExamSections.Any(sec => sec.ExamId == v.P.ExamId && sec.SubjectId == subjectId));
        if (status is not null) q = q.Where(v => v.P.Status == status);
        if (Clean(query.Search)?.ToLowerInvariant() is { } term)
        {
            q = q.Where(v => (v.StudentName ?? v.LeadName ?? "").ToLower().Contains(term)
                || v.E.Title.ToLower().Contains(term));
        }
        return new ExamOutcome<IQueryable<ResultView>>(q, null);
    }

    private static IQueryable<ResultView> Ordered(IQueryable<ResultView> q) =>
        q.OrderByDescending(v => v.E.CreatedAt).ThenBy(v => v.E.Id)
            .ThenBy(v => v.ClassName ?? v.StudentClassName).ThenBy(v => v.StudentName ?? v.LeadName)
            .ThenBy(v => v.P.Id);

    /// <summary>Rows plus per-subject scores — two more queries for the whole page.</summary>
    private static async Task<List<ExamResultRowDto>> ResultRowsAsync(
        IAppDbContext db, List<ResultView> views, CancellationToken ct)
    {
        var participantIds = views.Select(v => v.P.Id).ToList();
        var examIds = views.Select(v => v.P.ExamId).Distinct().ToList();

        var sections = (await (
                from s in db.ExamSections.AsNoTracking()
                join subject in db.Subjects.AsNoTracking() on s.SubjectId equals subject.Id
                where examIds.Contains(s.ExamId)
                orderby s.Order, s.Id
                select new { s.Id, s.ExamId, s.SubjectId, Name = subject.Name, s.MaxScore })
            .ToListAsync(ct))
            .GroupBy(s => s.ExamId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var scores = (await db.ExamSectionScores.AsNoTracking()
                .Where(s => participantIds.Contains(s.ParticipantId))
                .Select(s => new { s.ParticipantId, s.SectionId, s.Points, s.MaxPoints })
                .ToListAsync(ct))
            .ToDictionary(s => (s.ParticipantId, s.SectionId));

        return views.Select(v =>
        {
            var perSubject = sections.GetValueOrDefault(v.P.ExamId)?.Select(s =>
            {
                var hit = scores.GetValueOrDefault((v.P.Id, s.Id));
                return new ExamResultScoreDto(s.Id, s.SubjectId, s.Name, hit?.Points, hit?.MaxPoints ?? s.MaxScore);
            }).ToList() ?? [];

            return new ExamResultRowDto(
                v.P.Id, v.E.Id, v.E.Title,
                v.E.ExamDate ?? v.E.OpensAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                v.E.Kind, v.E.Delivery, v.P.ParticipantKind,
                DisplayName(v.P, v.StudentName, v.LeadName),
                v.P.ClassId, DisplayClass(v.ClassName, v.StudentClassName),
                v.P.Status, v.P.TotalPoints, v.P.MaxPoints, v.P.Percent, v.P.ScoredAt,
                perSubject);
        }).ToList();
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>A query-string or body value trimmed to "a real value, or nothing".</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>§6 paging: page ≥ 1; limit 1…200, default 50.</summary>
    public static (int Page, int Limit) Paging(int? page, int? limit) =>
        (Math.Max(1, page ?? 1), Math.Clamp(limit ?? DefaultPageLimit, 1, MaxPageLimit));

    private static ExamError UnknownFilter(string field, string value, IReadOnlyList<string> allowed) =>
        ExamError.BadRequest("validation",
            $"Noma'lum qiymat: {field}='{value}'. Ruxsat etilganlar: {string.Join(", ", allowed)}");

    private static List<string> Ids(IReadOnlyList<string>? raw) =>
        (raw ?? []).Select(Clean).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// A client timestamp in the Tashkent wall clock, to the second — what a
    /// <c>timestamp without time zone</c> column of this module holds (§6).
    /// The form sends <c>yyyy-MM-ddTHH:mm:ss</c> with no offset (kept as is);
    /// a value WITH an offset or <c>Z</c> is an instant and is converted, not
    /// relabelled — Npgsql would refuse a UTC-kind value for this column type.
    /// </summary>
    private static DateTime? ToWallClock(DateTime? value)
    {
        if (value is not { } v) return null;
        var wall = v.Kind switch
        {
            DateTimeKind.Utc => AppClock.ToLocal(new DateTimeOffset(v, TimeSpan.Zero)),
            DateTimeKind.Local => AppClock.ToLocal(new DateTimeOffset(v)),
            _ => v,
        };
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        return wall.AddTicks(-(wall.Ticks % TimeSpan.TicksPerSecond));
    }

    private static void Audit(
        IAppDbContext db, ExamActor actor, string entity, string entityId, string action, string summary,
        object? before = null, object? after = null, string? studentId = null) =>
        db.AuditLogs.Add(AuditService.Entry(entity, entityId, action, summary, actor.UserId, actor.Name,
            before: before, after: after, studentId: studentId));

    private static object ExamSnapshot(Exam e) => new
    {
        e.Title, e.Kind, e.Delivery, e.Status, e.ExamTypeId, e.Grade, e.ExamDate,
        e.OpensAt, e.ClosesAt, e.TimeLimitMin,
        Sections = e.Sections.OrderBy(s => s.Order)
            .Select(s => new { s.SubjectId, s.BankId, s.QuestionCount, s.PointsPerCorrect, s.MaxScore, s.Order })
            .ToList(),
    };

    /// <summary>SQLSTATE 23505 — a unique index said no (a race the pre-check could not see).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is DbException { SqlState: "23505" }) return true;
        }
        return false;
    }
}
