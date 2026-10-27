namespace SchoolLms.Application.Dtos;

// Attendance red list (docs/modules/attendance-red-list.md, 2026-10-15). Dates are "yyyy-MM-dd".

/// <summary>A parent phone to call: the pupil card's parent first, then the linked guardians.</summary>
public record RedListContactDto(string Name, string Phone, string? Relation);

/// <summary>One case as every list shows it.</summary>
/// <param name="Group">The class, or for the teacher's own list the class / track group the pupil is listed under.</param>
/// <param name="Returned">The pupil came to school after the last counted absence.</param>
/// <param name="Overdue">Needs management's attention: "unreachable", "callback_due" or "no_action" (<see cref="OverdueReason"/>).</param>
/// <param name="DaysWithoutAction">Calendar days since the last action (or since opening when there is none).</param>
public record AttendanceCaseRowDto(
    Guid Id, string StudentId, string FullName, string ClassName, string Group, bool Archived,
    string Status, string Trigger, int Streak, int WindowCount,
    DateOnly OpenedOn, DateOnly? LastAbsentOn, DateOnly? LastPresentOn, bool Returned,
    IReadOnlyList<DateOnly> AbsentDates, DateOnly? CallbackOn,
    DateTimeOffset? LastActionAt, string? LastComment, string? LastActorName, int DaysWithoutAction,
    bool Overdue, string? OverdueReason,
    IReadOnlyList<HomeroomTeacherDto> Teachers, IReadOnlyList<RedListContactDto> Contacts,
    DateTimeOffset? ClosedAt, string? ClosedByName);

/// <summary>One action in a case's history (live rows only), newest first.</summary>
public record AttendanceCaseActionDto(
    Guid Id, string Status, string Comment, DateOnly? CallbackOn,
    string ActorName, string ActorType, DateTimeOffset CreatedAt);

/// <summary>A case with its history.</summary>
public record AttendanceCaseDetailDto(AttendanceCaseRowDto Case, IReadOnlyList<AttendanceCaseActionDto> Actions);

/// <summary>Admin list: counts for the status tabs, the rows of the chosen tab and the filter options.</summary>
/// <param name="Counts">aloqa_kerak, qayta_qongiroq, boglanib_bolmadi, open, overdue and hal_boldi (closed in the last <see cref="ClosedDays"/> days).</param>
public record AttendanceCaseListDto(
    IReadOnlyDictionary<string, int> Counts, IReadOnlyList<AttendanceCaseRowDto> Rows,
    IReadOnlyList<string> Classes, IReadOnlyList<HomeroomTeacherDto> Teachers, int ClosedDays);

/// <summary>A pupil's open case, for links from other attendance screens.</summary>
public record OpenAttendanceCaseDto(string StudentId, Guid CaseId, string Status);

/// <summary>The teacher's own red list (Mini App and portal).</summary>
/// <param name="Groups">The classes and track groups the teacher leads.</param>
public record TeacherRedListDto(
    IReadOnlyList<string> Groups, int Overdue, IReadOnlyList<AttendanceCaseDetailDto> Cases,
    int ConsecutiveDays, int WindowDays, int WindowUnexcused, bool CountExcused);

/// <summary>Writing an action. Status is required; comment too; call-back day only with "qayta_qongiroq"
/// (tomorrow when left out).</summary>
public record AddAttendanceCaseActionRequest(string? Status, string? Comment, DateOnly? CallbackOn);

/// <summary>The criteria and switches as the settings card shows them.</summary>
public record AttendanceRedListSettingsDto(
    int ConsecutiveDays, int WindowDays, int WindowUnexcused, bool CountExcused, int EscalateAfterDays,
    bool NotifyTeachers, bool NotifyManagement, DateTimeOffset? LastScanAt, IReadOnlyList<string> ScanTimes,
    DateTimeOffset UpdatedAt, string? UpdatedByName);

/// <summary>Saving the criteria.</summary>
public record SaveAttendanceRedListSettingsRequest(
    int ConsecutiveDays, int WindowDays, int WindowUnexcused, bool CountExcused, int EscalateAfterDays,
    bool NotifyTeachers, bool NotifyManagement);

/// <summary>What one scan did.</summary>
/// <param name="Pupils">Pupils whose days were read.</param>
/// <param name="Opened">New cases.</param>
/// <param name="Updated">Open cases whose numbers changed.</param>
/// <param name="Open">Open cases after the scan.</param>
/// <param name="Notified">Teacher chats that got the "new case" notice.</param>
public record AttendanceScanResultDto(int Pupils, int Opened, int Updated, int Open, int Notified);

/* ================= Sinf rahbari: kelmaganlar izohi (2026-10-26) ================= */

/// <summary>An absence reason the homeroom teacher may pick (late reasons are not offered).</summary>
public record HomeroomAbsenceReasonDto(string Id, string Name, bool Unexcused);

/// <summary>
/// One of my pupils who missed school that day: the whole day (<c>absent</c>) or some lessons
/// (<c>partial</c>), the day's reason, and my comment.
/// </summary>
public record HomeroomAbsenceDto(
    string StudentId, string FullName, string Group, string Status, List<int> Periods, int HeldLessons,
    string? ReasonId, bool Unexcused, string? Comment, string? CommentBy, DateTimeOffset? CommentAt);

/// <summary>The homeroom teacher's absences of one day.</summary>
public record HomeroomAbsencesDto(
    DateOnly Date, IReadOnlyList<string> Groups, IReadOnlyList<HomeroomAbsenceReasonDto> Reasons,
    IReadOnlyList<HomeroomAbsenceDto> Rows);

/// <summary>Saving a day: the new reason (null — unchanged) and the comment (empty — removed).</summary>
public record SaveHomeroomAbsenceRequest(string? ReasonId, string? Comment);
