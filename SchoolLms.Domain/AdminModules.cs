namespace SchoolLms.Domain;

/// <summary>
/// Maktab admin paneli bo'limlari (modullari) — obuna/litsenziya va xodim ruxsatlari uchun
/// yagona manba. Kalitlar frontend <c>adminPermissions</c> hamda nav <c>perm</c> kalitlariga
/// MOS kelishi shart. Loyiha boshlig'i (Control Plane) maktabga shu kalitlardan qaysilarini
/// ochishini belgilaydi (<see cref="Platform.Tenant.EnabledModules"/>).
/// </summary>
public static class AdminModules
{
    public readonly record struct Module(string Key, string Label);

    public static readonly IReadOnlyList<Module> All = new[]
    {
        new Module("leads", "Lidlar"),
        new Module("students", "O'quvchilar"),
        new Module("teachers", "O'qituvchilar"),
        new Module("attendance", "Davomat"),
        new Module("schedule", "Dars jadvali"),
        new Module("classes", "Sinflar"),
        new Module("journal", "Jurnal"),
        new Module("messages", "Xabarlar"),
        new Module("app", "Ilova"),
        new Module("gradesReport", "Baholar hisoboti"),
        new Module("teacherReports", "O'qituvchilar hisoboti"),
        new Module("contracts", "Shartnomalar"),
        new Module("finance", "Moliya"),
        new Module("academicYear", "Yangi o'quv yili"),
        new Module("settings", "Sozlamalar"),
        new Module("staff", "Xodimlar"),
        new Module("feedback", "Taklif va shikoyatlar"),
        new Module("discipline", "Intizomiy ball"),
    };

    public static readonly IReadOnlySet<string> Keys =
        new HashSet<string>(All.Select(m => m.Key), StringComparer.Ordinal);

    public static bool IsValid(string key) => Keys.Contains(key);

    /// <summary>Berilgan ro'yxatdan faqat haqiqiy modul kalitlarini qaytaradi (tartibni saqlab).</summary>
    public static List<string> Sanitize(IEnumerable<string>? keys) =>
        (keys ?? []).Where(IsValid).Distinct().ToList();
}
