using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  KECHKI DARS VA YOTOQXONA STATISTIKASI — "DAVOMAT ANALITIKASI 2.0" (2026-10-11)
// ===========================================================================
//
//  Mijoz: "Davomat analitikasi 2.0 qilish, uni ichida kechki va yotoqxona statistikasi bo'ladi."
//
//  · BIRLIK — o'quvchi-kun: ro'yxatdagi (boarding_rosters) o'quvchining bitta kuni.
//  · YO'QLAMA KUNI — shu sessiyada kamida bitta belgi qo'yilgan kun. Dam olish yoki yo'qlama
//    olinmagan kunlar hisobga kirmaydi — aks holda ular butun ro'yxat "belgilanmagan" bo'lib
//    chiqardi.
//  · HOLAT — keldi (kech kelgan ham, sababi bilan), kelmadi (sababsiz), sababli (ruxsat bilan),
//    belgilanmagan (yo'qlama kunida ro'yxatda turib belgisi yo'q).
//  · GURUH — o'quvchining hozirgi yo'nalish guruhi, bo'lmasa sinfi ("Guruhsiz · 9-A").
// ===========================================================================

public sealed record BoardingTallyDto(
    int StudentDays, int Present, int Absent, int Excused, int Late, int Unchecked,
    double? PresentPct, double? AbsentPct);

public sealed record BoardingTrendPointDto(string Date, BoardingTallyDto Tally);

public sealed record BoardingGroupRowDto(string Key, string Name, int Students, BoardingTallyDto Tally);

public sealed record BoardingReasonRowDto(string ReasonId, string Name, bool IsLate, int Count);

/// <param name="LastAbsent">Oxirgi kelmagan (yoki sababli) kuni.</param>
public sealed record BoardingStudentRowDto(
    string StudentId, string FullName, string ClassName, string GroupName, BoardingTallyDto Tally, string? LastAbsent);

/// <summary>Oddiy "keldi" bo'lmagan bitta o'quvchi-kun.</summary>
public sealed record BoardingEventDto(
    string Date, string StudentId, string FullName, string GroupName, string Status, string? ReasonId);

public sealed record BoardingAnalyticsDto(
    string Session, string From, string To, int HeldDays, int RosterNow,
    BoardingTallyDto Total, List<BoardingTrendPointDto> Trend, List<BoardingGroupRowDto> Groups,
    List<BoardingReasonRowDto> Reasons, List<BoardingStudentRowDto> Students, List<BoardingEventDto> Events,
    bool EventsTruncated);

/// <inheritdoc cref="BoardingAnalyticsDto"/>
public static class BoardingAnalytics
{
    public const int MaxEvents = 5000;

    /// <summary>Ko'pi bilan bir o'quv yili — so'rov bitta jadvalni to'liq o'qiydi.</summary>
    public const int MaxDays = 400;

    private sealed class Tally
    {
        public int StudentDays, Present, Absent, Excused, Late, Unchecked;

        public void Add(string status, bool late)
        {
            StudentDays++;
            switch (status)
            {
                case BoardingStatus.Present: Present++; if (late) Late++; break;
                case BoardingStatus.Absent: Absent++; break;
                case BoardingStatus.Excused: Excused++; break;
                default: Unchecked++; break;
            }
        }

        public BoardingTallyDto ToDto()
        {
            var marked = Present + Absent + Excused;
            return new BoardingTallyDto(StudentDays, Present, Absent, Excused, Late, Unchecked,
                marked == 0 ? null : Math.Round(100.0 * Present / marked, 1),
                marked == 0 ? null : Math.Round(100.0 * (Absent + Excused) / marked, 1));
        }
    }

    /// <summary>Davr xatosi (foydalanuvchiga), yoki null.</summary>
    public static string? RangeError(DateOnly from, DateOnly to) =>
        to < from ? "Davr oxiri boshidan oldin"
        : to.DayNumber - from.DayNumber > MaxDays ? $"Davr {MaxDays} kundan oshmasin"
        : null;

    public static async Task<BoardingAnalyticsDto> BuildAsync(
        IAppDbContext db, string session, DateOnly from, DateOnly to, int maxEvents = MaxEvents, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        if (to > today) to = today;

        var roster = await db.BoardingRosters.AsNoTracking()
            .Where(r => r.Session == session && r.AddedOn <= to && (r.RemovedOn == null || r.RemovedOn > from))
            .Select(r => new { r.StudentId, r.AddedOn, r.RemovedOn }).ToListAsync(ct);
        var marks = await db.BoardingAttendance.AsNoTracking()
            .Where(a => a.Session == session && a.Date >= from && a.Date <= to)
            .Select(a => new { a.StudentId, a.Date, a.Status, a.ReasonId }).ToListAsync(ct);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var lateIds = reasons.Where(r => r.IsLate).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var studentIds = roster.Select(r => r.StudentId).Concat(marks.Select(m => m.StudentId))
            .Distinct(StringComparer.Ordinal).ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToDictionaryAsync(s => s.Id, ct);
        var track = (await (from m in db.StudyGroupMembers.AsNoTracking()
                            join g in db.StudyGroups.AsNoTracking() on m.GroupId equals g.Id
                            where g.IsTrack && !g.IsArchived && m.LeftOn == null && studentIds.Contains(m.StudentId)
                            select new { m.StudentId, g.Id, g.Name }).ToListAsync(ct))
            .GroupBy(x => x.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Key: g.First().Id.ToString(), g.First().Name), StringComparer.Ordinal);
        (string Key, string Name) GroupOf(string studentId)
        {
            if (track.TryGetValue(studentId, out var t)) return t;
            var cls = students.TryGetValue(studentId, out var st) ? st.ClassName ?? "" : "";
            return ("class:" + cls, cls == "" ? "Guruhsiz · sinfsiz" : $"Guruhsiz · {cls}");
        }

        var markAt = marks.GroupBy(m => (m.StudentId, m.Date)).ToDictionary(g => g.Key, g => g.First());
        var marksByDay = marks.ToLookup(m => m.Date);
        var heldDays = marks.Select(m => m.Date).Distinct().OrderBy(d => d).ToList();

        var total = new Tally();
        var byDay = new SortedDictionary<DateOnly, Tally>();
        var byGroup = new Dictionary<string, (string Name, Tally Tally, HashSet<string> Students)>(StringComparer.Ordinal);
        var byStudent = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var lastAbsent = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        var reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var events = new List<BoardingEventDto>();

        foreach (var day in heldDays)
        {
            // Shu kuni ro'yxatda turganlar + (ro'yxatdan tashqari) belgi qo'yilganlar — eski yozuvlar uchun.
            var onDay = roster.Where(r => r.AddedOn <= day && (r.RemovedOn == null || r.RemovedOn > day))
                .Select(r => r.StudentId)
                .Concat(marksByDay[day].Select(m => m.StudentId))
                .Distinct(StringComparer.Ordinal);
            var dayTally = new Tally();
            foreach (var id in onDay)
            {
                if (!students.ContainsKey(id)) continue;
                markAt.TryGetValue((id, day), out var mark);
                var status = mark?.Status ?? "unchecked";
                var late = mark is not null && mark.Status == BoardingStatus.Present && mark.ReasonId is not null
                           && lateIds.Contains(mark.ReasonId);
                total.Add(status, late);
                dayTally.Add(status, late);
                var (gKey, gName) = GroupOf(id);
                if (!byGroup.TryGetValue(gKey, out var g)) byGroup[gKey] = g = (gName, new Tally(), new HashSet<string>(StringComparer.Ordinal));
                g.Tally.Add(status, late);
                g.Students.Add(id);
                if (!byStudent.TryGetValue(id, out var st)) byStudent[id] = st = new Tally();
                st.Add(status, late);
                if (mark?.ReasonId is { } rid) reasonCounts[rid] = reasonCounts.GetValueOrDefault(rid) + 1;
                if (status is BoardingStatus.Absent or BoardingStatus.Excused)
                    if (!lastAbsent.TryGetValue(id, out var la) || day > la) lastAbsent[id] = day;
                if (status != BoardingStatus.Present || late)
                    events.Add(new BoardingEventDto(Iso(day), id, students[id].FullName, gName,
                        late ? "late" : status, mark?.ReasonId));
            }
            byDay[day] = dayTally;
        }

        var rosterNow = roster.Count(r => r.AddedOn <= today && (r.RemovedOn == null || r.RemovedOn > today)
                                         && students.ContainsKey(r.StudentId));
        var orderedEvents = events.OrderByDescending(e => e.Date, StringComparer.Ordinal)
            .ThenBy(e => e.GroupName, StringComparer.Ordinal).ThenBy(e => e.FullName, StringComparer.Ordinal).ToList();

        return new BoardingAnalyticsDto(
            session, Iso(from), Iso(to), heldDays.Count, rosterNow, total.ToDto(),
            [.. byDay.Select(kv => new BoardingTrendPointDto(Iso(kv.Key), kv.Value.ToDto()))],
            [.. byGroup.OrderBy(kv => kv.Key.StartsWith("class:", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(kv => kv.Value.Name, StringComparer.Ordinal)
                .Select(kv => new BoardingGroupRowDto(kv.Key, kv.Value.Name, kv.Value.Students.Count, kv.Value.Tally.ToDto()))],
            [.. reasons.Where(r => reasonCounts.ContainsKey(r.Id))
                .OrderByDescending(r => reasonCounts[r.Id]).ThenBy(r => r.Name, StringComparer.Ordinal)
                .Select(r => new BoardingReasonRowDto(r.Id, r.Name, r.IsLate, reasonCounts[r.Id]))],
            [.. byStudent.Select(kv =>
                {
                    var s = students[kv.Key];
                    return new BoardingStudentRowDto(kv.Key, s.FullName, s.ClassName ?? "", GroupOf(kv.Key).Name,
                        kv.Value.ToDto(), lastAbsent.TryGetValue(kv.Key, out var la) ? Iso(la) : null);
                })
                .OrderByDescending(r => r.Tally.Absent).ThenByDescending(r => r.Tally.Excused)
                .ThenBy(r => r.FullName, StringComparer.Ordinal)],
            [.. orderedEvents.Take(maxEvents)],
            orderedEvents.Count > maxEvents);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
