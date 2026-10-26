namespace SchoolLms.Domain;

// ===========================================================================
//  Automatic Telegram messages to parents (docs/modules/auto-messages.md, 2026-10-13).
//  One row per rule; every rule ships switched off and is turned on once the school
//  approves its text. Telegram is the only channel (CLAUDE.md).
//  2026-10-15: runs with statuses and approval, retries, the absence cut-off, the payment rule
//  and one delivery log for automatic and manual sends.
// ===========================================================================

/// <summary>One automatic message rule (<see cref="AutoMessageKind"/>).</summary>
public class AutoMessageRule
{
    /// <summary><see cref="AutoMessageKind"/>: absence | invoice | debt | birthday | payment.</summary>
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

    /// <summary>
    /// When due, the rule prepares its messages as "Tasdiq kutmoqda" and sends nothing until staff
    /// approve the run (2026-10-15). Never applies to <see cref="AutoMessageKind.Payment"/>.
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// Absence rule only: "HH:mm" — only lessons that start before it count (client: "11:30 gacha").
    /// Null — the send time.
    /// </summary>
    public string? CutoffTime { get; set; }

    /// <summary>
    /// Pictures sent with the message (<c>/uploads/…</c>, in order, ≤ 10; 2026-10-25): one → a photo,
    /// more → an album, the text under them. Empty — text only. Never used by
    /// <see cref="AutoMessageKind.Payment"/>, whose message is the receipt PDF's caption.
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();

    // --- Templates staff create themselves (2026-10-26) ---
    // Client: "kelajakda qo'shimcha avtomatik habar yaratish kerak bo'ladigan bo'lsa kod kiritmasdan".
    // A template is a rule with key "c_…": it goes by the calendar only (no event of its own) to the
    // parents of every pupil, or of the pupils in the chosen classes. Null / empty on the built-in rules.

    /// <summary>The template's name as staff typed it; null on the built-in rules (the screen names those).</summary>
    public string? Title { get; set; }

    /// <summary><see cref="AutoMessageSchedule"/>: once | daily | weekly | monthly; null on the built-in rules.</summary>
    public string? Schedule { get; set; }

    /// <summary>Once: the day it goes, "yyyy-MM-dd".</summary>
    public string? SendDate { get; set; }

    /// <summary>Weekly: the days it goes, 1 = Monday … 7 = Sunday.</summary>
    public List<int> WeekDays { get; set; } = new();

    /// <summary>
    /// Who gets it: classes.id of the chosen classes; empty — every pupil. A chosen class that has since
    /// been archived or deleted reaches nobody (never "everyone").
    /// </summary>
    public List<string> ClassIds { get; set; } = new();

    /// <summary>
    /// A deleted template: never runs and is not shown, but its row stays so the history keeps its name
    /// and its runs.
    /// </summary>
    public bool IsArchived { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

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
    /// <summary>Payment received: the receipt's caption (event-driven, never scheduled).</summary>
    public const string Payment = "payment";

    /// <summary>The built-in rules, in the screen's order.</summary>
    public static readonly IReadOnlyList<string> All = [Absence, Invoice, Debt, Birthday, Payment];

    /// <summary>Key prefix of a template staff created (2026-10-26): "c_" and 8 hex digits.</summary>
    public const string CustomPrefix = "c_";

    public static bool IsCustom(string key) => key.StartsWith(CustomPrefix, StringComparison.Ordinal);

    /// <summary>Monthly rules run on <see cref="AutoMessageRule.DayOfMonth"/>; the others every day.</summary>
    public static bool IsMonthly(string key) => key is Invoice or Debt;

    /// <summary>Like <see cref="IsMonthly(string)"/>, and a template whose schedule is monthly.</summary>
    public static bool IsMonthly(AutoMessageRule rule) =>
        IsMonthly(rule.Key) || (IsCustom(rule.Key) && rule.Schedule == AutoMessageSchedule.Monthly);

    /// <summary>
    /// A rule that runs by the day (not monthly) goes on <paramref name="day"/>: always for the built-in
    /// ones and daily templates; a once template on its date; a weekly one on its week days.
    /// </summary>
    public static bool RunsOn(AutoMessageRule rule, DateOnly day)
    {
        if (!IsCustom(rule.Key)) return true;
        return rule.Schedule switch
        {
            AutoMessageSchedule.Once => rule.SendDate == day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            AutoMessageSchedule.Weekly => rule.WeekDays.Contains(day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek),
            AutoMessageSchedule.Daily => true,
            _ => false,
        };
    }

    /// <summary>Sent when something happens (a payment), never by the scheduler.</summary>
    /// <remarks>Compared as text on purpose: <c>key is Payment</c> would bind to the <see cref="Domain.Payment"/> type.</remarks>
    public static bool IsEventDriven(string key) => string.Equals(key, Payment, StringComparison.Ordinal);
}

/// <summary>When a template staff created goes (<see cref="AutoMessageRule.Schedule"/>).</summary>
public static class AutoMessageSchedule
{
    /// <summary>On one day (<see cref="AutoMessageRule.SendDate"/>).</summary>
    public const string Once = "once";
    public const string Daily = "daily";
    /// <summary>On the chosen week days (<see cref="AutoMessageRule.WeekDays"/>).</summary>
    public const string Weekly = "weekly";
    /// <summary>On <see cref="AutoMessageRule.DayOfMonth"/>.</summary>
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> All = [Once, Daily, Weekly, Monthly];
}

/// <summary>Statuses of a run and of a log row.</summary>
public static class AutoMessageStatus
{
    /// <summary>Waiting for its time ("Rejalashtirilgan").</summary>
    public const string Scheduled = "scheduled";
    /// <summary>Prepared, waiting for staff approval ("Tasdiq kutmoqda").</summary>
    public const string PendingApproval = "pending_approval";
    /// <summary>Run only: messages are going out.</summary>
    public const string Sending = "sending";
    /// <summary>Run only: finished ("Yuborildi").</summary>
    public const string Done = "done";
    /// <summary>Stopped by staff, by switching the rule off, or by the end of its period ("To'xtatilgan").</summary>
    public const string Cancelled = "cancelled";

    /// <summary>A run that may still send something.</summary>
    public static readonly string[] OpenRun = [Scheduled, PendingApproval, Sending];

    /// <summary>A log row not sent yet that may still be stopped.</summary>
    public static readonly string[] WaitingRow = [Scheduled, PendingApproval];
}

/// <summary>Who sent a log row.</summary>
public static class AutoMessageSource
{
    /// <summary>A rule, or the receipt right after a payment.</summary>
    public const string Auto = "auto";
    /// <summary>Staff pressed a button (the nurse's absence button, a receipt resend).</summary>
    public const string Manual = "manual";
}

/// <summary>
/// One rule's run for one period (2026-10-15): when it is planned, whether it waits for approval,
/// who approved or stopped it, and how many chats it reached. One per (rule, period).
/// </summary>
public class AutoMessageRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="AutoMessageKind"/> (never payment).</summary>
    public string RuleKey { get; set; } = string.Empty;

    /// <summary>"2026-10-13" (daily rules) or "2026-10" (monthly).</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary><see cref="AutoMessageStatus"/>: scheduled | pending_approval | sending | done | cancelled.</summary>
    public string Status { get; set; } = AutoMessageStatus.Scheduled;

    /// <summary>When the rule is due (its send time on its day).</summary>
    public DateTimeOffset PlannedAt { get; set; }

    /// <summary>Pupils the run covers.</summary>
    public int Pupils { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int NoTelegram { get; set; }
    /// <summary>Chats still waiting (scheduled or pending approval).</summary>
    public int Pending { get; set; }
    public int Cancelled { get; set; }

    /// <summary>What the run left out and why (absence: classes whose attendance was not finished).</summary>
    public string? Note { get; set; }

    /// <summary>
    /// The rule's pictures when the run prepared its messages (2026-10-25) — like the rows' text, what
    /// was approved is what goes, and a retry sends the same pictures.
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>users.id of the approver.</summary>
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    /// <summary>users.id, or "auto" when the period ended or the rule was switched off.</summary>
    public string? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One automatic message to one parent chat (or the pupil had no chat at all). One row per
/// (rule, period, pupil, chat): a pupil–chat already sent for the period is never sent again.
/// Payment rows are the exception — one per receipt send (<see cref="RefId"/>).
/// </summary>
public class AutoMessageLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="AutoMessageKind"/>.</summary>
    public string RuleKey { get; set; } = string.Empty;

    /// <summary>"2026-10-13" (daily rules, payment, manual) or "2026-10" (monthly).</summary>
    public string Period { get; set; } = string.Empty;

    public string StudentId { get; set; } = string.Empty;

    /// <summary>Parent chat; null — the pupil has no parent on Telegram.</summary>
    public long? ChatId { get; set; }

    /// <summary>
    /// <see cref="BroadcastDelivery"/>: sent | failed | no_telegram, or
    /// <see cref="AutoMessageStatus"/>: scheduled | pending_approval | cancelled.
    /// </summary>
    public string Status { get; set; } = BroadcastDelivery.Sent;

    /// <summary>The text as sent (Telegram HTML).</summary>
    public string Text { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    /// <summary>The run this row belongs to; null — a payment receipt or the nurse's button.</summary>
    public Guid? RunId { get; set; }

    /// <summary>Payment rows: the receipt number (one receipt may be sent more than once).</summary>
    public string? RefId { get; set; }

    /// <summary><see cref="AutoMessageSource"/>: auto | manual.</summary>
    public string Source { get; set; } = AutoMessageSource.Auto;

    /// <summary>users.id of the staff member who pressed the button (manual rows).</summary>
    public string? SentBy { get; set; }

    /// <summary>Send attempts so far; a failed row of a run is retried while below 3.</summary>
    public int Attempts { get; set; }

    /// <summary>Why the last attempt did not get through (or why the row was stopped).</summary>
    public string? Error { get; set; }

    /// <summary>When the row was meant to go (the run's planned time).</summary>
    public DateTimeOffset? PlannedAt { get; set; }
}
