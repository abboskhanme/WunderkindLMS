using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Dars tahlili — observations, criteria, rating and analytics
//  (docs/modules/lesson-analysis.md). A port of the old app's
//  evaluations.py / criteria.py / teachers.py / dashboard.py / rating.py:
//  the rules, messages and numbers are the old ones.
//
//  WHO MAY DO WHAT lives in the controllers (roles) plus the two
//  data-dependent rules below: a COMPLETED observation is corrected only by
//  its observer or an admin (EnsureCanEdit), and a teacher sees only their own
//  COMPLETED observations (ForTeacher* methods, 404 otherwise).
// ===========================================================================

/// <summary>A rule violation with the HTTP status the controller answers with.</summary>
public sealed class LessonAnalysisException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>The caller of a staff endpoint.</summary>
/// <param name="IsAdmin">admin or superadmin (the old ADMIN role).</param>
public sealed record LessonAnalysisActor(string UserId, bool IsAdmin);

/// <summary><c>GET /observations</c> filters.</summary>
public sealed record LessonObservationFilter(
    string? TeacherId, string? Status, DateOnly? DateFrom, DateOnly? DateTo, int Limit = 100, int Offset = 0);

public sealed class LessonAnalysisService(IAppDbContext db, ILessonAnalysisAi ai)
{
    // limits follow the old schema: free text is capped so one request cannot push
    // megabytes into the database, the Gemini prompt and every PDF
    public const int ShortText = 50;
    public const int SubjectMax = 120;
    public const int LongText = 5000;
    public const int CommentText = 2000;
    public const int MaxDetails = 200;

    public const string NotFoundMessage = "Tahlil topilmadi";
    public const string TeacherNotFoundMessage = "O'qituvchi topilmadi";

    public static readonly string[] DistributionBuckets = ["0–59%", "60–74%", "75–89%", "90–100%"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // =====================================================================
    //  Criteria
    // =====================================================================

    public async Task<List<LaSectionDto>> CriteriaAsync(bool includeInactive, CancellationToken ct = default)
    {
        var sections = await db.LessonObservationSections.AsNoTracking()
            .Include(s => s.Criteria)
            .OrderBy(s => s.OrderIndex).ThenBy(s => s.Title)
            .ToListAsync(ct);
        return [.. sections.Select(s => new LaSectionDto(
            s.Id, s.Title, s.OrderIndex, s.Color,
            [.. s.Criteria
                .Where(c => includeInactive || c.IsActive)
                .OrderBy(c => c.OrderIndex).ThenBy(c => c.Text, StringComparer.Ordinal)
                .Select(ToDto)]))];
    }

    public async Task<LaCriterionDto> CreateCriterionAsync(LaCriterionCreateDto input, CancellationToken ct = default)
    {
        var text = (input.Text ?? "").Trim();
        if (text.Length < 3) throw new LessonAnalysisException(400, "Mezon matni kamida 3 belgi bo'lishi kerak");
        if (string.IsNullOrWhiteSpace(input.SectionId)
            || !await db.LessonObservationSections.AnyAsync(s => s.Id == input.SectionId, ct))
            throw new LessonAnalysisException(404, "Bo'lim topilmadi");

        var order = input.OrderIndex
            ?? (await db.LessonObservationCriteria.Where(c => c.SectionId == input.SectionId)
                .MaxAsync(c => (int?)c.OrderIndex, ct) ?? 0) + 1;
        var criterion = new LessonObservationCriterion
        {
            SectionId = input.SectionId,
            Text = text,
            OrderIndex = order,
            IsActive = true,
        };
        db.LessonObservationCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return ToDto(criterion);
    }

    /// <summary>Only the given fields change. There is no delete — deactivate instead.</summary>
    public async Task<LaCriterionDto> PatchCriterionAsync(string id, LaCriterionPatchDto input, CancellationToken ct = default)
    {
        var criterion = await db.LessonObservationCriteria.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new LessonAnalysisException(404, "Mezon topilmadi");
        if (input.Text is not null)
        {
            var text = input.Text.Trim();
            if (text.Length < 3) throw new LessonAnalysisException(400, "Mezon matni kamida 3 belgi bo'lishi kerak");
            criterion.Text = text;
        }
        if (input.OrderIndex is { } order) criterion.OrderIndex = order;
        if (input.IsActive is { } active) criterion.IsActive = active;
        await db.SaveChangesAsync(ct);
        return ToDto(criterion);
    }

    private static LaCriterionDto ToDto(LessonObservationCriterion c) => new(c.Id, c.Text, c.OrderIndex, c.IsActive);

    // =====================================================================
    //  Teachers (our `teachers` table — the old app's list is not ported)
    // =====================================================================

    private sealed record TeacherInfo(
        string Id, string FullName, string? Phone, string? PhotoUrl, List<string> SubjectIds, bool IsArchived);

    private sealed record ObservationLite(
        string Id, string TeacherId, string Status, DateOnly LessonDate, decimal ScorePercent, DateTimeOffset CreatedAt);

    private async Task<Dictionary<string, TeacherInfo>> TeacherMapAsync(CancellationToken ct) =>
        await db.Teachers.AsNoTracking()
            .Select(t => new TeacherInfo(t.Id, t.FullName, t.Phone, t.PhotoUrl, t.SubjectIds, t.IsArchived))
            .ToDictionaryAsync(t => t.Id, StringComparer.Ordinal, ct);

    private async Task<Dictionary<string, string>> SubjectNamesAsync(CancellationToken ct) =>
        await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, StringComparer.Ordinal, ct);

    private static LaTeacherDto TeacherDto(TeacherInfo t, IReadOnlyDictionary<string, string> subjects) =>
        new(t.Id, t.FullName, string.IsNullOrWhiteSpace(t.Phone) ? null : t.Phone,
            SubjectText(t.SubjectIds, subjects), string.IsNullOrWhiteSpace(t.PhotoUrl) ? null : t.PhotoUrl);

    /// <summary>The teacher's subjects joined with ", " (SPEC §5); null when none.</summary>
    private static string? SubjectText(IEnumerable<string>? subjectIds, IReadOnlyDictionary<string, string> subjects)
    {
        var names = (subjectIds ?? [])
            .Select(id => subjects.GetValueOrDefault(id))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private async Task<List<ObservationLite>> ObservationsLiteAsync(
        string? status, DateOnly? from, DateOnly? to, string? teacherId, CancellationToken ct)
    {
        var q = db.LessonObservations.AsNoTracking();
        if (status is not null) q = q.Where(o => o.Status == status);
        if (from is { } f) q = q.Where(o => o.LessonDate >= f);
        if (to is { } t) q = q.Where(o => o.LessonDate <= t);
        if (teacherId is not null) q = q.Where(o => o.TeacherId == teacherId);
        return await q
            .OrderBy(o => o.LessonDate).ThenBy(o => o.CreatedAt)
            .Select(o => new ObservationLite(o.Id, o.TeacherId, o.Status, o.LessonDate, o.ScorePercent, o.CreatedAt))
            .ToListAsync(ct);
    }

    private static LaTeacherSummaryDto Summary(
        TeacherInfo t, IReadOnlyDictionary<string, string> subjects, IEnumerable<ObservationLite> own)
    {
        var list = own.ToList();
        var completed = list.Where(o => o.Status == LessonObservationStatus.Completed).ToList();
        var mean = LessonAnalysisScoring.Mean(completed.Select(o => o.ScorePercent));
        var dto = TeacherDto(t, subjects);
        return new LaTeacherSummaryDto(
            dto.Id, dto.FullName, dto.Phone, dto.Subject, dto.PhotoUrl,
            completed.Count,
            list.Count(o => o.Status == LessonObservationStatus.Draft),
            mean is { } m ? LessonAnalysisScoring.PyRound(m, 2) : null,
            completed.Count == 0 ? null : Iso(completed.Max(o => o.LessonDate)));
    }

    /// <summary>Active teachers (by name), optional name search, with their numbers.</summary>
    public async Task<List<LaTeacherSummaryDto>> TeachersAsync(string? q, CancellationToken ct = default)
    {
        var teachers = await TeacherMapAsync(ct);
        var subjects = await SubjectNamesAsync(ct);
        var byTeacher = (await ObservationsLiteAsync(null, null, null, null, ct))
            .ToLookup(o => o.TeacherId, StringComparer.Ordinal);
        var needle = (q ?? "").Trim();

        return [.. teachers.Values
            .Where(t => !t.IsArchived)
            .Where(t => needle.Length == 0 || t.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(t => Summary(t, subjects, byTeacher[t.Id]))];
    }

    public async Task<LaTeacherSummaryDto> TeacherAsync(string id, CancellationToken ct = default)
    {
        var teachers = await TeacherMapAsync(ct);
        if (!teachers.TryGetValue(id, out var t)) throw new LessonAnalysisException(404, TeacherNotFoundMessage);
        var subjects = await SubjectNamesAsync(ct);
        return Summary(t, subjects, await ObservationsLiteAsync(null, null, null, id, ct));
    }

    /// <summary>A random active teacher — observations are not tied to the timetable.</summary>
    public async Task<LaTeacherDto> RandomTeacherAsync(CancellationToken ct = default)
    {
        var active = (await TeacherMapAsync(ct)).Values.Where(t => !t.IsArchived).ToList();
        if (active.Count == 0) throw new LessonAnalysisException(404, "Faol o'qituvchilar yo'q");
        return TeacherDto(active[Random.Shared.Next(active.Count)], await SubjectNamesAsync(ct));
    }

    /// <summary>
    /// The teacher's timetable lessons on <paramref name="date"/> — pre-fills the form (new,
    /// not in the old app). The quarter containing the date, its week
    /// (<see cref="PortalSchedule.WeekOf"/>) and the weekday (Mon = 0). A date outside every
    /// quarter, a Sunday or a holiday has no lessons.
    /// </summary>
    public async Task<List<LaTeacherLessonDto>> TeacherLessonsAsync(string teacherId, DateOnly date, CancellationToken ct = default)
    {
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct))
            throw new LessonAnalysisException(404, TeacherNotFoundMessage);

        var iso = Iso(date);
        var day = ((int)date.DayOfWeek + 6) % 7;
        if (day > 5) return [];
        if (await db.Holidays.AnyAsync(h => h.Date == iso, ct)) return [];

        var quarter = (await db.Quarters.AsNoTracking().OrderBy(q => q.Quarter).ToListAsync(ct))
            .FirstOrDefault(q => string.CompareOrdinal(iso, q.StartDate) >= 0 && string.CompareOrdinal(iso, q.EndDate) <= 0);
        if (quarter is null) return [];

        var week = PortalSchedule.WeekOf(ScheduleMath.GetQuarterWeeks(quarter.StartDate, quarter.EndDate), iso);
        var lessons = await PortalSchedule.TeacherWeekAsync(db, teacherId, quarter.Quarter, week);
        return [.. lessons
            .Where(l => l.Day == day)
            .OrderBy(l => l.Period).ThenBy(l => l.ClassName, StringComparer.Ordinal)
            .Select(l => new LaTeacherLessonDto(l.Period, l.StartTime, l.EndTime, l.ClassName, l.SubjectName))
            .Distinct()];
    }

    /// <summary>The <c>teachers</c> row of a teacher account; null when none is linked.</summary>
    public Task<string?> TeacherIdForUserAsync(string userId, CancellationToken ct = default) =>
        db.Teachers.AsNoTracking().Where(t => t.UserId == userId).Select(t => t.Id).FirstOrDefaultAsync(ct);

    // =====================================================================
    //  Observations
    // =====================================================================

    public async Task<List<LaObservationListItemDto>> ListAsync(
        LessonObservationFilter filter, CancellationToken ct = default)
    {
        if (filter.Limit is < 1 or > 500) throw new LessonAnalysisException(400, "limit 1..500 oralig'ida bo'lishi kerak");
        if (filter.Offset < 0) throw new LessonAnalysisException(400, "offset manfiy bo'lishi mumkin emas");
        if (filter.Status is not null && !LessonObservationStatus.IsValid(filter.Status))
            throw new LessonAnalysisException(400, "status: draft yoki completed");

        var q = db.LessonObservations.AsNoTracking();
        if (!string.IsNullOrEmpty(filter.TeacherId)) q = q.Where(o => o.TeacherId == filter.TeacherId);
        if (filter.Status is not null) q = q.Where(o => o.Status == filter.Status);
        if (filter.DateFrom is { } from) q = q.Where(o => o.LessonDate >= from);
        if (filter.DateTo is { } to) q = q.Where(o => o.LessonDate <= to);

        var rows = await (
                from o in q
                join t in db.Teachers on o.TeacherId equals t.Id
                join u in db.Users on o.ObserverUserId equals u.Id
                orderby o.LessonDate descending, o.CreatedAt descending
                select new
                {
                    o.Id, o.TeacherId, TeacherName = t.FullName, ObserverName = u.FullName, o.LessonDate,
                    o.ClassName, o.Subject, o.Status, o.ScoreDone, o.ScoreTotal, o.ScorePercent, o.AiSummary,
                })
            .Skip(filter.Offset).Take(filter.Limit)
            .ToListAsync(ct);

        return [.. rows.Select(r => new LaObservationListItemDto(
            r.Id, r.TeacherId, r.TeacherName, r.ObserverName, Iso(r.LessonDate), r.ClassName, r.Subject, r.Status,
            r.ScoreDone, r.ScoreTotal, (double)r.ScorePercent, !string.IsNullOrEmpty(r.AiSummary)))];
    }

    /// <summary>One observation for staff; <paramref name="actor"/> drives <c>canEdit</c>.</summary>
    public async Task<LaObservationDto> GetAsync(string id, LessonAnalysisActor actor, CancellationToken ct = default)
    {
        var o = await LoadAsync(id, tracked: false, ct);
        return await ToDtoAsync(o, CanEdit(o, actor), hideAiError: false, ct);
    }

    /// <summary>A teacher's own COMPLETED observation — anything else is 404 (drafts too, by id).</summary>
    public async Task<LaObservationDto> GetForTeacherAsync(string id, string? teacherId, CancellationToken ct = default)
    {
        var o = await LoadForTeacherAsync(id, teacherId, ct);
        // raw provider errors are for staff; the teacher's page never shows them
        var dto = await ToDtoAsync(o, canEdit: false, hideAiError: true, ct);
        return dto with { Sections = await CriteriaAsync(includeInactive: true, ct) };
    }

    private async Task<LessonObservation> LoadForTeacherAsync(string id, string? teacherId, CancellationToken ct)
    {
        var o = await LoadAsync(id, tracked: false, ct);
        if (teacherId is null || o.TeacherId != teacherId || o.Status != LessonObservationStatus.Completed)
            throw new LessonAnalysisException(404, NotFoundMessage);
        return o;
    }

    /// <summary>A teacher's own COMPLETED observations, newest first.</summary>
    public Task<List<LaObservationListItemDto>> ListForTeacherAsync(
        string? teacherId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken ct = default) =>
        teacherId is null
            ? Task.FromResult(new List<LaObservationListItemDto>())
            : ListAsync(new LessonObservationFilter(teacherId, LessonObservationStatus.Completed, from, to, limit, offset), ct);

    public async Task<LaObservationDto> CreateAsync(
        LaObservationInputDto input, LessonAnalysisActor actor, CancellationToken ct = default)
    {
        ValidateTexts(input, AllFields);
        if (string.IsNullOrWhiteSpace(input.TeacherId)) throw new LessonAnalysisException(400, "O'qituvchi tanlanmagan");
        var lessonDate = ParseDate(input.LessonDate) ?? throw new LessonAnalysisException(400, "Dars sanasi kerak (yyyy-MM-dd)");
        if (!await db.Teachers.AnyAsync(t => t.Id == input.TeacherId, ct))
            throw new LessonAnalysisException(404, TeacherNotFoundMessage);

        var now = DateTimeOffset.UtcNow;
        var o = new LessonObservation
        {
            TeacherId = input.TeacherId,
            ObserverUserId = actor.UserId,
            LessonDate = lessonDate,
            LessonTime = input.LessonTime,
            ClassName = input.ClassName,
            Subject = input.Subject,
            Strengths = input.Strengths,
            GrowthAreas = input.GrowthAreas,
            NextLessonAdvice = input.NextLessonAdvice,
            Status = LessonObservationStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await ApplyDetailsAsync(o, input.Details ?? [], ct);
        LessonAnalysisScoring.Recompute(o);
        db.LessonObservations.Add(o);
        await db.SaveChangesAsync(ct);
        return await GetAsync(o.Id, actor, ct);
    }

    private static readonly HashSet<string> AllFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "teacherId", "lessonDate", "lessonTime", "className", "subject",
        "strengths", "growthAreas", "nextLessonAdvice", "details",
    };

    /// <summary>
    /// PATCH: only the fields PRESENT in the body change (old <c>exclude_unset</c>). An explicit
    /// null for teacherId / lessonDate means "leave as is" (both are NOT NULL), not a 500.
    /// <c>details</c>, when present, is the full list the form shows: rows with no mark and no
    /// comment are dropped, answers to criteria deactivated since are kept (not in the list).
    /// </summary>
    public async Task<LaObservationDto> UpdateAsync(
        string id, JsonElement body, LessonAnalysisActor actor, CancellationToken ct = default)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new LessonAnalysisException(400, "So'rov tanasi noto'g'ri");
        LaObservationInputDto input;
        try
        {
            input = body.Deserialize<LaObservationInputDto>(Json)
                ?? throw new LessonAnalysisException(400, "So'rov tanasi noto'g'ri");
        }
        catch (JsonException)
        {
            throw new LessonAnalysisException(400, "So'rov tanasi noto'g'ri");
        }
        var present = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidateTexts(input, present);

        var o = await LoadAsync(id, tracked: true, ct);
        EnsureCanEdit(o, actor);

        if (present.Contains("teacherId") && !string.IsNullOrWhiteSpace(input.TeacherId))
        {
            if (!await db.Teachers.AnyAsync(t => t.Id == input.TeacherId, ct))
                throw new LessonAnalysisException(404, TeacherNotFoundMessage);
            o.TeacherId = input.TeacherId;
        }
        if (present.Contains("lessonDate") && input.LessonDate is not null)
            o.LessonDate = ParseDate(input.LessonDate) ?? throw new LessonAnalysisException(400, "Dars sanasi noto'g'ri (yyyy-MM-dd)");
        if (present.Contains("lessonTime")) o.LessonTime = input.LessonTime;
        if (present.Contains("className")) o.ClassName = input.ClassName;
        if (present.Contains("subject")) o.Subject = input.Subject;
        if (present.Contains("strengths")) o.Strengths = input.Strengths;
        if (present.Contains("growthAreas")) o.GrowthAreas = input.GrowthAreas;
        if (present.Contains("nextLessonAdvice")) o.NextLessonAdvice = input.NextLessonAdvice;
        if (present.Contains("details") && input.Details is not null)
            await ApplyDetailsAsync(o, input.Details, ct);

        LessonAnalysisScoring.Recompute(o);
        o.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    /// <summary>
    /// Finalise and generate the AI summary. A failing AI call is recorded on the row but never
    /// blocks completion. Completing a COMPLETED observation is a no-op — a double submit must
    /// not pay for a second model call (the draft → completed switch is an atomic claim, so two
    /// concurrent requests cannot both reach the model either).
    /// </summary>
    public async Task<LaObservationDto> CompleteAsync(string id, LessonAnalysisActor actor, CancellationToken ct = default)
    {
        var o = await LoadAsync(id, tracked: true, ct);
        if (o.Status == LessonObservationStatus.Completed) return await ToDtoAsync(o, CanEdit(o, actor), false, ct);

        LessonAnalysisScoring.Recompute(o);
        if (o.ScoreTotal == 0) throw new LessonAnalysisException(400, "Kamida bitta mezon belgilanishi kerak");

        var now = DateTimeOffset.UtcNow;
        var claimed = await db.LessonObservations
            .Where(x => x.Id == id && x.Status == LessonObservationStatus.Draft)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, LessonObservationStatus.Completed)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (claimed == 0) return await GetAsync(id, actor, ct);

        var result = await SummarizeAsync(o, ct);
        o.Status = LessonObservationStatus.Completed;
        o.CompletedAt = now;
        o.UpdatedAt = now;
        o.AiSummary = result.Summary;
        o.AiError = result.Error;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    /// <summary>Regenerate the AI summary. Provider failure → 502 with the error (and it is recorded).</summary>
    public async Task<LaObservationDto> RegenerateSummaryAsync(string id, LessonAnalysisActor actor, CancellationToken ct = default)
    {
        var o = await LoadAsync(id, tracked: true, ct);
        EnsureCanEdit(o, actor);

        var result = await SummarizeAsync(o, ct);
        o.UpdatedAt = DateTimeOffset.UtcNow;
        if (result.Error is not null && result.Summary is null)
        {
            o.AiError = result.Error;
            await db.SaveChangesAsync(ct);
            throw new LessonAnalysisException(502, result.Error);
        }
        o.AiSummary = result.Summary;
        o.AiError = null;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var o = await db.LessonObservations.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LessonAnalysisException(404, NotFoundMessage);
        db.LessonObservations.Remove(o);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A finished observation is a record about a teacher — only its author or an admin corrects it.</summary>
    private static void EnsureCanEdit(LessonObservation o, LessonAnalysisActor actor)
    {
        if (!CanEdit(o, actor))
            throw new LessonAnalysisException(403,
                "Yakunlangan tahlilni faqat uning muallifi yoki administrator tuzata oladi");
    }

    private static bool CanEdit(LessonObservation o, LessonAnalysisActor actor) =>
        o.Status == LessonObservationStatus.Draft || actor.IsAdmin || o.ObserverUserId == actor.UserId;

    private async Task<LessonObservation> LoadAsync(string id, bool tracked, CancellationToken ct)
    {
        var q = db.LessonObservations.Include(o => o.Details).AsQueryable();
        if (!tracked) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new LessonAnalysisException(404, NotFoundMessage);
    }

    /// <summary>
    /// Old <c>_apply_details</c>: unknown criteria are skipped; a row with no mark and no
    /// comment removes an existing answer (a cleared mark must not survive); anything else is
    /// upserted. Criteria not in the list are left alone.
    /// </summary>
    private async Task ApplyDetailsAsync(LessonObservation o, List<LaDetailDto> details, CancellationToken ct)
    {
        var valid = (await db.LessonObservationCriteria.AsNoTracking().Select(c => c.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var existing = o.Details.ToDictionary(d => d.CriterionId, StringComparer.Ordinal);

        foreach (var item in details)
        {
            if (item is null || string.IsNullOrEmpty(item.CriterionId) || !valid.Contains(item.CriterionId)) continue;
            existing.TryGetValue(item.CriterionId, out var detail);
            if (item.IsDone is null && string.IsNullOrEmpty(item.Comment))
            {
                if (detail is not null)
                {
                    o.Details.Remove(detail);
                    db.LessonObservationDetails.Remove(detail);
                    existing.Remove(item.CriterionId);
                }
                continue;
            }
            if (detail is null)
            {
                detail = new LessonObservationDetail { ObservationId = o.Id, CriterionId = item.CriterionId };
                o.Details.Add(detail);
                // explicit: a string key is client-set, so do not leave the Added state to discovery
                db.LessonObservationDetails.Add(detail);
                existing[item.CriterionId] = detail;
            }
            detail.IsDone = item.IsDone;
            detail.Comment = item.Comment;
        }
    }

    private static void ValidateTexts(LaObservationInputDto input, ISet<string> present)
    {
        void Max(string field, string? value, int max)
        {
            if (present.Contains(field) && value is not null && value.Length > max)
                throw new LessonAnalysisException(400, $"{field}: {max} belgidan oshmasin");
        }
        Max("lessonTime", input.LessonTime, ShortText);
        Max("className", input.ClassName, ShortText);
        Max("subject", input.Subject, SubjectMax);
        Max("strengths", input.Strengths, LongText);
        Max("growthAreas", input.GrowthAreas, LongText);
        Max("nextLessonAdvice", input.NextLessonAdvice, LongText);
        if (present.Contains("details") && input.Details is { } details)
        {
            if (details.Count > MaxDetails) throw new LessonAnalysisException(400, $"details: {MaxDetails} tadan oshmasin");
            if (details.Any(d => d?.Comment is { Length: > CommentText }))
                throw new LessonAnalysisException(400, $"comment: {CommentText} belgidan oshmasin");
        }
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private sealed record CriterionInfo(
        string Id, string Text, int OrderIndex, string SectionId, string SectionTitle, string SectionColor, int SectionOrder);

    private async Task<Dictionary<string, CriterionInfo>> CriteriaInfoAsync(CancellationToken ct) =>
        await (from c in db.LessonObservationCriteria.AsNoTracking()
               join s in db.LessonObservationSections.AsNoTracking() on c.SectionId equals s.Id
               select new CriterionInfo(c.Id, c.Text, c.OrderIndex, s.Id, s.Title, s.Color, s.OrderIndex))
            .ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, ct);

    /// <summary>Details in catalogue order (section, then criterion) with their criterion.</summary>
    private static List<(LessonObservationDetail Detail, CriterionInfo Criterion)> Ordered(
        LessonObservation o, IReadOnlyDictionary<string, CriterionInfo> criteria) =>
        [.. o.Details
            .Where(d => criteria.ContainsKey(d.CriterionId))
            .Select(d => (d, criteria[d.CriterionId]))
            .OrderBy(x => x.Item2.SectionOrder).ThenBy(x => x.Item2.SectionTitle, StringComparer.Ordinal)
            .ThenBy(x => x.Item2.OrderIndex).ThenBy(x => x.Item2.Text, StringComparer.Ordinal)];

    private async Task<LaObservationDto> ToDtoAsync(LessonObservation o, bool canEdit, bool hideAiError, CancellationToken ct)
    {
        var teacherName = await db.Teachers.AsNoTracking().Where(t => t.Id == o.TeacherId).Select(t => t.FullName).FirstOrDefaultAsync(ct) ?? "";
        var observerName = await db.Users.AsNoTracking().Where(u => u.Id == o.ObserverUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        var criteria = await CriteriaInfoAsync(ct);
        return new LaObservationDto(
            o.Id, o.TeacherId, teacherName, o.ObserverUserId, observerName,
            Iso(o.LessonDate), o.LessonTime, o.ClassName, o.Subject, o.Status,
            o.Strengths, o.GrowthAreas, o.NextLessonAdvice,
            o.AiSummary, hideAiError ? null : o.AiError,
            o.ScoreDone, o.ScoreTotal, (double)o.ScorePercent, o.CreatedAt, canEdit,
            [.. Ordered(o, criteria).Select(x => new LaDetailDto(x.Detail.CriterionId, x.Detail.IsDone, x.Detail.Comment))]);
    }

    private async Task<LessonAiResult> SummarizeAsync(LessonObservation o, CancellationToken ct)
    {
        var teacherName = await db.Teachers.AsNoTracking().Where(t => t.Id == o.TeacherId).Select(t => t.FullName).FirstOrDefaultAsync(ct) ?? "";
        var criteria = await CriteriaInfoAsync(ct);
        var input = new LessonAiObservation(
            o.Id, teacherName, o.Subject, o.ClassName, o.LessonDate,
            o.ScoreDone, o.ScoreTotal, o.ScorePercent,
            [.. Ordered(o, criteria).Select(x => new LessonAiDetail(
                x.Criterion.SectionTitle, x.Criterion.Text, x.Detail.IsDone, x.Detail.Comment))],
            o.Strengths, o.GrowthAreas, o.NextLessonAdvice);
        try
        {
            return await ai.SummarizeAsync(input, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // best-effort: the contract says "never throws", a faulty implementation must not 500 us
            return new LessonAiResult(null, $"AI xulosa yaratishda xato: {ex.Message}");
        }
    }

    // =====================================================================
    //  Reports
    // =====================================================================

    /// <summary>PDF of one observation for staff (any status).</summary>
    public async Task<(byte[] Pdf, string FileName)> ReportPdfAsync(string id, CancellationToken ct = default) =>
        await RenderPdfAsync(await LoadAsync(id, tracked: false, ct), ct);

    /// <summary>PDF of a teacher's own COMPLETED observation; anything else is 404.</summary>
    public async Task<(byte[] Pdf, string FileName)> ReportPdfForTeacherAsync(string id, string? teacherId, CancellationToken ct = default) =>
        await RenderPdfAsync(await LoadForTeacherAsync(id, teacherId, ct), ct);

    private async Task<(byte[] Pdf, string FileName)> RenderPdfAsync(LessonObservation o, CancellationToken ct)
    {
        var teacherName = await db.Teachers.AsNoTracking().Where(t => t.Id == o.TeacherId).Select(t => t.FullName).FirstOrDefaultAsync(ct) ?? "";
        var observerName = await db.Users.AsNoTracking().Where(u => u.Id == o.ObserverUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        var criteria = await CriteriaInfoAsync(ct);
        var model = new LessonReportModel(
            teacherName, observerName, o.LessonDate, o.LessonTime, o.Subject, o.ClassName,
            o.ScoreDone, o.ScoreTotal, o.ScorePercent,
            [.. Ordered(o, criteria).Select(x => new LessonReportRow(
                x.Criterion.SectionId, x.Criterion.SectionTitle, x.Criterion.SectionColor,
                x.Criterion.Text, x.Detail.IsDone, x.Detail.Comment))],
            o.Strengths, o.GrowthAreas, o.NextLessonAdvice, o.AiSummary);
        return (new LessonObservationPdf(model).Render(), LessonObservationPdf.FileName(teacherName, o.LessonDate));
    }

    /// <summary>Rating board + completed-observation log as .xlsx (excel.py).</summary>
    public async Task<(byte[] Xlsx, string FileName)> RatingXlsxAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var board = await RatingAsync(from, to, ct);
        var q = db.LessonObservations.AsNoTracking().Where(o => o.Status == LessonObservationStatus.Completed);
        if (from is { } f) q = q.Where(o => o.LessonDate >= f);
        if (to is { } t) q = q.Where(o => o.LessonDate <= t);
        var log = await (
                from o in q
                join te in db.Teachers on o.TeacherId equals te.Id
                join u in db.Users on o.ObserverUserId equals u.Id
                orderby o.LessonDate descending, o.CreatedAt descending
                select new LessonExcelObservation(
                    o.LessonDate, te.FullName, o.Subject, o.ClassName, u.FullName,
                    o.ScoreDone, o.ScoreTotal, o.ScorePercent, o.Status))
            .ToListAsync(ct);
        return (LessonAnalysisExcel.Build(board.Rows, log, from, to), LessonAnalysisExcel.FileName(AppClock.Today));
    }

    // =====================================================================
    //  Rating, dashboard, analytics (rating.py / dashboard.py)
    //  Only COMPLETED observations count, and only ANSWERED details count.
    // =====================================================================

    private sealed record TeacherAggregate(TeacherInfo Teacher, int Count, double Mean, DateOnly Last);

    /// <summary>Per-teacher aggregates of completed observations, best mean first (rating_rows).</summary>
    private static List<TeacherAggregate> Aggregate(
        IEnumerable<ObservationLite> completed, IReadOnlyDictionary<string, TeacherInfo> teachers) =>
        [.. completed
            .Where(o => teachers.ContainsKey(o.TeacherId))
            .GroupBy(o => o.TeacherId, StringComparer.Ordinal)
            .Select(g => new TeacherAggregate(
                teachers[g.Key], g.Count(), LessonAnalysisScoring.Mean(g.Select(o => o.ScorePercent))!.Value,
                g.Max(o => o.LessonDate)))
            .OrderByDescending(a => a.Mean)
            .ThenBy(a => a.Teacher.FullName, StringComparer.OrdinalIgnoreCase)];

    private static LaRatingRowDto RatingRow(TeacherAggregate a, IReadOnlyDictionary<string, string> subjects) =>
        new(a.Teacher.Id, a.Teacher.FullName, SubjectText(a.Teacher.SubjectIds, subjects), a.Count,
            LessonAnalysisScoring.PyRound(a.Mean, 2), Iso(a.Last));

    /// <summary>
    /// Leaderboard: average score_percent of COMPLETED observations in the date range (on
    /// lesson_date), rounded to 2 — plus active teachers with no completed observation in it.
    /// </summary>
    public async Task<LaRatingBoardDto> RatingAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var teachers = await TeacherMapAsync(ct);
        var subjects = await SubjectNamesAsync(ct);
        var completed = await ObservationsLiteAsync(LessonObservationStatus.Completed, from, to, null, ct);
        var rows = Aggregate(completed, teachers).Select(a => RatingRow(a, subjects)).ToList();

        var rated = rows.Select(r => r.TeacherId).ToHashSet(StringComparer.Ordinal);
        var notEvaluated = teachers.Values
            .Where(t => !t.IsArchived && !rated.Contains(t.Id))
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(t => TeacherDto(t, subjects))
            .ToList();
        return new LaRatingBoardDto(rows, notEvaluated);
    }

    /// <summary>Headline counters (old <c>GET /dashboard</c>, staff view).</summary>
    public async Task<LaDashboardDto> DashboardAsync(CancellationToken ct = default)
    {
        var teachers = await TeacherMapAsync(ct);
        var subjects = await SubjectNamesAsync(ct);
        var all = await ObservationsLiteAsync(null, null, null, null, ct);
        var completed = all.Where(o => o.Status == LessonObservationStatus.Completed).ToList();
        var since = AppClock.Today.AddDays(-30);

        var rows = Aggregate(completed, teachers).Select(a => RatingRow(a, subjects)).ToList();

        // teachers_without_evaluations: active and never observed at all (drafts count as observed)
        var observed = all.Select(o => o.TeacherId).ToHashSet(StringComparer.Ordinal);
        var missing = teachers.Values
            .Where(t => !t.IsArchived && !observed.Contains(t.Id))
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.FullName)
            .ToList();

        // average of the (already rounded) ratings, summed as floats in board order
        double sum = 0;
        foreach (var r in rows) sum += r.Rating;
        var average = rows.Count == 0 ? 0.0 : LessonAnalysisScoring.PyRound(sum / rows.Count, 1);

        return new LaDashboardDto(
            teachers.Values.Count(t => !t.IsArchived),
            completed.Count,
            completed.Count(o => o.LessonDate >= since),
            average,
            all.Count(o => o.Status == LessonObservationStatus.Draft),
            missing,
            [.. rows.Take(5)],
            rows.Count > 5 ? [.. rows.Skip(rows.Count - 5).Reverse()] : []);
    }

    /// <summary>
    /// Chart data over COMPLETED observations. <paramref name="teacherId"/> narrows every
    /// aggregate to one teacher — that is how a teacher is kept inside their own data.
    /// </summary>
    public async Task<LaAnalyticsDto> AnalyticsAsync(
        DateOnly? from, DateOnly? to, string? teacherId, CancellationToken ct = default)
    {
        var teachers = await TeacherMapAsync(ct);
        var subjects = await SubjectNamesAsync(ct);
        var completed = await ObservationsLiteAsync(LessonObservationStatus.Completed, from, to, teacherId, ct);

        // --- monthly: the 12 most recent months that have observations, oldest first
        var monthly = completed
            .GroupBy(o => o.LessonDate.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Take(12)
            .Select(g => new LaMonthlyPointDto(
                g.Key, g.Count(), LessonAnalysisScoring.PyRound(LessonAnalysisScoring.Mean(g.Select(o => o.ScorePercent))!.Value, 1)))
            .Reverse()
            .ToList();

        // --- answered details inside the scope (unanswered criteria never drag a percentage down)
        var ids = completed.Select(o => o.Id).ToList();
        var answered = await db.LessonObservationDetails.AsNoTracking()
            .Where(d => d.IsDone != null && ids.Contains(d.ObservationId))
            .Select(d => new { d.CriterionId, IsDone = d.IsDone!.Value })
            .ToListAsync(ct);

        var criteria = await CriteriaInfoAsync(ct);
        var sectionRows = await db.LessonObservationSections.AsNoTracking()
            .OrderBy(s => s.OrderIndex).ThenBy(s => s.Title)
            .Select(s => new { s.Id, s.Title, s.Color })
            .ToListAsync(ct);

        var sections = sectionRows.Select(s =>
        {
            var inSection = answered.Where(a => criteria.TryGetValue(a.CriterionId, out var c) && c.SectionId == s.Id).ToList();
            var done = inSection.Count(a => a.IsDone);
            return new LaSectionProgressDto(s.Id, s.Title, s.Color, done, inSection.Count,
                LessonAnalysisScoring.Percentage(done, inSection.Count));
        }).ToList();

        // --- distribution: how many teachers fall into each rating bucket (always all four)
        var means = completed
            .GroupBy(o => o.TeacherId, StringComparer.Ordinal)
            .Select(g => LessonAnalysisScoring.Mean(g.Select(o => o.ScorePercent))!.Value)
            .ToList();
        int[] counts =
        [
            means.Count(m => m < 60),
            means.Count(m => m >= 60 && m < 75),
            means.Count(m => m >= 75 && m < 90),
            means.Count(m => m >= 90),
        ];
        var distribution = DistributionBuckets.Select((b, i) => new LaDistributionBucketDto(b, counts[i])).ToList();

        // --- top teachers: the rating formula, rounded to 1, best first (8)
        var top = Aggregate(completed, teachers)
            .OrderByDescending(a => a.Mean)
            .ThenBy(a => a.Teacher.FullName, StringComparer.Ordinal)
            .Take(8)
            .Select(a => new LaTopTeacherDto(a.Teacher.Id, a.Teacher.FullName,
                SubjectText(a.Teacher.SubjectIds, subjects), LessonAnalysisScoring.PyRound(a.Mean, 1), a.Count))
            .ToList();

        // --- criteria most often marked "not done", worst first (6)
        var misses = answered
            .Where(a => criteria.ContainsKey(a.CriterionId))
            .GroupBy(a => a.CriterionId, StringComparer.Ordinal)
            .Select(g => new { Criterion = criteria[g.Key], Missed = g.Count(a => !a.IsDone), Total = g.Count() })
            .OrderByDescending(x => (decimal)x.Missed * 100m / x.Total)
            .ThenByDescending(x => x.Total)
            .ThenBy(x => x.Criterion.SectionOrder).ThenBy(x => x.Criterion.OrderIndex)
            .ThenBy(x => x.Criterion.Id, StringComparer.Ordinal)
            .Take(6)
            .Select(x => new LaCriterionMissDto(x.Criterion.Id, x.Criterion.Text, x.Criterion.SectionTitle,
                x.Missed, x.Total, LessonAnalysisScoring.Percentage(x.Missed, x.Total)))
            .ToList();

        return new LaAnalyticsDto(monthly, sections, distribution, top, misses);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
