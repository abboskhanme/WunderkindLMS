namespace SchoolLms.Domain;

/// <summary>
/// A teacher's pay addition on top of lessons × category rate (client, 2026-10-15: "Xodimlar bo'limidagi
/// o'qituvchilar ma'lumotlarini ichiga sertifikat va ustama qismlarini qo'shish va buni to'g'ridan to'g'ri
/// moliyaga bog'lash — oylik hisob-kitobi"). Two kinds share one table because the salary reads them the same
/// way: a <b>certificate</b> the teacher holds (IELTS, milliy sertifikat …) with the allowance it earns, and a
/// named <b>ustama</b> (sinf rahbarligi, to'garak …). The old school-wide <see cref="Teacher.BonusPct"/> stays
/// as it was — this is added next to it. docs/modules/teacher-allowances.md.
///
/// <para>Paid for every month from <see cref="FromMonth"/> to <see cref="ToMonth"/> (open when null) that is not
/// after the month <see cref="ExpiresOn"/> falls in. A percentage is taken of the month's lesson pay (lessons
/// actually due × rate, before <see cref="Teacher.BonusPct"/>); a fixed sum is paid whole, but only in a month
/// the teacher has lessons planned (no pay in the summer, like the lessons themselves).</para>
/// </summary>
public class TeacherAllowance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TeacherId { get; set; } = string.Empty;

    /// <summary><see cref="TeacherAllowanceSources"/>: certificate | ustama.</summary>
    public string Source { get; set; } = TeacherAllowanceSources.Ustama;

    /// <summary>"IELTS", "Milliy sertifikat — matematika", "Sinf rahbarligi".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary><see cref="TeacherAllowanceKinds"/>: percent | fixed.</summary>
    public string AmountKind { get; set; } = TeacherAllowanceKinds.Fixed;

    /// <summary>Percent (0–1000) or so'm per month (≥ 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>First month paid, "yyyy-MM".</summary>
    public string FromMonth { get; set; } = string.Empty;

    /// <summary>Last month paid, "yyyy-MM"; null — until changed.</summary>
    public string? ToMonth { get; set; }

    /// <summary>Certificate: level or score as written on it ("C1", "A+", "7.5").</summary>
    public string? Level { get; set; }

    /// <summary>Certificate number.</summary>
    public string? Number { get; set; }

    public DateOnly? IssuedOn { get; set; }

    /// <summary>Certificate expiry — the allowance stops after that month.</summary>
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>Scan of the certificate — a URL from <c>POST /api/admin/uploads</c>.</summary>
    public string? FileUrl { get; set; }

    public string? Note { get; set; }

    /// <summary>users.id of whoever added it (from the token).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Is it paid in <paramref name="month"/> ("yyyy-MM")?</summary>
    public bool ActiveIn(string month) =>
        string.CompareOrdinal(month, FromMonth) >= 0
        && (ToMonth is null || string.CompareOrdinal(month, ToMonth) <= 0)
        && (ExpiresOn is not { } exp || string.CompareOrdinal(month, $"{exp.Year:D4}-{exp.Month:D2}") <= 0);
}

public static class TeacherAllowanceSources
{
    public const string Certificate = "certificate";
    public const string Ustama = "ustama";

    public static readonly IReadOnlyList<string> All = [Certificate, Ustama];
}

public static class TeacherAllowanceKinds
{
    public const string Percent = "percent";
    public const string Fixed = "fixed";

    public static readonly IReadOnlyList<string> All = [Percent, Fixed];
}
