namespace SchoolLms.Domain;

/// <summary>
/// Kechki dars va yotoqxona davomati (mijoz, 2026-09-23). Kunduzgi dars davomatidan ALOHIDA:
/// u jurnalda (dars-ma-dars) qoladi, bu esa kuniga BIR marta, ikki xil sessiya uchun
/// ikki xil odam oladi.
///
/// <para>Kimdan olinadi — shu kuni FAOL <b>yotoqxona</b> abonementi bor o'quvchilardan
/// (<c>fee_categories.code = 'dormitory'</c>). Abonementi yo'q o'quvchi ro'yxatda kulrang
/// ko'rinadi, belgilanmaydi va hech bir hisobga kirmaydi — bu jadvalda uning qatori ham
/// bo'lmaydi.</para>
/// </summary>
public class BoardingAttendance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly Date { get; set; }

    /// <summary><see cref="BoardingSession"/>: evening | dorm.</summary>
    public string Session { get; set; } = BoardingSession.Evening;

    public string StudentId { get; set; } = string.Empty;

    /// <summary><see cref="BoardingStatus"/>: present | absent | excused.</summary>
    public string Status { get; set; } = BoardingStatus.Present;

    /// <summary>Kim belgilagan (users.id).</summary>
    public string MarkedBy { get; set; } = string.Empty;

    public DateTimeOffset MarkedAt { get; set; }

    /// <summary>"Kelmadi/Yo'q" uchun ota-onaga Telegram xabar qachon ketgan. Bir holat
    /// uchun bir marta — qayta saqlash xabarni takrorlamaydi.</summary>
    public DateTimeOffset? NotifiedAt { get; set; }
}

/// <summary>Sessiyalar — har biri kuniga bir marta.</summary>
public static class BoardingSession
{
    /// <summary>Kechki dars (yo'nalish guruhi bo'yicha umumiy davomat).</summary>
    public const string Evening = "evening";
    /// <summary>Yotoqxona (kechki tekshiruv).</summary>
    public const string Dorm = "dorm";

    public static readonly IReadOnlyList<string> All = [Evening, Dorm];

    /// <summary>Sessiyani belgilash ruxsati (xodim uchun). Admin/superadmin — har doim.</summary>
    public static string PermissionOf(string session) =>
        session == Dorm ? "attendanceDorm" : "attendanceEvening";
}

/// <summary>Holatlar. Kechki darsda: keldi / kelmadi / sababli; yotoqxonada: joyida / yo'q /
/// ruxsat bilan — ma'nosi bir xil, ekrandagi yorliq sessiyaga qarab.</summary>
public static class BoardingStatus
{
    public const string Present = "present";
    public const string Absent = "absent";
    public const string Excused = "excused";

    public static readonly IReadOnlyList<string> All = [Present, Absent, Excused];
}
