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

    /// <summary>
    /// Retired 2026-09-28: teachers never see their salary (client) — <c>/api/teacher/salary</c>
    /// answers 403 whatever this key says. Kept only because existing rows still carry it;
    /// no longer granted (<see cref="All"/>) nor offered in the admin form.
    /// </summary>
    public const string Salary = "salary";

    /// <summary>
    /// Seasonal assessment entry — <c>/teacher/seasonal-marks</c>, limited to the
    /// (class, subject) pairs the teacher teaches (admission-and-testing.md §4.2).
    /// Existing teachers receive it from the <c>AdmissionAndExams</c> migration's
    /// back-fill; new teachers get it through <see cref="All"/>.
    /// </summary>
    public const string SeasonalMarks = "seasonalMarks";

    /// <summary>Barcha mavjud bo'lim kalitlari (yangi o'qituvchi uchun standart — hammasi ochiq).</summary>
    public static readonly string[] All = { Journal, Assignments, Schedule, Messages, SeasonalMarks };

    /// <summary>
    /// Admin-section keys a teacher can hold through an access role (Boshqaruv → Rollar): the
    /// evening-lesson and dorm roll calls (client, 2026-10-12: some teachers are also dorm
    /// commandants — one account, no second login). Each key also counts in its ":view" form.
    /// Only these keys reach a teacher's request; a role holding any other key is not given to a teacher.
    /// </summary>
    public static readonly IReadOnlyList<string> RoleKeys = [.. BoardingSession.All.Select(BoardingSession.PermissionOf)];

    /// <summary>True for a <see cref="RoleKeys"/> key, full or ":view".</summary>
    public static bool IsRoleKey(string key) =>
        RoleKeys.Contains(key.EndsWith(Roles.ViewSuffix, StringComparison.Ordinal) ? key[..^Roles.ViewSuffix.Length] : key);

    /// <summary>
    /// A role a teacher can be given: at least one section key, and every section key a <see cref="RoleKeys"/>
    /// one. Page grants (<c>"/admin/…|edit"</c>, the admin menu's entries saved next to the keys by
    /// Boshqaruv → Rollar) give no access on the server and are skipped.
    /// </summary>
    public static bool FitsTeacher(IReadOnlyCollection<string> rolePermissions)
    {
        var sections = rolePermissions.Where(k => !k.StartsWith('/')).ToList();
        return sections.Count > 0 && sections.All(IsRoleKey);
    }
}
