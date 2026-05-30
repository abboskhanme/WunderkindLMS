using System.Globalization;

namespace SchoolLms.Server.Services;

/// <summary>
/// Frontend dagi lib/weeks.ts mantig'ining C# nusxasi.
/// Sanalar "YYYY-MM-DD" (ISO) string ko'rinishida.
/// </summary>
public static class ScheduleMath
{
    public record WeekRange(int Week, string StartISO, string EndISO);

    private static DateOnly Parse(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ToISO(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string AddDaysISO(string iso, int n) => ToISO(Parse(iso).AddDays(n));

    /// <summary>0=Dushanba ... 6=Yakshanba</summary>
    private static int LessonDow(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    /// <summary>Berilgan sana joylashgan haftaning dushanbasi (ISO).</summary>
    public static string MondayOfISO(string iso)
    {
        var d = Parse(iso);
        return ToISO(d.AddDays(-LessonDow(d)));
    }

    /// <summary>
    /// Chorak oralig'ini dushanbadan boshlanuvchi haftalarga bo'lish. Hafta diapazoni chorak
    /// chegaralariga QISILADI — chorakdan tashqaridagi kunlar jadvalga kirmaydi (masalan chorak
    /// 25-mayda tugasa, oxirgi hafta 26–30-mayni o'z ichiga olmaydi). StartISO endi har doim ham
    /// dushanba emas; haftadagi dars sanasini hisoblashda MondayOfISO(StartISO) dan foydalaning.
    /// </summary>
    public static List<WeekRange> GetQuarterWeeks(string startISO, string endISO)
    {
        var result = new List<WeekRange>();
        if (!DateOnly.TryParseExact(startISO, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(endISO, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            start > end)
        {
            return result;
        }

        // start sanasi joylashgan haftaning dushanbasi.
        // EDGE: yakshanba kalendar hafta oxiri — shu haftaning dushanbasi (6 kun oldin) chorakdan
        // butunlay tashqarida bo'ladi va 1-hafta yo'qoladi. Shu sababli chorak yakshanba'da
        // boshlansa, KEYINGI dushanbani olamiz (haqiqiy birinchi o'quv hafta = 1-hafta).
        var dow = LessonDow(start);
        var firstMonday = dow == 6 ? start.AddDays(1) : start.AddDays(-dow);
        var cursor = firstMonday;
        var week = 1;
        while (cursor <= end)
        {
            var ws = cursor;
            var we = cursor.AddDays(5); // Shanba
            // Chorak chegarasiga qisamiz.
            var clampedStart = ws < start ? start : ws;
            var clampedEnd = we > end ? end : we;
            // Chorak ichida hech bo'lmaganda bitta o'quv kuni bo'lgan haftanigina qo'shamiz.
            if (clampedStart <= clampedEnd)
                result.Add(new WeekRange(week, ToISO(clampedStart), ToISO(clampedEnd)));
            cursor = cursor.AddDays(7);
            week++;
        }
        return result;
    }
}
