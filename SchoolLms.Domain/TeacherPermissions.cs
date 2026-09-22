namespace SchoolLms.Domain;

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

    /// <summary>
    /// Seasonal assessment entry — <c>/teacher/seasonal-marks</c>, limited to the
    /// (class, subject) pairs the teacher teaches (admission-and-testing.md §4.2).
    /// Existing teachers receive it from the <c>AdmissionAndExams</c> migration's
    /// back-fill; new teachers get it through <see cref="All"/>.
    /// </summary>
    public const string SeasonalMarks = "seasonalMarks";

    /// <summary>Barcha mavjud bo'lim kalitlari (yangi o'qituvchi uchun standart — hammasi ochiq).</summary>
    public static readonly string[] All = { Journal, Assignments, Schedule, Messages, Salary, SeasonalMarks };
}
