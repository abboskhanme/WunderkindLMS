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

    /// <summary>
    /// Hozirgi LAHZA (instant) — moliyadagi <c>timestamptz</c> ustunlari uchun.
    ///
    /// <para>
    /// <b>Nega UTC, UTC+5 emas?</b> PostgreSQL <c>timestamptz</c> ofsetni
    /// SAQLAMAYDI — u lahzani saqlaydi va o'qishda sessiya mintaqasiga
    /// o'giradi. Shuning uchun Npgsql <c>DateTimeOffset</c> ni faqat ofseti
    /// nol bo'lganda qabul qiladi; UTC+5 bilan yozishga urinish
    /// <c>"only offset 0 (UTC) is supported"</c> xatosini beradi (P1-07 da
    /// amalda uchradi). Ya'ni bu "mintaqani yo'qotish" emas — aksincha,
    /// lahzani BIR MA'NOLI saqlash.
    /// </para>
    /// <para>
    /// Ko'rsatishda vaqt yana Toshkentga o'giriladi: <see cref="ToLocal"/>
    /// yoki frontend formatlash orqali.
    /// </para>
    /// <para>
    /// Eski entity'lar esa <see cref="Now"/> ni (ofsetsiz "devor soati")
    /// <c>timestamp without time zone</c> ga yozadi. Moliyada bu yaramaydi:
    /// smena chegarasi va <c>received_at</c> lahzasi aniq bo'lmasa,
    /// Z-hisobotni keyin qayta hisoblab bo'lmaydi (SPEC §7).
    /// </para>
    /// </summary>
    public static DateTimeOffset NowInstant => DateTimeOffset.UtcNow;

    /// <summary>
    /// <c>timestamptz</c> dan o'qilgan lahzani maktab mintaqasidagi vaqtga
    /// o'giradi (ko'rsatish va kunlik guruhlash uchun).
    /// </summary>
    public static DateTime ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTimeFromUtc(instant.UtcDateTime, Tz);

    /// <summary>Lahza maktab mintaqasida qaysi kunga tushadi (Z-hisobot, kunlik kesim).</summary>
    public static DateOnly LocalDateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(ToLocal(instant));

    /// <summary>
    /// Berilgan MAKTAB SANASIGA tushadigan lahza — bugungi soat-daqiqa bilan
    /// (masalan 14:30 da yozilgan uch kun oldingi kirim → o'sha kunning
    /// 14:30 i). Kassa kirimini va to'lovni orqadagi sana bilan yozish uchun
    /// (mijoz, 2026-09-18: "oldingi sana uchun tanlash mumkin bo'lsin").
    ///
    /// <para>
    /// Butun KUN ayiriladi, chunki Toshkent mintaqasida yozgi vaqt yo'q
    /// (ofset doim +5) — shuning uchun lahzadan N kun ayirish mahalliy
    /// sanani aynan N kunga suradi va soatni o'zgartirmaydi. Natija
    /// <see cref="LocalDateOf"/> dan o'tkazilganda <paramref name="localDate"/>
    /// ni qaytaradi — kunlik kesim va jurnal sanasi shunga tayanadi.
    /// </para>
    /// </summary>
    public static DateTimeOffset InstantOn(DateOnly localDate) =>
        NowInstant.AddDays(localDate.DayNumber - Today.DayNumber);

    /// <summary>"yyyy-MM-ddTHH:mm:ss" — saqlash/ko'rsatish uchun standart ISO satr (mintaqa: UTC+5).</summary>
    public static string Iso() => Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
