namespace SchoolLms.Server.Models;

/// <summary>
/// O'qituvchi web panelidagi bo'limlar (ruxsatlar) kalitlari. Admin har o'qituvchiga qaysi
/// bo'limlardan foydalanishni belgilaydi (Teacher.Permissions). "Bosh sahifa" har doim ochiq.
/// </summary>
public static class TeacherPermissions
{
    public const string Journal = "journal";
    public const string Assignments = "assignments";
    public const string Schedule = "schedule";
    public const string Messages = "messages";
    public const string Salary = "salary";

    /// <summary>Barcha mavjud bo'lim kalitlari (yangi o'qituvchi uchun standart — hammasi ochiq).</summary>
    public static readonly string[] All = { Journal, Assignments, Schedule, Messages, Salary };
}
