using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Copies one week's canteen menu onto one or more following weeks (client, 2026-10-16:
/// "Oshxona menyusini haftalik ko'chirish imkonini qo'shish"). See docs/modules/canteen.md.
///
/// <para>
/// <b>A week is Monday..Sunday.</b> The Mini App shows parents all seven days, so a Sunday menu
/// travels with its week. Only the weekdays the source week actually fills are written; a target
/// day whose source day is empty is left exactly as it is, with or without overwrite.
/// </para>
/// <para>
/// <b>Conflicts.</b> A target day that would be written and already has dishes is a conflict.
/// Without overwrite nothing at all is written and the caller gets every such date; with it,
/// those days' dishes are removed and replaced. Removal and insertion go through one
/// <c>SaveChanges</c>, i.e. one transaction — a failed copy never leaves a half-copied week.
/// </para>
/// <para>
/// <b>Skipped dishes.</b> <see cref="Dish"/> has no active/archived flag — a dish is a free-text
/// row on one date, not a catalogue entry. "Skipped" therefore means a row the menu can never
/// show: a meal key outside <see cref="CanteenMenu.Meals"/>, a blank name or an unreadable date.
/// </para>
/// </summary>
public static class CanteenWeekCopy
{
    /// <summary>Upper bound for "copy to several weeks" — two months ahead is plenty for a canteen.</summary>
    public const int MaxWeeks = 8;

    private const int DaysPerWeek = 7;

    /// <summary>Index 0 = Monday, matching the day offset inside a week.</summary>
    private static readonly string[] WeekdayNames =
        ["Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba", "Yakshanba"];

    public enum Status { Ok, Invalid, Conflict }

    /// <param name="Message">Uzbek text for the admin — set for <see cref="Status.Invalid"/> and <see cref="Status.Conflict"/>.</param>
    /// <param name="ConflictDates">Filled target dates (ISO, ascending) — set only for <see cref="Status.Conflict"/>.</param>
    public sealed record Outcome(
        Status Status,
        string? Message = null,
        CanteenCopyWeekResultDto? Result = null,
        IReadOnlyList<string>? ConflictDates = null);

    public static async Task<Outcome> RunAsync(
        IAppDbContext db, CanteenCopyWeekPayload p, CancellationToken ct = default)
    {
        if (!TryParseMonday(p.FromWeekStart, out var from, out var error)
            || !TryParseMonday(p.ToWeekStart, out var to, out error))
            return new Outcome(Status.Invalid, error);
        if (p.Weeks is < 1 or > MaxWeeks)
            return new Outcome(Status.Invalid, $"Haftalar soni 1 dan {MaxWeeks} gacha bo'lishi kerak.");

        var targetWeeks = Enumerable.Range(0, p.Weeks).Select(i => to.AddDays(i * DaysPerWeek)).ToList();
        if (targetWeeks.Contains(from))
            return new Outcome(Status.Invalid, "Hafta o'zining ustiga ko'chirilmaydi — boshqa haftani tanlang.");

        var sourceStart = Iso(from);
        var sourceEnd = Iso(from.AddDays(DaysPerWeek - 1));
        var source = await db.Dishes.AsNoTracking()
            .Where(d => string.Compare(d.Date, sourceStart) >= 0 && string.Compare(d.Date, sourceEnd) <= 0)
            .ToListAsync(ct);

        // Day offset inside the week (0 = Monday) → the dishes to copy onto that weekday.
        var byOffset = new SortedDictionary<int, List<Dish>>();
        var skipped = 0;
        foreach (var dish in source)
        {
            if (!TryOffset(from, dish, out var offset)) { skipped++; continue; }
            if (!byOffset.TryGetValue(offset, out var list)) byOffset[offset] = list = [];
            list.Add(dish);
        }
        if (byOffset.Count == 0)
            return new Outcome(Status.Invalid, "Tanlangan haftada ko'chiriladigan taom yo'q.");

        var targetDates = targetWeeks
            .SelectMany(week => byOffset.Keys.Select(offset => Iso(week.AddDays(offset))))
            .ToList();
        var occupied = await db.Dishes
            .Where(d => targetDates.Contains(d.Date))
            .ToListAsync(ct);

        if (occupied.Count > 0 && !p.Overwrite)
        {
            var filled = occupied.Select(d => d.Date).Distinct().Order(StringComparer.Ordinal).ToList();
            return new Outcome(
                Status.Conflict,
                $"Bu kunlarda menyu allaqachon bor: {string.Join(", ", filled.Select(DayLabel))}. " +
                "Ularni almashtirish uchun «Ustiga yozish»ni yoqing.",
                ConflictDates: filled);
        }

        db.Dishes.RemoveRange(occupied);
        var copied = 0;
        foreach (var week in targetWeeks)
            foreach (var (offset, dishes) in byOffset)
            {
                var date = Iso(week.AddDays(offset));
                foreach (var d in dishes)
                {
                    db.Dishes.Add(new Dish
                    {
                        Date = date,
                        Meal = d.Meal,
                        Name = d.Name,
                        Ingredients = d.Ingredients,
                        // The same /uploads/... file is shared: deleting a dish never deletes its image.
                        ImageUrl = d.ImageUrl,
                    });
                    copied++;
                }
            }
        await db.SaveChangesAsync(ct);

        return new Outcome(Status.Ok, Result: new CanteenCopyWeekResultDto(
            copied, skipped, occupied.Count,
            targetWeeks.Select(Iso).ToList(),
            targetDates));
    }

    private static bool TryParseMonday(string? iso, out DateOnly date, out string? error)
    {
        error = null;
        if (!DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            error = $"Sana noto'g'ri: \"{iso}\" — yyyy-MM-dd ko'rinishida bo'lsin.";
            return false;
        }
        if (date.DayOfWeek != DayOfWeek.Monday)
        {
            error = $"Hafta dushanbadan boshlanadi: {date:dd.MM.yyyy} — {WeekdayNames[OffsetOf(date)]}.";
            return false;
        }
        return true;
    }

    /// <summary>Weekday offset of a source dish, or false when the dish cannot be shown on the menu.</summary>
    private static bool TryOffset(DateOnly monday, Dish dish, out int offset)
    {
        offset = -1;
        if (!CanteenMenu.Meals.Contains(dish.Meal) || string.IsNullOrWhiteSpace(dish.Name)) return false;
        if (!DateOnly.TryParseExact(dish.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        offset = date.DayNumber - monday.DayNumber;
        return offset is >= 0 and < DaysPerWeek;
    }

    private static int OffsetOf(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"Dushanba 20.10" — the weekday plus the short date the canteen page shows.</summary>
    private static string DayLabel(string iso)
    {
        var d = DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{WeekdayNames[OffsetOf(d)]} {d:dd.MM}";
    }
}
