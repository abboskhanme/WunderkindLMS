namespace SchoolLms.Domain;

/// <summary>
/// Kechki dars va yotoqxona davomati (mijoz, 2026-09-23). Kunduzgi dars davomatidan ALOHIDA:
/// u jurnalda (dars-ma-dars) qoladi, bu esa kuniga BIR marta, ikki xil sessiya uchun
/// ikki xil odam oladi.
///
/// <para>Kimdan olinadi — shu kuni sessiya RO'YXATIDA turgan o'quvchilardan
/// (<see cref="BoardingRosterEntry"/>, 2026-10-11 dan; ilgari yotoqxona abonementidan olinardi).</para>
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

    /// <summary>
    /// Aniq sabab (absence_reasons.id), 2026-09-26: "keldi" da — kechikish sababi (IsLate)
    /// yoki null; "kelmadi"/"sababli" da — yo'qlik sababi. Eski yozuvlarda null.
    /// FK yo'q: sabablar katalogi saqlashda qayta yoziladi (id'lar saqlanadi), jurnal kabi.
    /// </summary>
    public string? ReasonId { get; set; }

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

/// <summary>
/// Kechki dars yoki yotoqxona ro'yxatidagi o'quvchi — SANALI a'zolik (mijoz, 2026-10-11: "kechki
/// dars va yotoqxona yo'qlamasini o'quvchilarni qo'lda qo'shadigan qilish, abonomentga qaralmasin").
/// Ikki sessiyaning ro'yxati alohida. Chiqarilgan o'quvchining o'tgan kunlardagi davomati joyida
/// qoladi: o'sha kunlarda u ro'yxatda bo'lgan.
/// </summary>
public class BoardingRosterEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="BoardingSession"/>: evening | dorm.</summary>
    public string Session { get; set; } = BoardingSession.Evening;

    public string StudentId { get; set; } = string.Empty;

    /// <summary>Shu kundan boshlab ro'yxatda.</summary>
    public DateOnly AddedOn { get; set; }

    /// <summary>Shu kundan boshlab ro'yxatda EMAS; null — hozir ham ro'yxatda.</summary>
    public DateOnly? RemovedOn { get; set; }

    /// <summary>Kim qo'shgan (users.id).</summary>
    public string AddedBy { get; set; } = string.Empty;

    /// <summary>Kim chiqargan (users.id).</summary>
    public string? RemovedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
