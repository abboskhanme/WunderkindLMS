using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Ishga qabul va adaptatsiya (client, 2026-10-07; Recruitment.cs). Vacancies with a public apply
//  link, the applicant's walk through the five hiring stages with a result per stage, hiring into
//  a 30-day adaptation checklist, and its end. No Telegram messages (HR cut-over is on hold).
// ===========================================================================

public record VacancyDto(
    Guid Id, string Title, string? Department, string? Description, string? Requirements, string Slug, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt, int Applicants, int Active);

public record VacancyInput(string Title, string? Department, string? Description, string? Requirements);

public record PublicVacancyDto(string Slug, string Title, string? Department, string? Description, string? Requirements);

/// <summary>What the public form sends (the CV comes as a file next to it).</summary>
public record PublicApplyInput(string FullName, string Phone, string? Email, string? Experience, string? About);

public record ApplicantRowDto(
    Guid Id, string FullName, string Phone, string? Email, Guid? VacancyId, string? VacancyTitle, string? Position,
    string Source, string Stage, string StageLabel, DateTimeOffset StageChangedAt, DateTimeOffset CreatedAt, bool HasCv,
    int? LastScore, string? RejectReason, string? RejectedAtStage, string? HiredOn, int StepsDone, int StepsTotal, int StepsOverdue);

public record ApplicantStageResultDto(string Stage, string StageLabel, int? Score, string? Comment, string ByName, DateTimeOffset At);

public record ApplicantEventDto(string? FromStage, string ToStage, string ToLabel, string? Note, string ByName, DateTimeOffset At);

public record OnboardingStepDto(
    Guid Id, int Position, string Title, string? Description, string DueOn, int Day,
    DateTimeOffset? DoneAt, string? DoneByName, string? Comment, bool Overdue);

public record ApplicantDetailDto(
    ApplicantRowDto Row, string? Experience, string? About, string? CvName,
    List<ApplicantStageResultDto> Results, List<ApplicantEventDto> Events, List<OnboardingStepDto> Onboarding, int? OnboardingDay);

public record ApplicantInput(string FullName, string Phone, string? Email, Guid? VacancyId, string? Position, string? Experience, string? About);

public record ApplicantMoveRequest(string Stage, string? Note);

public record ApplicantResultInput(int? Score, string? Comment);

public record ApplicantRejectRequest(string Reason);

public record ApplicantHireRequest(string? HiredOn);

public record OnboardingStepUpdate(bool Done, string? Comment);

public record OnboardingTemplateDto(Guid Id, int Position, string Title, string? Description, int DueDay, bool IsActive);

public record OnboardingTemplateInput(Guid? Id, string Title, string? Description, int DueDay, bool IsActive);

public record RecruitmentCountsDto(int New, Dictionary<string, int> Pipeline, int Onboarding, int Adapted, int Rejected);

/// <summary>An error with its HTTP status — the controller turns it into a response.</summary>
public sealed class RecruitmentException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public static class RecruitmentService
{
    public const string Permission = "recruitment";
    public const string SourcePublic = "public";
    public const string SourceManual = "manual";

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static RecruitmentException Bad(string message) => new(400, message);
    private static RecruitmentException NotFound(string message = "Nomzod topilmadi") => new(404, message);

    private static string? Clean(string? value, int max)
    {
        var v = (value ?? "").Trim();
        return v.Length == 0 ? null : v.Length > max ? v[..max] : v;
    }

    // =====================================================================
    //  Vacancies
    // =====================================================================

    /// <summary>A link-friendly slug from the title plus a short random tail (unique).</summary>
    public static string SlugFrom(string title)
    {
        var map = new Dictionary<char, string> { ['ʻ'] = "", ['\''] = "", ['`'] = "", ['’'] = "", ['ʼ'] = "" };
        var sb = new StringBuilder();
        foreach (var ch in title.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (map.TryGetValue(ch, out var r)) { sb.Append(r); continue; }
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '-');
        }
        var core = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (core.Length > 50) core = core[..50].TrimEnd('-');
        var tail = Guid.NewGuid().ToString("N")[..4];
        return core.Length == 0 ? $"vakansiya-{tail}" : $"{core}-{tail}";
    }

    public static async Task<List<VacancyDto>> VacanciesAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var vacancies = await db.Vacancies.AsNoTracking().OrderBy(v => v.Status).ThenByDescending(v => v.CreatedAt).ToListAsync(ct);
        var counts = await db.JobApplicants.AsNoTracking().Where(a => a.VacancyId != null)
            .GroupBy(a => a.VacancyId!.Value)
            .Select(g => new
            {
                g.Key,
                All = g.Count(),
                Active = g.Count(a => a.Stage != ApplicantStage.Rejected && a.Stage != ApplicantStage.Adapted),
            })
            .ToDictionaryAsync(x => x.Key, ct);
        return [.. vacancies.Select(v => new VacancyDto(
            v.Id, v.Title, v.Department, v.Description, v.Requirements, v.Slug, v.Status, v.CreatedAt, v.ClosedAt,
            counts.TryGetValue(v.Id, out var c) ? c.All : 0, counts.TryGetValue(v.Id, out var c2) ? c2.Active : 0))];
    }

    private static void Apply(Vacancy v, VacancyInput input)
    {
        var title = Clean(input.Title, 200) ?? throw Bad("Vakansiya nomi kiritilmagan");
        v.Title = title;
        v.Department = Clean(input.Department, 100);
        v.Description = Clean(input.Description, 4000);
        v.Requirements = Clean(input.Requirements, 4000);
    }

    public static async Task<Vacancy> CreateVacancyAsync(IAppDbContext db, VacancyInput input, string actorId, CancellationToken ct = default)
    {
        var v = new Vacancy { CreatedBy = actorId, CreatedAt = AppClock.NowInstant };
        Apply(v, input);
        v.Slug = SlugFrom(v.Title);
        db.Vacancies.Add(v);
        await db.SaveChangesAsync(ct);
        return v;
    }

    public static async Task UpdateVacancyAsync(IAppDbContext db, Guid id, VacancyInput input, CancellationToken ct = default)
    {
        var v = await db.Vacancies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("Vakansiya topilmadi");
        Apply(v, input);
        await db.SaveChangesAsync(ct);
    }

    public static async Task SetVacancyStatusAsync(IAppDbContext db, Guid id, bool open, CancellationToken ct = default)
    {
        var v = await db.Vacancies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("Vakansiya topilmadi");
        v.Status = open ? VacancyStatus.Open : VacancyStatus.Closed;
        v.ClosedAt = open ? null : AppClock.NowInstant;
        await db.SaveChangesAsync(ct);
    }

    public static async Task<List<PublicVacancyDto>> PublicVacanciesAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.Vacancies.AsNoTracking().Where(v => v.Status == VacancyStatus.Open).OrderByDescending(v => v.CreatedAt)
            .Select(v => new PublicVacancyDto(v.Slug, v.Title, v.Department, v.Description, v.Requirements)).ToListAsync(ct);

    public static async Task<Vacancy?> OpenVacancyAsync(IAppDbContext db, string slug, CancellationToken ct = default)
    {
        var key = (slug ?? "").Trim().ToLowerInvariant();
        return await db.Vacancies.AsNoTracking().FirstOrDefaultAsync(v => v.Slug == key && v.Status == VacancyStatus.Open, ct);
    }

    // =====================================================================
    //  Applications
    // =====================================================================

    private static (string FullName, string Phone, string? Email) Contact(string? fullName, string? phone, string? email)
    {
        var name = Clean(fullName, 200) ?? throw Bad("F.I.Sh. kiritilmagan");
        var normalized = HrValues.NormalizePhone(phone) ?? throw Bad("Telefon raqami noto'g'ri (+998 XX XXX XX XX)");
        var mail = Clean(email, 200);
        if (mail is not null && (!mail.Contains('@') || mail.Contains(' '))) throw Bad("Email noto'g'ri");
        return (name, normalized, mail);
    }

    /// <summary>
    /// The public form. The same phone applying again to the same vacancy while still in the
    /// process is not a second row — the existing one is returned (and the CV, if new, replaces
    /// nothing: the caller deletes the unused file).
    /// </summary>
    public static async Task<(JobApplicant Applicant, bool Created)> ApplyAsync(
        IAppDbContext db, Vacancy vacancy, PublicApplyInput input, string? cvPath, string? cvName, CancellationToken ct = default)
    {
        var (name, phone, email) = Contact(input.FullName, input.Phone, input.Email);
        var existing = await db.JobApplicants.AsNoTracking().FirstOrDefaultAsync(a =>
            a.VacancyId == vacancy.Id && a.Phone == phone && a.Stage != ApplicantStage.Rejected && a.Stage != ApplicantStage.Adapted, ct);
        if (existing is not null) return (existing, false);

        var now = AppClock.NowInstant;
        var a = new JobApplicant
        {
            VacancyId = vacancy.Id, FullName = name, Phone = phone, Email = email,
            Experience = Clean(input.Experience, 2000), About = Clean(input.About, 4000),
            CvPath = cvPath, CvName = Clean(cvName, 200), Source = SourcePublic,
            Stage = ApplicantStage.New, StageChangedAt = now, CreatedAt = now,
        };
        db.JobApplicants.Add(a);
        db.ApplicantEvents.Add(new ApplicantEvent { ApplicantId = a.Id, ToStage = ApplicantStage.New, Note = "Sayt orqali ariza", By = "", At = now });
        await db.SaveChangesAsync(ct);
        return (a, true);
    }

    public static async Task<JobApplicant> CreateAsync(
        IAppDbContext db, ApplicantInput input, string? cvPath, string? cvName, string actorId, CancellationToken ct = default)
    {
        var (name, phone, email) = Contact(input.FullName, input.Phone, input.Email);
        if (input.VacancyId is { } vid && !await db.Vacancies.AnyAsync(v => v.Id == vid, ct)) throw Bad("Vakansiya topilmadi");
        var now = AppClock.NowInstant;
        var a = new JobApplicant
        {
            VacancyId = input.VacancyId, FullName = name, Phone = phone, Email = email, Position = Clean(input.Position, 200),
            Experience = Clean(input.Experience, 2000), About = Clean(input.About, 4000),
            CvPath = cvPath, CvName = Clean(cvName, 200), Source = SourceManual,
            Stage = ApplicantStage.New, StageChangedAt = now, CreatedAt = now, CreatedBy = actorId,
        };
        db.JobApplicants.Add(a);
        db.ApplicantEvents.Add(new ApplicantEvent { ApplicantId = a.Id, ToStage = ApplicantStage.New, Note = "Qo'lda qo'shildi", By = actorId, At = now });
        await db.SaveChangesAsync(ct);
        return a;
    }

    public static async Task UpdateAsync(IAppDbContext db, Guid id, ApplicantInput input, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var (name, phone, email) = Contact(input.FullName, input.Phone, input.Email);
        if (input.VacancyId is { } vid && !await db.Vacancies.AnyAsync(v => v.Id == vid, ct)) throw Bad("Vakansiya topilmadi");
        (a.FullName, a.Phone, a.Email, a.VacancyId) = (name, phone, email, input.VacancyId);
        a.Position = Clean(input.Position, 200);
        a.Experience = Clean(input.Experience, 2000);
        a.About = Clean(input.About, 4000);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The CV path being replaced (for the caller to delete), after storing the new one.</summary>
    public static async Task<string?> SetCvAsync(IAppDbContext db, Guid id, string cvPath, string cvName, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var old = a.CvPath;
        a.CvPath = cvPath;
        a.CvName = Clean(cvName, 200);
        await db.SaveChangesAsync(ct);
        return old;
    }

    private static void Event(IAppDbContext db, JobApplicant a, string to, string? note, string actorId, DateTimeOffset now)
    {
        db.ApplicantEvents.Add(new ApplicantEvent { ApplicantId = a.Id, FromStage = a.Stage, ToStage = to, Note = Clean(note, 1000), By = actorId, At = now });
        a.Stage = to;
        a.StageChangedAt = now;
    }

    /// <summary>
    /// Into or along the hiring stages: from a new application, between the five stages (either
    /// way — a stage may be skipped for a non-teaching post), or back from a rejection.
    /// Hiring, the end of adaptation and rejection have their own calls.
    /// </summary>
    public static async Task MoveAsync(IAppDbContext db, Guid id, ApplicantMoveRequest req, string actorId, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var to = req.Stage;
        var allowed = to == ApplicantStage.New
            ? a.Stage == ApplicantStage.Rejected
            : ApplicantStage.IsPipeline(to) && (a.Stage is ApplicantStage.New or ApplicantStage.Rejected || ApplicantStage.IsPipeline(a.Stage));
        if (!allowed) throw Bad($"«{ApplicantStage.Label(a.Stage)}» bosqichidan «{ApplicantStage.Label(to)}» ga o'tkazib bo'lmaydi");
        if (a.Stage == to) return;
        if (a.Stage == ApplicantStage.Rejected)
        {
            a.RejectReason = null;
            a.RejectedAtStage = null;
        }
        Event(db, a, to, req.Note, actorId, AppClock.NowInstant);
        await db.SaveChangesAsync(ct);
    }

    public static async Task SaveResultAsync(
        IAppDbContext db, Guid id, string stage, ApplicantResultInput input, string actorId, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (!ApplicantStage.IsPipeline(stage)) throw Bad("Natija faqat ishga qabul bosqichlariga yoziladi");
        if (a.Stage == ApplicantStage.New) throw Bad("Avval nomzodni jarayonga oling");
        if (input.Score is < 0 or > 100) throw Bad("Ball 0..100 oralig'ida bo'lsin");
        var comment = Clean(input.Comment, 4000);
        if (input.Score is null && comment is null) throw Bad("Ball yoki izoh kiriting");
        var row = await db.ApplicantStageResults.FirstOrDefaultAsync(r => r.ApplicantId == id && r.Stage == stage, ct);
        if (row is null)
        {
            row = new ApplicantStageResult { ApplicantId = id, Stage = stage };
            db.ApplicantStageResults.Add(row);
        }
        row.Score = input.Score;
        row.Comment = comment;
        row.By = actorId;
        row.At = AppClock.NowInstant;
        await db.SaveChangesAsync(ct);
    }

    public static async Task RejectAsync(IAppDbContext db, Guid id, string? reason, string actorId, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (a.Stage is ApplicantStage.Rejected or ApplicantStage.Adapted) throw Bad("Bu nomzodning jarayoni yakunlangan");
        var text = Clean(reason, 1000) ?? throw Bad("Rad etish sababini yozing");
        a.RejectReason = text;
        a.RejectedAtStage = a.Stage;
        Event(db, a, ApplicantStage.Rejected, text, actorId, AppClock.NowInstant);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>After the director's interview: hired, and the adaptation plan is laid out from the active steps.</summary>
    public static async Task HireAsync(IAppDbContext db, Guid id, ApplicantHireRequest req, string actorId, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (a.Stage != ApplicantStage.DirectorInterview) throw Bad("Ishga olish — direktor suhbatidan keyin");
        DateOnly start;
        if (string.IsNullOrWhiteSpace(req.HiredOn)) start = AppClock.Today;
        else if (!DateOnly.TryParseExact(req.HiredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
            throw Bad("Ishga kirish sanasi noto'g'ri");

        var templates = await db.OnboardingStepTemplates.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Position).ToListAsync(ct);
        var position = 0;
        foreach (var t in templates)
            db.ApplicantOnboardingSteps.Add(new ApplicantOnboardingStep
            {
                ApplicantId = a.Id, Position = ++position, Title = t.Title, Description = t.Description,
                DueOn = start.AddDays(Math.Clamp(t.DueDay, 1, OnboardingStepTemplate.Days) - 1),
            });
        a.HiredOn = start;
        Event(db, a, ApplicantStage.Onboarding, $"Ishga olindi: {start:dd.MM.yyyy}", actorId, AppClock.NowInstant);
        await db.SaveChangesAsync(ct);
    }

    public static async Task SetStepAsync(
        IAppDbContext db, Guid id, Guid stepId, OnboardingStepUpdate req, string actorId, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (a.Stage != ApplicantStage.Onboarding) throw Bad("Nomzod adaptatsiyada emas");
        var step = await db.ApplicantOnboardingSteps.FirstOrDefaultAsync(s => s.Id == stepId && s.ApplicantId == id, ct)
                   ?? throw NotFound("Bosqich topilmadi");
        step.Comment = Clean(req.Comment, 1000);
        if (req.Done && step.DoneAt is null)
        {
            step.DoneAt = AppClock.NowInstant;
            step.DoneBy = actorId;
        }
        else if (!req.Done)
        {
            step.DoneAt = null;
            step.DoneBy = null;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The adaptation passed — every step must be done.</summary>
    public static async Task AdaptAsync(IAppDbContext db, Guid id, string? note, string actorId, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (a.Stage != ApplicantStage.Onboarding) throw Bad("Nomzod adaptatsiyada emas");
        var open = await db.ApplicantOnboardingSteps.CountAsync(s => s.ApplicantId == id && s.DoneAt == null, ct);
        if (open > 0) throw Bad($"Adaptatsiyaning {open} ta bosqichi hali bajarilmagan");
        Event(db, a, ApplicantStage.Adapted, note, actorId, AppClock.NowInstant);
        await db.SaveChangesAsync(ct);
    }

    // =====================================================================
    //  Reads
    // =====================================================================

    /// <summary>The tab a stage belongs to: new | pipeline | onboarding | archive.</summary>
    public static string[] StagesOf(string? tab) => tab switch
    {
        "new" => [ApplicantStage.New],
        "pipeline" => ApplicantStage.Pipeline,
        "onboarding" => [ApplicantStage.Onboarding],
        "archive" => [ApplicantStage.Adapted, ApplicantStage.Rejected],
        _ => ApplicantStage.All,
    };

    public static async Task<List<ApplicantRowDto>> ListAsync(
        IAppDbContext db, string? tab, Guid? vacancyId, string? q, CancellationToken ct = default)
    {
        var stages = StagesOf(tab);
        var query = db.JobApplicants.AsNoTracking().Where(a => stages.Contains(a.Stage));
        if (vacancyId is { } v) query = query.Where(a => a.VacancyId == v);
        var needle = (q ?? "").Trim();
        if (needle.Length > 0)
        {
            var lower = needle.ToLower();
            query = query.Where(a => a.FullName.ToLower().Contains(lower) || a.Phone.Contains(needle));
        }
        var rows = await query.OrderByDescending(a => a.StageChangedAt).Take(500).ToListAsync(ct);
        return await RowsAsync(db, rows, ct);
    }

    private static async Task<List<ApplicantRowDto>> RowsAsync(IAppDbContext db, List<JobApplicant> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();
        var vacancyIds = rows.Select(r => r.VacancyId).Where(v => v is not null).Distinct().ToList();
        var vacancies = await db.Vacancies.AsNoTracking().Where(v => vacancyIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Title, ct);
        var results = await db.ApplicantStageResults.AsNoTracking().Where(r => ids.Contains(r.ApplicantId) && r.Score != null)
            .Select(r => new { r.ApplicantId, r.Score, r.At }).ToListAsync(ct);
        var steps = await db.ApplicantOnboardingSteps.AsNoTracking().Where(s => ids.Contains(s.ApplicantId))
            .Select(s => new { s.ApplicantId, s.DoneAt, s.DueOn }).ToListAsync(ct);
        var today = AppClock.Today;
        return [.. rows.Select(a =>
        {
            var mine = steps.Where(s => s.ApplicantId == a.Id).ToList();
            return new ApplicantRowDto(
                a.Id, a.FullName, a.Phone, a.Email, a.VacancyId,
                a.VacancyId is { } v ? vacancies.GetValueOrDefault(v) : null, a.Position, a.Source,
                a.Stage, ApplicantStage.Label(a.Stage), a.StageChangedAt, a.CreatedAt, a.CvPath is not null,
                results.Where(r => r.ApplicantId == a.Id).OrderByDescending(r => r.At).Select(r => r.Score).FirstOrDefault(),
                a.RejectReason, a.RejectedAtStage, a.HiredOn is { } h ? Iso(h) : null,
                mine.Count(s => s.DoneAt != null), mine.Count, mine.Count(s => s.DoneAt == null && s.DueOn < today));
        })];
    }

    public static async Task<ApplicantDetailDto> DetailAsync(IAppDbContext db, Guid id, CancellationToken ct = default)
    {
        var a = await db.JobApplicants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var row = (await RowsAsync(db, [a], ct))[0];
        var results = await db.ApplicantStageResults.AsNoTracking().Where(r => r.ApplicantId == id).ToListAsync(ct);
        var events = await db.ApplicantEvents.AsNoTracking().Where(e => e.ApplicantId == id).OrderBy(e => e.At).ToListAsync(ct);
        var steps = await db.ApplicantOnboardingSteps.AsNoTracking().Where(s => s.ApplicantId == id).OrderBy(s => s.Position).ToListAsync(ct);
        var userIds = results.Select(r => r.By).Concat(events.Select(e => e.By)).Concat(steps.Select(s => s.DoneBy ?? ""))
            .Where(u => u.Length > 0).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        string NameOf(string? uid) => string.IsNullOrEmpty(uid) ? "" : users.GetValueOrDefault(uid, "");
        var today = AppClock.Today;

        return new ApplicantDetailDto(
            row, a.Experience, a.About, a.CvName,
            [.. results.OrderBy(r => Array.IndexOf(ApplicantStage.Pipeline, r.Stage))
                .Select(r => new ApplicantStageResultDto(r.Stage, ApplicantStage.Label(r.Stage), r.Score, r.Comment, NameOf(r.By), r.At))],
            [.. events.Select(e => new ApplicantEventDto(e.FromStage, e.ToStage, ApplicantStage.Label(e.ToStage), e.Note, NameOf(e.By), e.At))],
            [.. steps.Select(s => new OnboardingStepDto(
                s.Id, s.Position, s.Title, s.Description, Iso(s.DueOn),
                a.HiredOn is { } h ? s.DueOn.DayNumber - h.DayNumber + 1 : 0,
                s.DoneAt, s.DoneBy is null ? null : NameOf(s.DoneBy), s.Comment, s.DoneAt is null && s.DueOn < today))],
            a.Stage == ApplicantStage.Onboarding && a.HiredOn is { } hired ? Math.Max(1, today.DayNumber - hired.DayNumber + 1) : null);
    }

    public static async Task<RecruitmentCountsDto> CountsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var counts = await db.JobApplicants.AsNoTracking().GroupBy(a => a.Stage)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new RecruitmentCountsDto(
            counts.GetValueOrDefault(ApplicantStage.New),
            ApplicantStage.Pipeline.ToDictionary(s => s, s => counts.GetValueOrDefault(s)),
            counts.GetValueOrDefault(ApplicantStage.Onboarding),
            counts.GetValueOrDefault(ApplicantStage.Adapted),
            counts.GetValueOrDefault(ApplicantStage.Rejected));
    }

    // =====================================================================
    //  Adaptation steps (settings)
    // =====================================================================

    public static async Task<List<OnboardingTemplateDto>> TemplatesAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.OnboardingStepTemplates.AsNoTracking().OrderBy(t => t.Position)
            .Select(t => new OnboardingTemplateDto(t.Id, t.Position, t.Title, t.Description, t.DueDay, t.IsActive)).ToListAsync(ct);

    /// <summary>
    /// Replaces the list in the given order. Plans already laid out keep their own copies, so a
    /// removed step simply stops being used for new hires.
    /// </summary>
    public static async Task SaveTemplatesAsync(IAppDbContext db, List<OnboardingTemplateInput> input, CancellationToken ct = default)
    {
        if (input is not { Count: > 0 }) throw Bad("Kamida bitta bosqich bo'lishi kerak");
        if (input.Count > 30) throw Bad("Bosqichlar 30 tadan oshmasin");
        foreach (var (t, i) in input.Select((t, i) => (t, i + 1)))
        {
            if (Clean(t.Title, 200) is null) throw Bad($"{i}-bosqich nomi bo'sh");
            if (t.DueDay is < 1 or > OnboardingStepTemplate.Days) throw Bad($"{i}-bosqich kuni 1..{OnboardingStepTemplate.Days} oralig'ida bo'lsin");
        }
        var existing = await db.OnboardingStepTemplates.ToListAsync(ct);
        var keep = input.Where(t => t.Id is not null).Select(t => t.Id!.Value).ToHashSet();
        db.OnboardingStepTemplates.RemoveRange(existing.Where(e => !keep.Contains(e.Id)));
        var position = 0;
        foreach (var t in input)
        {
            var row = t.Id is { } tid ? existing.FirstOrDefault(e => e.Id == tid) : null;
            if (row is null)
            {
                row = new OnboardingStepTemplate { Id = t.Id ?? Guid.NewGuid() };
                db.OnboardingStepTemplates.Add(row);
            }
            row.Position = ++position;
            row.Title = Clean(t.Title, 200)!;
            row.Description = Clean(t.Description, 1000);
            row.DueDay = t.DueDay;
            row.IsActive = t.IsActive;
        }
        await db.SaveChangesAsync(ct);
    }
}
