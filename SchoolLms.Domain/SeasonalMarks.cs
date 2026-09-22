namespace SchoolLms.Domain;

// ===========================================================================
//  Seasonal assessment — EduSchool "Mavsumiy baholash" (monthly / quarterly /
//  yearly subject score, 0–100). Spec: docs/modules/admission-and-testing.md
//  §2.4, §5.12. Unit: A1. Mapping: SchoolLms.Infrastructure/Data/ExamModel.cs.
// ===========================================================================
//
//  NEW STORAGE, BUT ONLY THIS ONE TABLE (§2.4)
//  -------------------------------------------
//  Nothing we have holds a per-period 0–100 score: `JournalEntry.Grade` is 1–5
//  per lesson, `QuarterGrade` is 2–5, `EvaluationGrade` is the 1–5 feedback
//  instrument. The coverage report and the by-subjects pivot are reports over
//  this table — no second table for them. Edit history goes to `AuditLog`
//  (`EntityType = "SeasonalMark"`), not to a column.
//
//  `Quarter` IS AN INT, NOT A FK TO `quarters` (§5.12)
//  ----------------------------------------------------
//  `PUT /api/admin/settings/quarters` deletes and re-creates every quarter row
//  with fresh GUIDs; a FK would either orphan every quarterly mark or make that
//  screen throw. `JournalEntry`, `QuarterGrade` and `WeekAssignment` all carry
//  the quarter as an int too.

/// <summary><see cref="SeasonalMark.PeriodKind"/> values. Closed list (<c>ck_seasonal_marks_period_kind</c>).</summary>
public static class SeasonalPeriodKind
{
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";

    public static readonly IReadOnlyList<string> All = [Monthly, Quarterly, Yearly];
}

/// <summary>
/// One pupil's seasonal score (0–100) and/or comment in one subject for one
/// period. Unique per (student, subject, period) through the generated
/// <see cref="PeriodKey"/> (<c>ux_seasonal_marks_student_subject_period</c>).
/// </summary>
public class SeasonalMark
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="Student.Id"/>; <c>on delete cascade</c>.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>The pupil's class at entry time (a snapshot); <c>on delete restrict</c>.</summary>
    public string ClassId { get; set; } = string.Empty;

    /// <summary><see cref="Subject.Id"/>; <c>on delete restrict</c>.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary><see cref="SeasonalPeriodKind"/>.</summary>
    public string PeriodKind { get; set; } = SeasonalPeriodKind.Monthly;

    /// <summary>Calendar year, 2000–2100.</summary>
    public int Year { get; set; }

    /// <summary>1–12, set only when <see cref="PeriodKind"/> is <c>monthly</c>.</summary>
    public int? Month { get; set; }

    /// <summary>1–4, set only when <see cref="PeriodKind"/> is <c>quarterly</c>. A number, not a FK.</summary>
    public int? Quarter { get; set; }

    /// <summary>
    /// <c>M:2026-03</c> | <c>Q:2026-2</c> | <c>Y:2026</c> — a <b>generated stored column</b>
    /// computed by PostgreSQL from the four columns above; the application never
    /// writes it. It exists because a plain unique index over
    /// (kind, year, month, quarter) would accept two yearly marks (NULLs are
    /// distinct). To look a mark up before it is saved, build the same key with
    /// <see cref="BuildPeriodKey"/>.
    /// </summary>
    public string PeriodKey { get; private set; } = string.Empty;

    /// <summary>0–100, <c>numeric(5,2)</c>, rounded half-up and clamped by the service. NULL = comment only.</summary>
    public decimal? Score { get; set; }

    /// <summary>NULL or at least 3 characters after trim.</summary>
    public string? Comment { get; set; }

    /// <summary><see cref="AppUser.Id"/>; <c>on delete set null</c>.</summary>
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = AppClock.Now;

    public DateTime UpdatedAt { get; set; } = AppClock.Now;

    /// <summary>
    /// The C# twin of the <c>period_key</c> generation expression in
    /// <c>ExamModel.cs</c> — used by the bulk upsert to find an existing mark.
    /// The two must stay identical; <c>ExamMigrationTests</c> compares them
    /// against the database for all three kinds.
    /// </summary>
    public static string BuildPeriodKey(string periodKind, int year, int? month, int? quarter) => periodKind switch
    {
        SeasonalPeriodKind.Monthly => $"M:{year}-{month:00}",
        SeasonalPeriodKind.Quarterly => $"Q:{year}-{quarter}",
        _ => $"Y:{year}",
    };
}
