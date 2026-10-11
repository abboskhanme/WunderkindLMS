using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Qo'shimcha qo'ng'iroq jadvallarini boshqarish (Sozlamalar → Dars vaqtlari, 2026-10-11).
/// Qoidalar: <c>SchoolLms.Domain/BellSchedules.cs</c>. Asosiy jadval bu yerda emas — u avvalgidek
/// <c>PUT api/admin/settings/lesson-times</c> orqali saqlanadi.
/// </summary>
public static class BellScheduleService
{
    /// <summary>Jadval muharrirlari 10 ta soat ko'rsatadi (<c>schedulePeriods</c>) — undan ortig'ini qo'yib bo'lmaydi.</summary>
    public const int MaxPeriods = 10;

    public static async Task<List<BellScheduleDto>> ListAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var schedules = await db.BellSchedules.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var periods = (await db.BellSchedulePeriods.AsNoTracking().ToListAsync(ct)).ToLookup(p => p.ScheduleId);
        var owners = (await db.BellScheduleOwners.AsNoTracking().ToListAsync(ct)).ToLookup(o => o.ScheduleId);
        return [.. schedules.Select(s => new BellScheduleDto(
            s.Id, s.Name,
            [.. periods[s.Id].OrderBy(p => p.Period).Select(p => new LessonTimeDto(p.Period, p.StartTime, p.EndTime))],
            [.. owners[s.Id].Select(o => o.OwnerId).Order(StringComparer.Ordinal)]))];
    }

    /// <summary>Sozlamalar sahifasi: asosiy jadval, qo'shimchalar va biriktiriladigan egalar.</summary>
    public static async Task<BellSchedulesDto> OverviewAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var defaults = await db.LessonTimes.AsNoTracking().OrderBy(t => t.Period)
            .Select(t => new LessonTimeDto(t.Period, t.StartTime, t.EndTime)).ToListAsync(ct);
        var schedules = await ListAsync(db, ct);
        var assigned = schedules.SelectMany(s => s.OwnerIds.Select(o => (Owner: o, s.Id)))
            .ToDictionary(x => x.Owner, x => x.Id, StringComparer.Ordinal);
        var owners = (await LessonRoster.PickerOwnersAsync(db, ct: ct))
            .Select(o => new BellOwnerOptionDto(o.Id, o.Name, o.Kind, o.IsTrack, o.Grade,
                assigned.TryGetValue(o.Id, out var sid) ? sid : null))
            .ToList();
        return new BellSchedulesDto(defaults, schedules, owners);
    }

    /// <summary>Yangi jadval (<paramref name="id"/> null) yoki mavjudini to'liq almashtirish.</summary>
    /// <returns>Saqlangan jadval; xato bo'lsa — foydalanuvchiga matn. Jadval topilmasa ikkalasi ham null.</returns>
    public static async Task<(BellScheduleDto? Schedule, string? Error)> SaveAsync(
        IAppDbContext db, Guid? id, SaveBellScheduleRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return (null, "Jadval nomini kiriting");
        if (name.Length > 120) return (null, "Nom 120 belgidan oshmasin");
        var lower = name.ToLowerInvariant();
        if (await db.BellSchedules.AnyAsync(s => s.Name.ToLower() == lower && (id == null || s.Id != id), ct))
            return (null, "Bu nomli jadval allaqachon bor");

        var (periods, periodError) = NormalizePeriods(req.Periods);
        if (periodError is not null) return (null, periodError);

        var ownerIds = (req.OwnerIds ?? []).Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.Ordinal).ToList();
        var groupIds = ownerIds.Select(o => Guid.TryParse(o, out var g) ? g : (Guid?)null)
            .Where(g => g is not null).Select(g => g!.Value).ToList();
        var known = (await db.Classes.Where(c => ownerIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct))
            .Concat((await db.StudyGroups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct))
                .Select(g => g.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        // O'chirilgan sinf/guruh shu jadvalda qolib ketgan bo'lsa — jimgina tushib qoladi (aks holda
        // jadvalni hech qachon saqlab bo'lmasdi); yangi noma'lum id esa xato.
        var stored = id is { } sid
            ? (await db.BellScheduleOwners.Where(o => o.ScheduleId == sid).Select(o => o.OwnerId).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal)
            : [];
        if (ownerIds.Any(o => !known.Contains(o) && !stored.Contains(o)))
            return (null, "Tanlangan sinf yoki guruhlardan biri topilmadi");
        ownerIds = [.. ownerIds.Where(known.Contains)];

        BellSchedule? schedule;
        if (id is { } existingId)
        {
            schedule = await db.BellSchedules.FirstOrDefaultAsync(s => s.Id == existingId, ct);
            if (schedule is null) return (null, null);
            schedule.Name = name;
            db.BellSchedulePeriods.RemoveRange(await db.BellSchedulePeriods.Where(p => p.ScheduleId == existingId).ToListAsync(ct));
        }
        else
        {
            schedule = new BellSchedule { Id = Guid.NewGuid(), Name = name, CreatedAt = AppClock.NowInstant };
            db.BellSchedules.Add(schedule);
        }
        var scheduleId = schedule.Id;
        db.BellSchedulePeriods.AddRange(periods.Select(p => new BellSchedulePeriod
        {
            ScheduleId = scheduleId, Period = p.Period, StartTime = p.StartTime, EndTime = p.EndTime,
        }));

        // Bitta ega — bitta jadval: boshqa jadvaldagi ega shu jadvalga ko'chadi.
        var rows = await db.BellScheduleOwners
            .Where(o => o.ScheduleId == scheduleId || ownerIds.Contains(o.OwnerId)).ToListAsync(ct);
        foreach (var row in rows)
        {
            if (!ownerIds.Contains(row.OwnerId)) db.BellScheduleOwners.Remove(row);
            else row.ScheduleId = scheduleId;
        }
        var present = rows.Select(r => r.OwnerId).ToHashSet(StringComparer.Ordinal);
        db.BellScheduleOwners.AddRange(ownerIds.Where(o => !present.Contains(o))
            .Select(o => new BellScheduleOwner { OwnerId = o, ScheduleId = scheduleId }));

        await db.SaveChangesAsync(ct);
        return ((await ListAsync(db, ct)).First(s => s.Id == scheduleId), null);
    }

    /// <summary>Jadval o'chiriladi; uning sinf va guruhlari asosiy jadvalga qaytadi.</summary>
    public static async Task<bool> DeleteAsync(IAppDbContext db, Guid id, CancellationToken ct = default) =>
        await db.BellSchedules.Where(s => s.Id == id).ExecuteDeleteAsync(ct) > 0;

    /// <summary>
    /// Darslar ro'yxatini tekshiradi: 1..20 ta, vaqt "HH:mm", boshlanishi tugashidan oldin va har
    /// dars oldingisi tugagach boshlanadi. Raqamlar tartib bo'yicha 1, 2, 3 … qilib qayta beriladi.
    /// </summary>
    public static (List<LessonTimeDto> Periods, string? Error) NormalizePeriods(IReadOnlyList<LessonTimeDto>? input)
    {
        var list = (input ?? []).OrderBy(p => p.Period).ToList();
        if (list.Count == 0) return ([], "Kamida bitta dars vaqtini kiriting");
        if (list.Count > MaxPeriods) return ([], $"Kuniga {MaxPeriods} tadan ortiq dars bo'lmaydi");

        var result = new List<LessonTimeDto>(list.Count);
        TimeOnly? previousEnd = null;
        for (var i = 0; i < list.Count; i++)
        {
            var n = i + 1;
            if (!TryClock(list[i].StartTime, out var start) || !TryClock(list[i].EndTime, out var end))
                return ([], $"{n}-dars: vaqtni SS:DD ko'rinishida kiriting");
            if (end <= start) return ([], $"{n}-dars: tugash vaqti boshlanishidan keyin bo'lsin");
            if (previousEnd is { } pe && start < pe) return ([], $"{n}-dars oldingi dars tugamasdan boshlanyapti");
            previousEnd = end;
            result.Add(new LessonTimeDto(n, start.ToString("HH:mm", CultureInfo.InvariantCulture),
                end.ToString("HH:mm", CultureInfo.InvariantCulture)));
        }
        return (result, null);
    }

    private static bool TryClock(string? s, out TimeOnly t) =>
        TimeOnly.TryParseExact((s ?? "").Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
}
