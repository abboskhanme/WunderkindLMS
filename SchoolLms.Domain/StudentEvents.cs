namespace SchoolLms.Domain;

// ===========================================================================
//  Student flow — who came, who left and why (docs/modules/student-flow.md, 2026-10-15).
//
//  One row per arrival, departure or return of a pupil. The pupil's own row only says how
//  things are NOW (archived or not, the last archive reason); this table says WHEN each
//  change happened, in which class and why — the report and the dashboard widget count it.
//
//  No foreign key to `students`: a pupil deleted for good keeps their trace here (name and
//  class are snapshots taken at the moment).
// ===========================================================================

/// <summary>One arrival, departure or return of a pupil.</summary>
public class StudentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>students.id — deliberately no FK (a deleted pupil keeps the trace).</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Full name at the moment of the event.</summary>
    public string StudentName { get; set; } = string.Empty;

    /// <summary><see cref="StudentEventKind"/>: enrolled | left | returned.</summary>
    public string Kind { get; set; } = StudentEventKind.Enrolled;

    /// <summary>School-calendar day of the event.</summary>
    public DateOnly OnDate { get; set; }

    /// <summary>classes.id at that moment (null — no class, or the class is unknown).</summary>
    public string? ClassId { get; set; }

    /// <summary>Class name at that moment (snapshot; survives a rename or a deleted class).</summary>
    public string? ClassName { get; set; }

    /// <summary>Leave reason from the catalog (<see cref="StudentArchiveReason"/>); left events only.</summary>
    public Guid? ReasonId { get; set; }

    /// <summary>Free-text detail of the reason (the archive dialog's note).</summary>
    public string? ReasonText { get; set; }

    /// <summary><see cref="StudentEventSource"/> — which path wrote the row.</summary>
    public string Source { get; set; } = StudentEventSource.Manual;

    /// <summary>users.id of who did it (null — system or backfill without a trace).</summary>
    public string? ActorId { get; set; }

    public string? ActorName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Stored event kinds. "Activated" is not stored — it is the first payment, read from payments.</summary>
public static class StudentEventKind
{
    public const string Enrolled = "enrolled";
    public const string Left = "left";
    public const string Returned = "returned";

    public static readonly IReadOnlyList<string> All = [Enrolled, Left, Returned];
}

/// <summary>The path that wrote a <see cref="StudentEvent"/>.</summary>
public static class StudentEventSource
{
    /// <summary>One pupil by hand: the form, the archive dialog, restore.</summary>
    public const string Manual = "manual";
    /// <summary>Several pupils archived at once.</summary>
    public const string Bulk = "bulk";
    /// <summary>A class archived (or brought back) with its pupils.</summary>
    public const string ClassArchive = "class_archive";
    /// <summary>Year rollover — the last grade graduates.</summary>
    public const string Graduation = "graduation";
    /// <summary>Pupil deleted for good (single or several).</summary>
    public const string Delete = "delete";
    /// <summary>Excel import.</summary>
    public const string Import = "import";
    /// <summary>A lead turned into a pupil.</summary>
    public const string Lead = "lead";
    /// <summary>Written by the migration from the data that existed before this table.</summary>
    public const string Backfill = "backfill";

    public static readonly IReadOnlyList<string> All =
        [Manual, Bulk, ClassArchive, Graduation, Delete, Import, Lead, Backfill];
}
