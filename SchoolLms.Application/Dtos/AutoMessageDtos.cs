using SchoolLms.Application.Services;

namespace SchoolLms.Application.Dtos;

/// <summary>One automatic message rule as the screen shows it (docs/modules/auto-messages.md).</summary>
/// <param name="LastSent">Chats the last period reached; LastFailed / LastNoTelegram likewise.</param>
public record AutoMessageRuleDto(
    string Key, bool Enabled, string SendTime, int DayOfMonth, decimal MinAmount, string Text,
    bool Monthly, IReadOnlyList<AutoMessageTokenDto> Tokens, string DefaultText,
    string? LastPeriod, DateTimeOffset? LastRunAt, int LastSent, int LastFailed, int LastNoTelegram,
    DateTimeOffset UpdatedAt, string? UpdatedByName);

/// <summary>Saving a rule.</summary>
public record SaveAutoMessageRuleRequest(bool Enabled, string SendTime, int DayOfMonth, decimal MinAmount, string Text);

/// <summary>Preview with a text not saved yet (null — the saved text).</summary>
public record PreviewAutoMessageRequest(string? Text);

/// <summary>One row of the automatic message log.</summary>
public record AutoMessageLogRowDto(
    Guid Id, string RuleKey, string Period, string StudentId, string FullName, string ClassName,
    long? ChatId, string? ParentName, string Status, string Text, DateTimeOffset At);
