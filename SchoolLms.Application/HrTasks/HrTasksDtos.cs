using System.Text.Json.Serialization;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  HR topshiriqlari — the JSON contract (schoollms.client/src/types/hrTasks.ts).
//  ASP.NET's default web JSON (camelCase) turns these names into exactly the TS
//  field names; values (statuses, sources, kinds, keep_open …) are the old ones.
//  Times of day are "HH:mm" strings, dates "yyyy-MM-dd", instants ISO-8601 (UTC).
// ===========================================================================

/// <summary>Keyset-style page <c>{items, nextCursor, hasMore, total?}</c> (old <c>Page</c>).</summary>
public sealed record HrPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Total);

// ---------------------------------------------------------------- positions

public sealed record HrPositionDto(
    string Id, string Name, bool IsActive, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, int? EmployeesCount);

public sealed record HrPositionCreateInput(string? Name);

// ---------------------------------------------------------------- employees

/// <summary><c>HrEmployee</c>. A class hierarchy (node, create result) keeps one card shape.</summary>
public record HrEmployeeDto
{
    public string Id { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Role { get; init; } = "";
    public string HrRole { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? PositionId { get; init; }
    public string? Branch { get; init; }
    public long? TelegramUserId { get; init; }
    public string? InviteCode { get; init; }
    public DateTimeOffset? LinkedAt { get; init; }
    public bool IsActive { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? PositionName { get; init; }
    public int OpenTasksCount { get; init; }
    public string? PanelRole { get; init; }
    public IReadOnlyList<string> ManagerIds { get; init; } = [];
    public IReadOnlyList<string> ManagerNames { get; init; } = [];
    public int SubordinateCount { get; init; }
}

/// <summary>A tree node — the card + its subordinates (an employee with two managers appears twice).</summary>
public sealed record HrEmployeeNodeDto : HrEmployeeDto
{
    public IReadOnlyList<HrEmployeeNodeDto> Children { get; init; } = [];
}

public sealed record HrEmployeeTreeDto(IReadOnlyList<HrEmployeeNodeDto> Roots, IReadOnlyList<HrEmployeeNodeDto> Unassigned);

public sealed record HrCredentialsDto(string Login, string Password);

/// <summary><c>POST /employees</c> — the card + one-time credentials when a staff user was created.</summary>
public sealed record HrEmployeeCreateResultDto : HrEmployeeDto
{
    public HrCredentialsDto? Credentials { get; init; }
}

public sealed record HrEmployeeCreateInput(
    string? FullName,
    string? Phone,
    string? PositionId,
    string? Note,
    string? PanelRole,
    string? Branch,
    List<string>? ManagerIds,
    string? UserId,
    string? HrRole);

public sealed record HrManagersInput(List<string>? ManagerIds);

public sealed record HrInviteDto(string EmployeeId, string InviteCode, string? InviteUrl);

public sealed record HrUserCandidateDto(string Id, string FullName, string? Phone, string Role, string? EmployeeId);

/// <summary><c>GET /api/admin/hr/access</c> — what the panel may show this account.</summary>
public sealed record HrAccessDto(bool IsDirector, bool CanManage, string Scope, string? EmployeeId, string? HrRole);

// ---------------------------------------------------------------- templates

public sealed record HrTemplateDto(
    string Id,
    string Title,
    string? Description,
    string TargetType,
    string? TargetEmployeeId,
    string? TargetPositionId,
    string Recurrence,
    IReadOnlyList<int> Weekdays,
    IReadOnlyList<int> DaysOfMonth,
    string? DueTime,
    string Kind,
    decimal? TargetAmount,
    string? Unit,
    bool ProofRequired,
    string OverduePolicy,
    bool IsActive,
    string? CreatedBy,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? TargetEmployeeName,
    string? TargetPositionName);

public sealed record HrTemplateCreateInput(
    string? Title,
    string? Description,
    string? TargetType,
    string? TargetEmployeeId,
    string? TargetPositionId,
    string? Recurrence,
    List<int>? Weekdays,
    List<int>? DaysOfMonth,
    string? DueTime,
    string? Kind,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? TargetAmount,
    string? Unit,
    bool? ProofRequired);

public sealed record HrTemplateBatchItemInput(
    string? Title,
    string? Description,
    string? Recurrence,
    List<int>? Weekdays,
    List<int>? DaysOfMonth,
    string? DueTime,
    string? Kind,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? TargetAmount,
    string? Unit,
    bool? ProofRequired,
    string? AttachmentId);

public sealed record HrTemplateBatchInput(List<string>? EmployeeIds, List<HrTemplateBatchItemInput>? Items);

// ---------------------------------------------------------------- tasks

/// <summary><c>HrTask</c> (panel shape).</summary>
public record HrTaskDto
{
    public string Id { get; init; } = "";
    public string EmployeeId { get; init; } = "";
    public string? TemplateId { get; init; }
    public string Source { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string Kind { get; init; } = "";
    public decimal? TargetAmount { get; init; }
    public decimal CurrentAmount { get; init; }
    public string? Unit { get; init; }
    public bool ProofRequired { get; init; }
    public string OverduePolicy { get; init; } = "";
    public DateOnly PeriodStart { get; init; }
    public DateTimeOffset DueAt { get; init; }
    public string Status { get; init; } = "";
    public bool IsLate { get; init; }
    public string? EmployeeComment { get; init; }
    public string? DirectorComment { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ReturnedAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; init; }
    public bool NeedsAcceptance { get; init; }
    public string? VoiceUrl { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? EmployeeName { get; init; }
    public string? PositionName { get; init; }
    public int ProofCount { get; init; }
}

public sealed record HrTaskProofDto(
    string Id, string TaskId, string Kind, string? FilePath, string? OriginalName, string? ContentType,
    int? SizeBytes, string? Url, string? Text, DateTimeOffset? CreatedAt);

public sealed record HrTaskProgressDto(string Id, string TaskId, decimal Amount, string? Note, DateTimeOffset? CreatedAt);

public sealed record HrTaskAttachmentDto(
    string Id, string Kind, string Url, string ContentType, int SizeBytes, int? DurationSeconds,
    string? TaskId, string? OriginalName, DateTimeOffset? CreatedAt);

/// <summary><c>GET /tasks/{id}</c> — flat: the task + proofs, KPI steps and the manager's files.</summary>
public sealed record HrTaskDetailDto : HrTaskDto
{
    public IReadOnlyList<HrTaskProofDto> Proofs { get; init; } = [];
    public IReadOnlyList<HrTaskProgressDto> Progress { get; init; } = [];
    public IReadOnlyList<HrTaskAttachmentDto> Attachments { get; init; } = [];
}

public sealed record HrTaskCreateInput(
    string? EmployeeId,
    string? Title,
    string? Description,
    string? DueAt,
    string? Kind,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? TargetAmount,
    string? Unit,
    bool? ProofRequired);

public sealed record HrTaskReturnInput(string? Comment);

public sealed record HrTaskCancelInput(string? Comment);

public sealed record HrTaskBatchItemInput(
    string? Title,
    string? DueAt,
    string? Description,
    string? Kind,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? TargetAmount,
    string? Unit,
    string? AttachmentId);

public sealed record HrTaskBatchInput(List<string>? EmployeeIds, List<HrTaskBatchItemInput>? Items);

public sealed record HrBatchSkippedDto(string EmployeeId, string Reason);

/// <summary>Partial success: an inactive/out-of-scope employee is skipped, never fails the batch.</summary>
public sealed record HrBatchResultDto(
    int Created,
    IReadOnlyList<HrBatchSkippedDto> Skipped,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? TaskIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? TemplateIds);

// ---------------------------------------------------------------- settings

public sealed record HrSettingsDto(
    string Timezone,
    string DigestTime,
    IReadOnlyList<int> DigestWeekdays,
    bool MorningListEnabled,
    string MorningTime,
    string GenerationTime,
    string CloseOverdueTime,
    bool ReminderEnabled,
    IReadOnlyList<int> ReminderOffsets,
    bool RemindEndOfDay,
    DateTimeOffset? UpdatedAt);

/// <summary><c>PUT /settings</c> — the whole object (reminder fields default like the old API).</summary>
public sealed record HrSettingsInput(
    string? Timezone,
    string? DigestTime,
    List<int>? DigestWeekdays,
    bool? MorningListEnabled,
    string? MorningTime,
    string? GenerationTime,
    string? CloseOverdueTime,
    bool? ReminderEnabled,
    List<int>? ReminderOffsets,
    bool? RemindEndOfDay);

// ---------------------------------------------------------------- reports

public sealed record HrSummaryReportDto(
    DateOnly DateFrom, DateOnly DateTo, int TotalTasks, int DoneCount, int MissedCount, int OpenCount,
    int ReturnedCount, int CancelledCount, int LateCount, int OverdueCount, int SelfCreatedCount,
    int EmployeesCount, double DonePercent, int UnacceptedCount);

public sealed record HrEmployeeReportRowDto(
    string EmployeeId, string FullName, string? PositionName, int TotalTasks, int DoneCount, int MissedCount,
    int OpenCount, int ReturnedCount, int LateCount, double DonePercent, decimal? KpiTarget, decimal? KpiCurrent,
    double? KpiPercent)
{
    /// <summary>For the CSV "Bo'linma" column only — not in the JSON contract.</summary>
    [JsonIgnore] public string? Branch { get; init; }
}

public sealed record HrEmployeeReportDto(DateOnly DateFrom, DateOnly DateTo, IReadOnlyList<HrEmployeeReportRowDto> Items);

/// <summary><c>GET /dashboard</c> — the panel's first screen in one call (today, local).</summary>
public sealed record HrDashboardDto(
    DateOnly Date,
    HrSummaryReportDto Today,
    HrEmployeeReportDto Employees,
    IReadOnlyList<HrTaskDto> Overdue,
    int OverdueTotal,
    IReadOnlyList<HrTaskDto> SelfCreatedToday);

// ---------------------------------------------------------------- employee (Mini App + "Mening topshiriqlarim")

public sealed record HrMeDto(string EmployeeId, string FullName, string? PositionName, string Timezone, DateOnly Today);

public sealed record HrAppProofDto(string Id, string Kind, string? Url, string? OriginalName);

public sealed record HrAppAttachmentDto(string Id, string Kind, string Url, string ContentType, int SizeBytes, int? DurationSeconds);

/// <summary>Mini App <c>Task</c>.</summary>
public sealed record HrAppTaskDto(
    string Id,
    string Title,
    string? Description,
    string Kind,
    string Status,
    string Source,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    bool IsLate,
    bool ProofRequired,
    decimal? TargetAmount,
    decimal? CurrentAmount,
    string? Unit,
    string? EmployeeComment,
    string? DirectorComment,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<HrAppProofDto> Proofs,
    DateTimeOffset? AcceptedAt,
    bool NeedsAcceptance,
    IReadOnlyList<HrAppAttachmentDto> Attachments,
    string? VoiceUrl);

public sealed record HrTaskListDto(DateOnly Date, IReadOnlyList<HrAppTaskDto> Items);

public sealed record HrUploadedProofDto(string Id, string? Url);

public sealed record HrSummaryKpiDto(string? TaskId, string Title, decimal? CurrentAmount, decimal? TargetAmount, string? Unit);

public sealed record HrAppSummaryDto(
    DateOnly PeriodStart, DateOnly PeriodEnd, int Total, int Done, int Missed, int Open, int Late, double? Percent,
    IReadOnlyList<HrSummaryKpiDto> Kpi);

public sealed record HrSelfTaskInput(string? Title, string? Description, string? DueAt);

public sealed record HrCompleteInput(string? Comment, List<string>? ProofIds);

public sealed record HrProgressInput(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? Amount,
    string? Note);

// ---------------------------------------------------------------- list filters (query strings)

public sealed record HrEmployeeFilter(
    string? Q = null, string? PositionId = null, bool? IsActive = null, bool? Linked = null, string? Branch = null,
    string? Sort = null);

public sealed record HrTemplateFilter(
    string? Q = null, bool? IsActive = null, string? TargetType = null, string? TargetEmployeeId = null,
    string? TargetPositionId = null, string? Recurrence = null, string? Branch = null, string? Sort = null);

public sealed record HrTaskFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? EmployeeId = null,
    string? PositionId = null,
    IReadOnlyList<string>? Statuses = null,
    IReadOnlyList<string>? Sources = null,
    bool Overdue = false,
    string? Branch = null,
    bool? Accepted = null,
    string? Q = null,
    string? Sort = null,
    string? View = null);

/// <summary>
/// Panel list "view" (<c>?view=</c>): whose tasks the "Topshiriqlar" list shows. Null / unknown = everything in
/// scope, as before. The caller's own tasks live in "Mening topshiriqlarim" (<c>/api/hr/me/tasks</c>).
/// </summary>
public static class HrTaskViews
{
    /// <summary>Everyone in scope except the caller.</summary>
    public const string Staff = "staff";
    /// <summary>Tasks the caller gave — directly, or through a recurring template the caller made.</summary>
    public const string Given = "given";
}

public sealed record HrPaging(int Limit = HrPagingDefaults.Limit, string? Cursor = null, bool WithTotal = false);

public static class HrPagingDefaults
{
    public const int Limit = 50;
}
