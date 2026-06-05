using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'qituvchi oylik maoshini DARS JADVALI + DAVOMATdan hisoblaydi.
/// Reja (nominal) oylik = haftalik darslar × <see cref="WeeksPerMonth"/> × toifaning bir soat narxi.
/// Davomatga moslangan oylik = shu oyda o'qituvchi KELMAGAN (absent) kunlardagi darslar chegirilgan.
/// Haftalik darslar — har sinfning asosiy jadval shabloni (faqat mavjud, arxivlanmagan sinflar).
/// </summary>
public static class TeacherSalaryCalc
{
    /// <summary>Oyiga o'rtacha hafta soni (haftalik darslarni oylikka aylantirish uchun).</summary>
    public const int WeeksPerMonth = 4;

    /// <summary>
    /// Har o'qituvchining hafta kuni kesimida darslar soni: teacherId → int[6]
    /// (indeks 0=Dushanba ... 5=Shanba; <see cref="ScheduleLesson.Day"/> bilan bir xil).
    /// Har sinf uchun faqat bitta asosiy (eng ko'p darsli) shablon, faqat mavjud sinflar.
    /// </summary>
    public static async Task<Dictionary<string, int[]>> LessonsByWeekdayAsync(IAppDbContext db)
    {
        var classSet = (await db.Classes.Where(c => !c.IsArchived).Select(c => c.Id).ToListAsync())
            .ToHashSet();
        var templates = (await db.ScheduleTemplates.Include(t => t.Lessons).ToListAsync())
            .Where(t => classSet.Contains(t.ClassId)).ToList();
        var mainPerClass = templates
            .GroupBy(t => t.ClassId)
            .Select(g => g.OrderByDescending(t => t.Lessons.Count).ThenBy(t => t.Id).First());

        var result = new Dictionary<string, int[]>();
        foreach (var tpl in mainPerClass)
            foreach (var l in tpl.Lessons.Where(l => !string.IsNullOrEmpty(l.TeacherId) && l.Day is >= 0 and < 6))
            {
                if (!result.TryGetValue(l.TeacherId, out var arr)) result[l.TeacherId] = arr = new int[6];
                arr[l.Day]++;
            }
        return result;
    }

    /// <summary>Har o'qituvchining haftalik darslar soni (teacherId → son).</summary>
    public static async Task<Dictionary<string, int>> WeeklyLessonsAsync(IAppDbContext db) =>
        (await LessonsByWeekdayAsync(db)).ToDictionary(kv => kv.Key, kv => kv.Value.Sum());

    /// <summary>Toifa kaliti bo'yicha bir soat dars narxi (SchoolMeta'dan).</summary>
    public static decimal RateFor(SchoolMeta? meta, string? category)
    {
        if (meta is null) return 0m;
        return category switch
        {
            "oliy" => meta.SalaryRateOliy,
            "1" => meta.SalaryRate1,
            "2" => meta.SalaryRate2,
            "mutaxasis" => meta.SalaryRateMutaxasis,
            _ => 0m,
        };
    }

    /// <summary>Reja (nominal) oylik = haftalik darslar × 4 × toifa narxi (davomatsiz).</summary>
    public static decimal Monthly(int weeklyLessons, string? category, SchoolMeta? meta) =>
        weeklyLessons * WeeksPerMonth * RateFor(meta, category);

    /// <summary>Choraklar (dars jadvali davrlari) — har biri (StartDate, EndDate) "yyyy-MM-dd".</summary>
    public static async Task<List<(string Start, string End)>> QuarterRangesAsync(IAppDbContext db) =>
        (await db.Quarters.Where(q => q.StartDate.Length >= 10 && q.EndDate.Length >= 10)
            .Select(q => new { q.StartDate, q.EndDate }).ToListAsync())
        .Select(q => (q.StartDate, q.EndDate)).ToList();

    /// <summary>Sana biror chorak (dars jadvali davri) ichidami. Choraklar bo'sh = cheklov yo'q (true).</summary>
    public static bool InQuarter(string date, IReadOnlyList<(string Start, string End)>? quarters)
    {
        if (quarters is null || quarters.Count == 0) return true;
        foreach (var q in quarters)
            if (string.CompareOrdinal(date, q.Start) >= 0 && string.CompareOrdinal(date, q.End) <= 0)
                return true;
        return false;
    }

    /// <summary>Kelmagan (absent) kunlardagi darslar soni — faqat chorak davridagi kunlar.</summary>
    public static int MissedLessons(int[] byWeekday, IEnumerable<string> absentDates,
        IReadOnlyList<(string Start, string End)>? quarters = null)
    {
        var missed = 0;
        foreach (var d in absentDates)
            if (DateOnly.TryParse(d, out var date))
            {
                var wd = ((int)date.DayOfWeek + 6) % 7; // Dushanba=0 ... Shanba=5, Yakshanba=6
                if (wd < 6 && InQuarter(d, quarters)) missed += byWeekday[wd];
            }
        return missed;
    }

    /// <summary>Ustama (foiz) qo'shilgan summa: salary × (1 + bonus%/100).</summary>
    public static decimal WithBonus(decimal salary, decimal bonusPct) => salary * (1 + bonusPct / 100m);

    /// <summary>O'qituvchining maosh boshlanish sanasi ("YYYY-MM-DD") — yangi maydon, bo'lmasa eski oydan.</summary>
    public static string? StartDateOf(Teacher t) =>
        !string.IsNullOrEmpty(t.SalaryStartDate) ? t.SalaryStartDate
        : !string.IsNullOrEmpty(t.SalaryStartMonth) ? $"{t.SalaryStartMonth}-01"
        : null;

    /// <summary>[from..to] oralig'idagi darslar soni — yakshanbasiz va faqat CHORAK (dars jadvali) davridagi kunlar.</summary>
    public static int LessonsInRange(int[] byWeekday, DateOnly from, DateOnly to,
        IReadOnlyList<(string Start, string End)>? quarters = null)
    {
        var total = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var wd = ((int)d.DayOfWeek + 6) % 7; // Dushanba=0..Shanba=5, Yakshanba=6
            if (wd >= 6) continue; // yakshanba
            // Faqat chorak (dars jadvali) davridagi kunlar — undan tashqari oy/kunlarga maosh yo'q.
            if (!InQuarter($"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}", quarters)) continue;
            total += byWeekday[wd];
        }
        return total;
    }

    /// <summary>
    /// Bir oydagi REJA darslar soni — o'sha oyda hafta kunlari necha marta CHORAK ichida kelishiga qarab.
    /// Chorak (dars jadvali) bo'lmagan oylarda 0. Ishga kirgan oy — kelgan kunidan; kirgan oydan oldin 0.
    /// </summary>
    public static int PlannedLessonsForMonth(int[] byWeekday, string month, string? startDate,
        IReadOnlyList<(string Start, string End)>? quarters = null)
    {
        if (month.Length < 7) return 0;
        var y = int.Parse(month[..4]);
        var mo = int.Parse(month.Substring(5, 2));
        var monthEnd = new DateOnly(y, mo, DateTime.DaysInMonth(y, mo));
        var from = new DateOnly(y, mo, 1);
        if (startDate is { Length: >= 7 })
        {
            var startMonth = startDate[..7];
            if (string.CompareOrdinal(month, startMonth) < 0) return 0; // hali ishga kirmagan
            if (month == startMonth && startDate.Length >= 10 && DateOnly.TryParse(startDate, out var sd))
                from = sd; // qisman: kelgan kunidan oy oxirigacha
        }
        return LessonsInRange(byWeekday, from, monthEnd, quarters);
    }

    /// <summary>
    /// Bitta oy uchun yakuniy maosh (so'm): chorak davri + qisman birinchi oy + davomat + ustama bilan.
    /// </summary>
    public static decimal MonthlyForMonth(
        int[] byWeekday, string? category, SchoolMeta? meta, string month, string? startDate,
        IEnumerable<string> absentDatesInMonth, decimal bonusPct,
        IReadOnlyList<(string Start, string End)>? quarters = null)
    {
        var planned = PlannedLessonsForMonth(byWeekday, month, startDate, quarters);
        // Ishga kirgan oyda — faqat kelgan kunidan keyingi yo'qliklar hisobga olinadi.
        var absences = startDate is { Length: >= 10 } && startDate[..7] == month
            ? absentDatesInMonth.Where(d => string.CompareOrdinal(d, startDate) >= 0)
            : absentDatesInMonth;
        var net = Math.Max(0, planned - MissedLessons(byWeekday, absences, quarters));
        return WithBonus(net * RateFor(meta, category), bonusPct);
    }

    /// <summary>Bitta o'qituvchining REJA (nominal) oyligi — davomat hisobga olinmaydi.</summary>
    public static async Task<decimal> MonthlyAsync(IAppDbContext db, Teacher t)
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var weekly = (await WeeklyLessonsAsync(db)).GetValueOrDefault(t.Id);
        return Monthly(weekly, t.Category, meta);
    }
}
