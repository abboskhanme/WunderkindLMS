namespace SchoolLms.Domain;

// ===========================================================================
//  Automatic Telegram messages to parents (docs/modules/auto-messages.md, 2026-10-13).
//  One row per rule; every rule ships switched off and is turned on once the school
//  approves its text. Telegram is the only channel (CLAUDE.md).
// ===========================================================================

/// <summary>One automatic message rule (<see cref="AutoMessageKind"/>).</summary>
public class AutoMessageRule
{
    /// <summary><see cref="AutoMessageKind"/>: absence | invoice | debt | birthday.</summary>
    public string Key { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>"HH:mm", school time zone.</summary>
    public string SendTime { get; set; } = "10:00";

    /// <summary>Monthly rules only (invoice, debt): the day the message goes, 1–28.</summary>
    public int DayOfMonth { get; set; } = 1;

    /// <summary>Debt rule only: smaller debts get no reminder, so'm.</summary>
    public decimal MinAmount { get; set; }

    /// <summary>Broadcast markup with tokens ({fish}, {qarzdorlik}, ...).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The last period this rule finished ("2026-10-13" or "2026-10").</summary>
    public string? LastPeriod { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>users.id of the last editor.</summary>
    public string? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Rule keys.</summary>
public static class AutoMessageKind
{
    /// <summary>Pupil absent the whole day (the nurse's roll-call).</summary>
    public const string Absence = "absence";
    /// <summary>The month's invoice has been issued.</summary>
    public const string Invoice = "invoice";
    /// <summary>Monthly debt reminder.</summary>
    public const string Debt = "debt";
    /// <summary>Birthday greeting.</summary>
    public const string Birthday = "birthday";

    public static readonly IReadOnlyList<string> All = [Absence, Invoice, Debt, Birthday];

    /// <summary>Monthly rules run on <see cref="AutoMessageRule.DayOfMonth"/>; the others every day.</summary>
    public static bool IsMonthly(string key) => key is Invoice or Debt;
}

/// <summary>
/// One automatic message to one parent chat (or the pupil had no chat at all). One row per
/// (rule, period, pupil, chat): a pupil–chat already sent for the period is never sent again.
/// </summary>
public class AutoMessageLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="AutoMessageKind"/>.</summary>
    public string RuleKey { get; set; } = string.Empty;

    /// <summary>"2026-10-13" (daily rules) or "2026-10" (monthly).</summary>
    public string Period { get; set; } = string.Empty;

    public string StudentId { get; set; } = string.Empty;

    /// <summary>Parent chat; null — the pupil has no parent on Telegram.</summary>
    public long? ChatId { get; set; }

    /// <summary><see cref="BroadcastDelivery"/>: sent | failed | no_telegram.</summary>
    public string Status { get; set; } = BroadcastDelivery.Sent;

    /// <summary>The text as sent (Telegram HTML).</summary>
    public string Text { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}
