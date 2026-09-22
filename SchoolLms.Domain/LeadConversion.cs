namespace SchoolLms.Domain;

/// <summary>
/// Lid o'quvchiga aylangani haqidagi STATISTIK yozuv (<c>lead_conversions</c>).
///
/// <para>
/// Mijoz qarori (2026-09-22): o'quvchiga aylangan lid lidlar bazasidan O'CHIRILADI —
/// uning ma'lumotlari endi o'quvchi kartochkasida, lid sifatida saqlash ortiqcha.
/// Lekin lidlarning UMUMIY SONI saqlanishi kerak. Shuning uchun bu yerda shaxsiy
/// ma'lumot ham, bosqich (holat) ham YO'Q: faqat QACHON va qaysi MANBADAN — voronka
/// "jami lidlar" va "o'quvchiga aylandi" ni shundan sanaydi.
/// </para>
/// </summary>
public class LeadConversion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="LeadSource"/>: manual | survey — voronkaning manba kesimi uchun.</summary>
    public string Source { get; set; } = LeadSource.Manual;

    /// <summary>Ariza formasidan kelgan bo'lsa — qaysi ariza. Ariza o'chirilsa <c>NULL</c>.</summary>
    public Guid? SurveyId { get; set; }

    /// <summary>Qachon aylantirildi.</summary>
    public DateTimeOffset ConvertedAt { get; set; } = AppClock.NowInstant;
}
