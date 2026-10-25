using SchoolLms.Application.Services;

namespace SchoolLms.Application.Dtos;

/// <summary>One automatic message rule as the screen shows it (docs/modules/auto-messages.md).</summary>
/// <param name="LastSent">Chats the last period reached; LastFailed / LastNoTelegram likewise.</param>
/// <param name="RequiresApproval">Due messages wait for "Tasdiqlash" (2026-10-15).</param>
/// <param name="CutoffTime">Absence: only lessons starting before it count; null — the send time.</param>
/// <param name="EventDriven">Payment: sent with the receipt, has no time of its own.</param>
/// <param name="ImageUrls">Pictures sent with the text (<c>/uploads/…</c>, 2026-10-25); empty — text only.</param>
public record AutoMessageRuleDto(
    string Key, bool Enabled, string SendTime, int DayOfMonth, decimal MinAmount, string Text,
    bool Monthly, IReadOnlyList<AutoMessageTokenDto> Tokens, string DefaultText,
    string? LastPeriod, DateTimeOffset? LastRunAt, int LastSent, int LastFailed, int LastNoTelegram,
    DateTimeOffset UpdatedAt, string? UpdatedByName,
    bool RequiresApproval = false, string? CutoffTime = null, bool EventDriven = false,
    List<string>? ImageUrls = null);

/// <summary>
/// Saving a rule. <paramref name="RequiresApproval"/> null — unchanged; <paramref name="CutoffTime"/> empty — the
/// send time; <paramref name="ImageUrls"/> null — unchanged, empty — no pictures (uploaded first through
/// <c>/api/admin/uploads</c>, ≤ 10; ignored for the payment rule).
/// </summary>
public record SaveAutoMessageRuleRequest(
    bool Enabled, string SendTime, int DayOfMonth, decimal MinAmount, string Text,
    bool? RequiresApproval = null, string? CutoffTime = null, List<string>? ImageUrls = null);

/// <summary>Preview with a text not saved yet (null — the saved text).</summary>
public record PreviewAutoMessageRequest(string? Text);

/// <summary>One row of the delivery log (automatic and manual sends).</summary>
/// <param name="Source">auto | manual.</param>
/// <param name="RefId">Payment rows: the receipt number.</param>
public record AutoMessageLogRowDto(
    Guid Id, string RuleKey, string Period, string StudentId, string FullName, string ClassName,
    long? ChatId, string? ParentName, string Status, string Text, DateTimeOffset At,
    Guid? RunId = null, string? RefId = null, string Source = "auto", string? SentByName = null,
    int Attempts = 0, string? Error = null, DateTimeOffset? PlannedAt = null);

/// <summary>Filters of the delivery log. Days are the school's calendar.</summary>
public record AutoMessageLogQuery(
    string? Rule = null, string? Status = null, DateOnly? From = null, DateOnly? To = null,
    string? StudentId = null, string? Search = null, Guid? RunId = null, string? Source = null);

/// <summary>One page of the delivery log.</summary>
public record AutoMessageLogPageDto(int Total, int Page, int PageSize, List<AutoMessageLogRowDto> Rows);

/// <summary>One rule's run for one period.</summary>
/// <param name="ImageUrls">The pictures the run sends (taken from the rule when it prepared its messages).</param>
public record AutoMessageRunDto(
    Guid Id, string RuleKey, string Period, string Status, DateTimeOffset PlannedAt,
    int Pupils, int Sent, int Failed, int NoTelegram, int Pending, int Cancelled, string? Note,
    string? ApprovedByName, DateTimeOffset? ApprovedAt, string? CancelledByName, DateTimeOffset? CancelledAt,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, List<string>? ImageUrls = null);

/// <summary>Messages of one rule in one status on one day.</summary>
public record AutoMessageStatsCellDto(string Day, string RuleKey, string Status, int Count);

/// <summary>Delivery statistics for a period: totals per status and per rule × status × day.</summary>
public record AutoMessageStatsDto(
    string From, string To, Dictionary<string, int> Totals,
    List<AutoMessageStatsCellDto> ByRule, List<AutoMessageStatsCellDto> Cells);

/// <summary>Taking one pupil out of a run waiting for approval.</summary>
public record ExcludeFromRunRequest(string StudentId);
