using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;

namespace SchoolLms.Application.Services;

/// <summary>Bitta dars vaqti.</summary>
public readonly record struct BellTime(string StartTime, string EndTime);

/// <summary>
/// Dars vaqtlari kitobi — bir so'rov uchun bir marta o'qiladi (<see cref="BellTimes.LoadAsync"/>):
/// asosiy jadval (<c>lesson_times</c>), qo'shimcha jadvallar va qaysi ega qaysi jadvalda. Vaqt har
/// doim darsning EGASI (sinf yoki o'quv guruhi) bo'yicha olinadi (2026-10-11).
/// </summary>
public sealed class BellBook(
    IReadOnlyDictionary<int, BellTime> defaults,
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<int, BellTime>> schedules,
    IReadOnlyDictionary<string, Guid> owners)
{
    /// <summary>Asosiy jadval (Sozlamalar → Dars vaqtlari).</summary>
    public IReadOnlyDictionary<int, BellTime> Default => defaults;

    /// <summary>Egasining jadvali; biriktirilmagan (yoki noma'lum) ega — asosiy jadval.</summary>
    public IReadOnlyDictionary<int, BellTime> For(string? ownerId) =>
        ownerId is not null && owners.TryGetValue(ownerId, out var id) && schedules.TryGetValue(id, out var s) ? s : defaults;

    /// <summary>Egasining shu darsi vaqti; kiritilmagan bo'lsa null.</summary>
    public BellTime? Time(string? ownerId, int period) =>
        For(ownerId).TryGetValue(period, out var t) && t.StartTime != "" ? t : null;

    /// <summary>Egasi qo'shimcha jadvalga biriktirilganmi.</summary>
    public bool HasOwnSchedule(string ownerId) => owners.ContainsKey(ownerId);
}

/// <summary>Dars vaqtlarini o'qish — yagona joy (fayl: <c>SchoolLms.Domain/BellSchedules.cs</c>).</summary>
public static class BellTimes
{
    public static async Task<BellBook> LoadAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var defaults = (await db.LessonTimes.AsNoTracking().ToListAsync(ct))
            .GroupBy(t => t.Period)
            .ToDictionary(g => g.Key, g => new BellTime(g.First().StartTime, g.First().EndTime));
        var periods = await db.BellSchedulePeriods.AsNoTracking().ToListAsync(ct);
        var schedules = periods.GroupBy(p => p.ScheduleId)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyDictionary<int, BellTime>)g.ToDictionary(p => p.Period, p => new BellTime(p.StartTime, p.EndTime)));
        // Darssiz jadval ham jadval — egasi bo'sh jadvalni ko'radi, asosiyni emas.
        foreach (var id in await db.BellSchedules.AsNoTracking().Select(s => s.Id).ToListAsync(ct))
            schedules.TryAdd(id, new Dictionary<int, BellTime>());
        var owners = await db.BellScheduleOwners.AsNoTracking()
            .ToDictionaryAsync(o => o.OwnerId, o => o.ScheduleId, StringComparer.Ordinal, ct);
        return new BellBook(defaults, schedules, owners);
    }
}
