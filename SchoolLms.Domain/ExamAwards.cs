namespace SchoolLms.Domain;

/// <summary>
/// A thank-you (top) or warning (bottom) message sent to a pupil's account after a test (client, 2026-10-15:
/// "Oylik testni eng top va bottom o'quvchilarining accountiga xabar jo'natish (tashakkurnoma diplom /
/// ogohlantirish) — nechta o'quvchiga jo'natish sonini qo'lda kiritishli qilish"). One row per exam, pupil and
/// kind — the same pupil is never sent the same kind twice for one exam. The message itself is a
/// <see cref="Broadcast"/> (so it sits in the pupil's "Maktab e'lonlari" feed and in Xabarlar's register with
/// who got it); this row ties it to the exam. docs/modules/exam-awards.md.
/// </summary>
public class ExamAwardNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ExamId { get; set; } = string.Empty;

    public string StudentId { get; set; } = string.Empty;

    /// <summary><see cref="ExamAwardKinds"/>: top | bottom.</summary>
    public string Kind { get; set; } = ExamAwardKinds.Top;

    /// <summary>Place at sending: 1 = best for top; for bottom 1 = lowest. Within the class when ranked per class.</summary>
    public int Rank { get; set; }

    public decimal? Percent { get; set; }

    /// <summary>broadcasts.id of the message.</summary>
    public string BroadcastId { get; set; } = string.Empty;

    /// <summary>Telegram chats it reached (0 — none: not connected or every send failed).</summary>
    public int Delivered { get; set; }

    /// <summary>A thank-you PDF went with it.</summary>
    public bool WithDiploma { get; set; }

    /// <summary>users.id of the sender.</summary>
    public string SentBy { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }
}

public static class ExamAwardKinds
{
    public const string Top = "top";
    public const string Bottom = "bottom";

    public static readonly IReadOnlyList<string> All = [Top, Bottom];
}
