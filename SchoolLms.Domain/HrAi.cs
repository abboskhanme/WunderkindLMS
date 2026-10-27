namespace SchoolLms.Domain;

// ===========================================================================
//  HR topshiriqlari — AI orqali yaratish (docs/modules/hr-ai-tasks.md, phase 1, 2026-10-27).
//  A director writes or speaks to the bot; the AI's answer is validated in code and kept here as a
//  draft. Only the director's "Tasdiqlash" turns a draft into tasks — through HrTasksService.
// ===========================================================================

/// <summary>One AI proposal waiting for (or past) the director's decision.</summary>
public class HrAiDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The director (<see cref="AppUser.Id"/>).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The private chat the card went to.</summary>
    public long ChatId { get; set; }

    /// <summary>The card's message, edited when a button is pressed; null — not sent.</summary>
    public long? MessageId { get; set; }

    /// <summary><see cref="HrAiSources"/>: text | voice.</summary>
    public string Source { get; set; } = HrAiSources.Text;

    /// <summary>The director's message, or the voice transcript.</summary>
    public string InputText { get; set; } = string.Empty;

    /// <summary>The validated proposal (JSON, refs resolved to ids and names).</summary>
    public string Proposal { get; set; } = "{}";

    /// <summary><see cref="HrAiDraftStatuses"/>.</summary>
    public string Status { get; set; } = HrAiDraftStatuses.Pending;

    /// <summary>What approval created and what failed (JSON).</summary>
    public string? Result { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>The last status change ("O'zgartirish" opens a 30-minute window from here).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class HrAiSources
{
    public const string Text = "text";
    public const string Voice = "voice";
}

public static class HrAiDraftStatuses
{
    /// <summary>On the card, waiting for a button.</summary>
    public const string Pending = "pending";
    /// <summary>"O'zgartirish" pressed: the director's next message revises it.</summary>
    public const string Editing = "editing";
    /// <summary>"Tasdiqlash" pressed, rows being created (a second press finds this and stops).</summary>
    public const string Approving = "approving";
    public const string Approved = "approved";
    public const string Cancelled = "cancelled";
    /// <summary>Not decided within <see cref="HrLimits.DraftTtlHours"/>.</summary>
    public const string Expired = "expired";
    /// <summary>A revision took its place.</summary>
    public const string Replaced = "replaced";
    /// <summary>Approved, but nothing could be created.</summary>
    public const string Failed = "failed";

    public static readonly string[] All = [Pending, Editing, Approving, Approved, Cancelled, Expired, Replaced, Failed];

    /// <summary>A draft whose buttons still act.</summary>
    public static readonly string[] Open = [Pending, Editing];
}
