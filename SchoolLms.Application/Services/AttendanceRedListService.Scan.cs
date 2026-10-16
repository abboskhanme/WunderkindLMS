using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  The red-list scan (docs/modules/attendance-red-list.md).
//
//  Reads every active pupil's days of the last month through the attendance report itself
//  (AttendanceAnalytics.BuildAsync — one definition of "absent the whole day"), applies
//  RedListCriteria and:
//    · opens a case ("aloqa_kerak") for a pupil who meets a rule and has no open case;
//    · refreshes the numbers of open cases (streak never goes down, "qaytdi" = came back);
//    · never closes a case — only a person does, with "hal_boldi".
//  Running it twice changes nothing the second time (one open case per pupil, unique index).
//
//  The scheduler (AttendanceRedListScheduler) runs it at 13:00 (after the nurse's roll-call,
//  09:00–12:40 on prod) and 18:00, school days only; the admin page has "Hozir tekshirish".
// ===========================================================================

public sealed partial class AttendanceRedListService
{
    /// <summary>Scheduled scans, school time. 13:00 also sends the management digest.</summary>
    public static readonly IReadOnlyList<string> ScanTimes = ["13:00", "18:00"];

    /// <summary>Days a scan reads: at least a month, so a long run is still counted whole.</summary>
    public const int MinLookbackDays = 31;

    /// <summary>One scan at a time in this process (the button and the scheduler may meet).</summary>
    private static readonly SemaphoreSlim ScanLock = new(1, 1);

    /// <summary>Scans the school as of <paramref name="today"/> and tells teachers about new cases.</summary>
    public async Task<AttendanceScanResultDto> ScanAsync(DateOnly today, CancellationToken ct = default)
    {
        await ScanLock.WaitAsync(ct);
        try
        {
            return await ScanCoreAsync(today, ct);
        }
        finally
        {
            ScanLock.Release();
        }
    }

    private async Task<AttendanceScanResultDto> ScanCoreAsync(DateOnly today, CancellationToken ct)
    {
        var settings = await TrackedSettingsAsync(ct);
        var lookback = Math.Max(settings.WindowDays, MinLookbackDays);
        var from = today.AddDays(-(lookback - 1));

        var reasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var unexcused = reasons.Where(AttendanceAnalytics.IsUnexcusedReason)
            .Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var days = new Dictionary<string, List<(DateOnly Date, RedListDay Kind)>>(StringComparer.Ordinal);
        await AttendanceAnalytics.BuildAsync(db, null, Iso(from), Iso(today), Iso(today), maxEvents: 0,
            onStudentDay: (studentId, date, day) =>
            {
                var kind = RedListCriteria.Classify(day, unexcused, settings.CountExcused);
                if (kind is null || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d)) return;
                if (!days.TryGetValue(studentId, out var list)) days[studentId] = list = [];
                list.Add((d, kind.Value));
            });

        var open = await db.AttendanceCases.Where(c => c.ClosedAt == null).ToListAsync(ct);
        var openOf = open.ToDictionary(c => c.StudentId, StringComparer.Ordinal);
        var ids = days.Keys.ToList();
        // After a resolved case only later days count — resolving must not reopen it on the next scan.
        var sinceOf = (await db.AttendanceCases.AsNoTracking()
                .Where(c => c.ClosedAt != null && ids.Contains(c.StudentId))
                .Select(c => new { c.StudentId, c.LastAbsentOn, c.ClosedAt })
                .ToListAsync(ct))
            .GroupBy(c => c.StudentId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Max(c => c.LastAbsentOn ?? AppClock.LocalDateOf(c.ClosedAt!.Value)),
                StringComparer.Ordinal);

        var now = AppClock.NowInstant;
        var opened = new List<AttendanceCase>();
        var updated = 0;
        foreach (var (studentId, list) in days)
        {
            var verdict = RedListCriteria.Evaluate(
                list, today, settings, sinceOf.TryGetValue(studentId, out var since) ? since : null);
            if (openOf.TryGetValue(studentId, out var existing))
            {
                if (Refresh(existing, verdict, now)) updated++;
                continue;
            }
            if (!verdict.Opens) continue;

            var c = new AttendanceCase
            {
                StudentId = studentId,
                OpenedOn = today,
                Trigger = verdict.Trigger,
                Streak = verdict.LatestRun,
                WindowCount = verdict.WindowCount,
                LastAbsentOn = verdict.LastAbsentOn,
                LastPresentOn = verdict.LastPresentOn,
                AbsentDates = [.. verdict.AbsentDates],
                Status = AttendanceCaseStatus.ContactNeeded,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AttendanceCases.Add(c);
            opened.Add(c);
            db.AuditLogs.Add(AuditService.Entry(
                AuditEntity, c.Id.ToString("D"), "open",
                "Qizil ro'yxatga kiritildi: "
                + ReasonText(c.Trigger, c.Streak, c.WindowCount, settings.WindowDays, settings.CountExcused) + ".",
                actorId: null, actorName: "Tizim", after: Snapshot(c), studentId: studentId));
        }

        settings.LastScanAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            // Another instance opened the same pupil's case a moment earlier; the next scan catches up.
            throw new AttendanceCaseException(409, "Tekshiruv bir vaqtda ikki marta ishga tushdi — birozdan keyin qayta urinib ko'ring.");
        }

        var notified = await NotifyTeachersAsync(opened, settings, ct);
        log.LogInformation(
            "Qizil ro'yxat tekshiruvi {Today}: o'quvchi={Pupils}, yangi={Opened}, yangilangan={Updated}, xabar={Notified}",
            today, days.Count, opened.Count, updated, notified);
        return new AttendanceScanResultDto(days.Count, opened.Count, updated, open.Count + opened.Count, notified);
    }

    /// <summary>Brings an open case's numbers up to date; true when something changed.</summary>
    private static bool Refresh(AttendanceCase c, RedListVerdict v, DateTimeOffset now)
    {
        var streak = Math.Max(c.Streak, v.LatestRun);
        var lastAbsent = v.LastAbsentOn ?? c.LastAbsentOn;
        var lastPresent = v.LastPresentOn ?? c.LastPresentOn;
        List<DateOnly> dates = v.AbsentDates.Count > 0 ? [.. v.AbsentDates] : c.AbsentDates;
        if (streak == c.Streak && v.WindowCount == c.WindowCount && lastAbsent == c.LastAbsentOn
            && lastPresent == c.LastPresentOn && dates.SequenceEqual(c.AbsentDates)) return false;

        c.Streak = streak;
        c.WindowCount = v.WindowCount;
        c.LastAbsentOn = lastAbsent;
        c.LastPresentOn = lastPresent;
        c.AbsentDates = dates;
        c.UpdatedAt = now;
        return true;
    }

    // =====================================================================
    //  Scheduling
    // =====================================================================

    /// <summary>
    /// Runs the scan if a slot is due (school days only: not Sunday, not a holiday) and, once a day,
    /// the management digest. Null — nothing was due.
    /// </summary>
    public async Task<AttendanceScanResultDto?> RunDueAsync(DateTime localNow, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(localNow);
        if (today.DayOfWeek == DayOfWeek.Sunday) return null;
        var iso = Iso(today);
        if (await db.Holidays.AsNoTracking().AnyAsync(h => h.Date == iso, ct)) return null;

        var slot = DueSlot(localNow, (await SettingsAsync(ct)).LastScanSlot);
        if (slot is null) return null;

        var result = await ScanAsync(today, ct);
        var settings = await TrackedSettingsAsync(ct);
        settings.LastScanSlot = slot;
        if (settings.NotifyManagement && settings.LastDigestOn != today)
        {
            await SendDigestAsync(today, settings, ct);
            settings.LastDigestOn = today;
        }
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>The latest scan time already reached today ("2026-10-15 13:00"), unless that slot already ran.</summary>
    public static string? DueSlot(DateTime localNow, string? lastSlot)
    {
        var time = TimeOnly.FromDateTime(localNow);
        string? due = null;
        foreach (var at in ScanTimes)
            if (time >= TimeOnly.ParseExact(at, "HH:mm", CultureInfo.InvariantCulture))
                due = $"{Iso(DateOnly.FromDateTime(localNow))} {at}";
        return due is null || due == lastSlot ? null : due;
    }
}
