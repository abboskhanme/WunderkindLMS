namespace SchoolLms.Domain;

// ===========================================================================
//  Attendance "red list" — pupils who miss school day after day without a reason, and the
//  follow-up with their parents (client, 2026-10-15; docs/modules/attendance-red-list.md).
//
//  A daily scan reads the journal (the same pupil-day rule as the attendance report) and opens
//  ONE case per pupil when the school's criteria are met. The homeroom teacher and management
//  work the case: every call is an action with a comment, and the latest action sets the case's
//  status. "Hal bo'ldi" closes it; a later run of absences opens a new case.
//
//  Statuses are fixed (a check constraint, no catalog) — unlike debtors, the school asked for
//  exactly four. The case keeps its current status, callback date and last action time as
//  columns so lists and the overdue rule need no history scan; they are recomputed from the
//  live actions whenever an action is deleted (AttendanceRedListService.DeleteActionAsync).
//
//  Not financial: full CRUD for app_rw. Actions are soft-deleted all the same — who promised
//  what on the phone must not disappear.
// ===========================================================================

/// <summary>One follow-up case: a pupil on the red list until someone marks it resolved.</summary>
public class AttendanceCase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>students.id</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>The school day the scan opened the case.</summary>
    public DateOnly OpenedOn { get; set; }

    /// <summary><see cref="AttendanceCaseTrigger"/>: which rule opened it (consecutive wins when both are met).</summary>
    public string Trigger { get; set; } = AttendanceCaseTrigger.Consecutive;

    /// <summary>
    /// The longest run of consecutive counted absences seen while the case is open, including the
    /// run that opened it. Never lowered by a later scan.
    /// </summary>
    public int Streak { get; set; }

    /// <summary>Counted absences within the window (settings), as of the last scan.</summary>
    public int WindowCount { get; set; }

    /// <summary>The last full day the pupil missed (counted), as of the last scan.</summary>
    public DateOnly? LastAbsentOn { get; set; }

    /// <summary>The last day the pupil came (present or partly present), as of the last scan.
    /// Later than <see cref="LastAbsentOn"/> = "qaytdi".</summary>
    public DateOnly? LastPresentOn { get; set; }

    /// <summary>The counted absence days the last scan saw, newest first (at most 20).</summary>
    public List<DateOnly> AbsentDates { get; set; } = [];

    /// <summary><see cref="AttendanceCaseStatus"/> — the status of the latest live action, or "aloqa_kerak".</summary>
    public string Status { get; set; } = AttendanceCaseStatus.ContactNeeded;

    /// <summary>Agreed call-back day of the latest action (only with "qayta_qongiroq").</summary>
    public DateOnly? CallbackOn { get; set; }

    /// <summary>When the latest live action was written; null — nobody has acted yet.</summary>
    public DateTimeOffset? LastActionAt { get; set; }

    /// <summary>Set by a "hal_boldi" action. null = open (one open case per pupil, partial unique index).</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>users.id of whoever closed it.</summary>
    public string? ClosedBy { get; set; }

    /// <summary>When the homeroom / track teachers were told on Telegram (null — not reached).</summary>
    public DateTimeOffset? NotifiedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time a scan refreshed the numbers.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One call or note on a case. Append-only; a mistaken row gets <see cref="DeletedAt"/>.</summary>
public class AttendanceCaseAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CaseId { get; set; }

    /// <summary><see cref="AttendanceCaseStatus"/> after this action.</summary>
    public string Status { get; set; } = AttendanceCaseStatus.ContactNeeded;

    /// <summary>What was done — required (the database refuses a blank one).</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Agreed call-back day ("qayta_qongiroq" only).</summary>
    public DateOnly? CallbackOn { get; set; }

    /// <summary>users.id of the author — from the token, never from the request body.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>The author's name at the time of writing.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary><see cref="AttendanceCaseActorType"/>: teacher (Mini App / portal) or staff (admin panel).</summary>
    public string ActorType { get; set; } = AttendanceCaseActorType.Staff;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Soft delete. Lists and the case status read live rows only.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>users.id of whoever deleted it.</summary>
    public string? DeletedBy { get; set; }
}

/// <summary>The red-list criteria and switches — one row (id = 1).</summary>
public class AttendanceRedListSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Consecutive school days with a counted full-day absence that open a case.</summary>
    public int ConsecutiveDays { get; set; } = 3;

    /// <summary>Calendar days of the window rule (today included).</summary>
    public int WindowDays { get; set; } = 14;

    /// <summary>Counted full-day absences within <see cref="WindowDays"/> that open a case.</summary>
    public int WindowUnexcused { get; set; } = 5;

    /// <summary>true — excused full-day absences count too; false — only unexcused ones (excused days break a run).</summary>
    public bool CountExcused { get; set; }

    /// <summary>An open case with no action for this many days is shown as overdue and goes into the digest.</summary>
    public int EscalateAfterDays { get; set; } = 2;

    /// <summary>
    /// Tell the homeroom and track-group teachers on Telegram when a case opens. Ships off (2026-10-15): switched on
    /// once the school approves the text, like every automatic message.
    /// </summary>
    public bool NotifyTeachers { get; set; }

    /// <summary>Send the daily digest of overdue cases to staff holding the <c>attendanceCases</c> key. Ships off, as above.</summary>
    public bool NotifyManagement { get; set; }

    public DateTimeOffset? LastScanAt { get; set; }

    /// <summary>The scheduler's last slot ("2026-10-15 13:00") — each slot runs once.</summary>
    public string? LastScanSlot { get; set; }

    /// <summary>The day the last management digest went out.</summary>
    public DateOnly? LastDigestOn { get; set; }

    /// <summary>users.id of the last editor.</summary>
    public string? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The four case statuses (client's words).</summary>
public static class AttendanceCaseStatus
{
    /// <summary>"Aloqa kerak" — nobody has reached the parents yet.</summary>
    public const string ContactNeeded = "aloqa_kerak";
    /// <summary>"Qayta qo'ng'iroq" — call again (on <see cref="AttendanceCase.CallbackOn"/>).</summary>
    public const string CallBack = "qayta_qongiroq";
    /// <summary>"Bog'lanib bo'lmadi" — could not reach them; escalated to management at once.</summary>
    public const string Unreachable = "boglanib_bolmadi";
    /// <summary>"Hal bo'ldi" — resolved; closes the case.</summary>
    public const string Resolved = "hal_boldi";

    public static readonly IReadOnlyList<string> All = [ContactNeeded, CallBack, Unreachable, Resolved];

    public static bool IsValid(string? status) => status is not null && All.Contains(status);
}

/// <summary>Which rule opened a case.</summary>
public static class AttendanceCaseTrigger
{
    public const string Consecutive = "consecutive";
    public const string Window = "window";
}

/// <summary>Who wrote an action.</summary>
public static class AttendanceCaseActorType
{
    public const string Teacher = "teacher";
    public const string Staff = "staff";
}
