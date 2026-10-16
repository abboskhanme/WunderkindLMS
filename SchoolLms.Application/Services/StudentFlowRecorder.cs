using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>Who wrote a <see cref="StudentEvent"/>.</summary>
public sealed record StudentEventActor(string? Id, string? Name)
{
    /// <summary>
    /// The signed-in user of the request (the same claims <see cref="AuditService.Record"/> reads);
    /// no user (a controller called without a request) — "Tizim".
    /// </summary>
    public static StudentEventActor From(ClaimsPrincipal? user) => new(
        user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value,
        user?.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim");
}

/// <summary>
/// Writes pupils' arrivals, departures and returns (docs/modules/student-flow.md). Every path
/// that adds, archives, deletes or brings back a pupil calls it next to its own change, before
/// its <c>SaveChanges</c> — the event lands in the same transaction.
///
/// <para>
/// <b>The class is taken BEFORE it is blanked.</b> Archiving empties <c>students.class_name</c>
/// (StudentArchiveService.LeaveClassAndGroupsAsync), so callers pass the class they read first.
/// </para>
/// </summary>
public sealed class StudentFlowRecorder(IAppDbContext db, StudentEventActor actor)
{
    /// <summary>Seeded catalog row "Maktabni bitirdi" (parity_wave2_seed.sql).</summary>
    public static readonly Guid GraduationReasonSeedId = new("00000000-0000-0000-0000-0000000000a1");
    public const string GraduationReasonName = "Maktabni bitirdi";

    private Dictionary<string, string>? _classIds;

    public Task EnrolledAsync(Student s, string source, CancellationToken ct = default) =>
        AddAsync(s, StudentEventKind.Enrolled, EnrollmentDay(s), s.ClassName, null, null, null, source, ct);

    /// <param name="className">The class the pupil was in at that moment (read before archiving blanks it).</param>
    /// <param name="onDate">Default today.</param>
    public Task LeftAsync(
        Student s, string? className, Guid? reasonId, string? reasonText, string source,
        CancellationToken ct = default, string? classId = null, DateOnly? onDate = null) =>
        AddAsync(s, StudentEventKind.Left, onDate ?? AppClock.Today, className, classId, reasonId, reasonText, source, ct);

    public Task ReturnedAsync(
        Student s, string? className, string source, CancellationToken ct = default, string? classId = null) =>
        AddAsync(s, StudentEventKind.Returned, AppClock.Today, className, classId, null, null, source, ct);

    /// <summary>The catalog's "Maktabni bitirdi" row — by its seeded id, else by name; null if neither exists.</summary>
    public async Task<Guid?> GraduationReasonIdAsync(CancellationToken ct = default) =>
        await db.StudentArchiveReasons.AsNoTracking()
            .Where(r => r.Id == GraduationReasonSeedId || r.Name == GraduationReasonName)
            .OrderBy(r => r.Id == GraduationReasonSeedId ? 0 : 1)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>The enrolment day on the card; today when it is empty or unreadable.</summary>
    public static DateOnly EnrollmentDay(Student s) =>
        DateOnly.TryParseExact(s.EnrollmentDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : AppClock.Today;

    private async Task AddAsync(
        Student s, string kind, DateOnly on, string? className, string? classId,
        Guid? reasonId, string? reasonText, string source, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(className) ? null : className.Trim();
        classId ??= name is null ? null : await ClassIdAsync(name, ct);
        var text = string.IsNullOrWhiteSpace(reasonText) ? null : reasonText.Trim();
        db.StudentEvents.Add(new StudentEvent
        {
            StudentId = s.Id,
            StudentName = Clip(s.FullName, 300),
            Kind = kind,
            OnDate = on,
            ClassId = classId,
            ClassName = name is null ? null : Clip(name, 100),
            ReasonId = kind == StudentEventKind.Left ? reasonId : null,
            ReasonText = kind == StudentEventKind.Left && text is not null ? Clip(text, 500) : null,
            Source = source,
            ActorId = actor.Id,
            ActorName = actor.Name,
            CreatedAt = AppClock.NowInstant,
        });
    }

    /// <summary>Class id by name — the class list is loaded once per recorder (bulk paths call this in a loop).</summary>
    private async Task<string?> ClassIdAsync(string name, CancellationToken ct)
    {
        _classIds ??= (await db.Classes.AsNoTracking().Select(c => new { c.Name, c.Id }).ToListAsync(ct))
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        return _classIds.GetValueOrDefault(name);
    }

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;
}
