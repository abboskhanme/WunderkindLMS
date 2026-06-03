namespace SchoolLms.Domain;

/// <summary>
/// Butun platforma uchun yagona "soat". Server qayerda (Windows, Linux, Docker/UTC)
/// turishidan qat'i nazar vaqtni doim maktab mintaqasida — Asia/Tashkent (UTC+5,
/// yozgi vaqt yo'q) — qaytaradi.
///
/// Nega kerak: ilgari kodda <c>DateTime.Now</c> (server lokal vaqti) va
/// <c>DateTime.UtcNow</c> aralash ishlatilgan edi. Docker runtime'i UTC'da yuradi,
/// shuning uchun saqlangan vaqtlar mintaqa belgisisiz (Z/ofsetsiz) chiqib, frontend
/// ularni 5 soat orqada ko'rsatardi. Endi hamma joyda <see cref="Now"/> ishlatiladi —
/// qiymat ham, ko'rsatiladigan satr ham doim O'zbekiston vaqtida bo'ladi.
/// </summary>
public static class AppClock
{
    private static readonly TimeZoneInfo Tz = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // Linux/macOS (tzdata) — "Asia/Tashkent"; Windows — "West Asia Standard Time"
        // (UTC+05:00 Ashgabat, Tashkent).
        foreach (var id in new[] { "Asia/Tashkent", "West Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // tzdata topilmasa — qattiq +5 ofset (zaxira).
        return TimeZoneInfo.CreateCustomTimeZone("UZT+5", TimeSpan.FromHours(5), "UZT", "UZT");
    }

    /// <summary>Maktab mintaqasidagi hozirgi vaqt (UTC+5).</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

    /// <summary>Maktab mintaqasidagi bugungi sana.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>"yyyy-MM-ddTHH:mm:ss" — saqlash/ko'rsatish uchun standart ISO satr (mintaqa: UTC+5).</summary>
    public static string Iso() => Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
