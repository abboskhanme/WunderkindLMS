namespace SchoolLms.Domain;

// ===========================================================================
//  "Taklif va shikoyatlar" as a workflow (client, 2026-10-07):
//    * three statuses the sender sees: qabul qilindi → jarayonda → hal qilindi;
//    * the sender finds what they sent earlier in their own app (Mini App);
//    * an item goes to a department (bo'lim) and a responsible person, who is told by the bot.
//
//  Additive to the original `feedbacks` table: its "new" status stays "new" (shown as "Qabul
//  qilindi"), so old rows and the admin bell keep working. Telegram is the only channel.
// ===========================================================================

/// <summary>Feedback statuses — the three the client named, in order.</summary>
public static class FeedbackStatus
{
    /// <summary>Qabul qilindi — the school has it (every new item starts here).</summary>
    public const string New = "new";
    /// <summary>Jarayonda — someone is working on it.</summary>
    public const string InProgress = "in_progress";
    /// <summary>Hal qilindi — closed, usually with an answer to the sender.</summary>
    public const string Resolved = "resolved";

    public static readonly IReadOnlyList<string> All = [New, InProgress, Resolved];

    public static string Label(string status) => status switch
    {
        New => "Qabul qilindi",
        InProgress => "Jarayonda",
        Resolved => "Hal qilindi",
        _ => status,
    };
}

/// <summary>
/// A department an item can be routed to (O'quv bo'limi, Moliya, Ovqatlanish …). Not deleted —
/// switched off, so old items keep their department name.
/// </summary>
public class FeedbackDepartment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>List order (smaller first).</summary>
    public int Position { get; set; }
    /// <summary>false — not offered to senders any more.</summary>
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A responsible person of a department (users.id) — told by the bot about new items.</summary>
public class FeedbackDepartmentMember
{
    public Guid DepartmentId { get; set; }
    public string UserId { get; set; } = string.Empty;
}

/// <summary>
/// One step in an item's history: status change, routing, or a note. Append-only — the history is
/// what the sender and the school can point at later.
/// </summary>
public class FeedbackEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FeedbackId { get; set; } = string.Empty;
    /// <summary>The status after this step (null = the status did not change).</summary>
    public string? Status { get; set; }
    /// <summary>Routed to this department (null = unchanged).</summary>
    public Guid? DepartmentId { get; set; }
    /// <summary>Assigned to this person (users.id; null = unchanged).</summary>
    public string? AssigneeId { get; set; }
    /// <summary>Comment. Shown to the sender only when <see cref="IsPublic"/> (the answer).</summary>
    public string? Note { get; set; }
    public bool IsPublic { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
