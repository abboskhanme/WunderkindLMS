using System.Globalization;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  Telegram texts — ported VERBATIM from the old WunderkindHR
//  infrastructure/digest.py (evening digest, morning list),
//  infrastructure/notifier.py (new / returned task) and
//  application/use_cases/send_reminders.py (deadline reminder).
//
//  Pure: input → text, no SQL, no network — unit-tested without a bot.
//  parse_mode = HTML, so EVERY user-typed string (title, comment, position,
//  unit, name) goes through Escape(); otherwise one "<b>bajardim</b>" comment
//  breaks the whole digest ("can't parse entities") and the director gets
//  nothing. A message is at most 4096 characters; the digest splits on
//  EMPLOYEE boundaries.
// ===========================================================================

/// <summary>One task line of a digest / morning list (old DigestTaskDTO).</summary>
public sealed record HrDigestTask(
    string Id, string Title, string Status, string Kind, bool IsLate, DateTimeOffset DueAt,
    string? EmployeeComment, int ProofCount, decimal? TargetAmount, decimal CurrentAmount, string? Unit);

/// <summary>One employee's block (old DigestEmployeeDTO).</summary>
public sealed record HrDigestEmployee(
    string EmployeeId, string FullName, string? PositionName, string UserId,
    IReadOnlyList<HrDigestTask> Done, IReadOnlyList<HrDigestTask> NotDone, IReadOnlyList<HrDigestTask> OverdueOpen)
{
    public bool IsEmpty => Done.Count == 0 && NotDone.Count == 0 && OverdueOpen.Count == 0;
}

/// <summary>One day's digest (old DigestDTO).</summary>
public sealed record HrDigest(DateOnly Day, IReadOnlyList<HrDigestEmployee> Employees, int SelfCreatedCount, int UnacceptedCount = 0)
{
    public int DoneCount => Employees.Sum(e => e.Done.Count);
    public int NotDoneCount => Employees.Sum(e => e.NotDone.Count);
    public int TotalCount => DoneCount + NotDoneCount;
    /// <summary>An empty digest is never sent.</summary>
    public bool IsEmpty => Employees.All(e => e.IsEmpty);
}

public static class HrTasksTexts
{
    public const int TelegramMaxLen = 4096;
    public const int MaxMorningLines = 15;
    public const int MaxReminderLines = 10;
    private const int Percent = 100;
    private const int MinutesInDay = 1440;

    private const string IconDone = "✅";
    private const string IconMissed = "❌";
    private const string IconOpen = "⏳";
    private const string IconOverdue = "⚠️";
    private const string IconRed = "🔴";
    private const string IconLate = "⏰";
    private const string IconProof = "📎";
    private const string IconEmployee = "👤";
    private const string IconUnaccepted = "👀";

    public const string VoiceCaption = "🎧 Ovozli izoh";

    /// <summary>Telegram-HTML-safe user text (html.escape(quote=False)).</summary>
    public static string Escape(string? value) =>
        (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary><c>147.00</c> → <c>147</c>, <c>12.50</c> → <c>12.5</c>.</summary>
    public static string Num(decimal? value)
    {
        if (value is null) return "0";
        // no trailing zeros, no exponent: 147.00 → "147", 12.50 → "12.5"
        return value.Value.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private static string Kpi(HrDigestTask task)
    {
        var parts = $"{Num(task.CurrentAmount)}/{Num(task.TargetAmount)}";
        if (!string.IsNullOrEmpty(task.Unit)) parts = $"{parts} {Escape(task.Unit)}";
        if (task.TargetAmount is { } target && target > 0)
            parts = $"{parts} ({(int)(task.CurrentAmount * Percent / target)}%)";
        return parts;
    }

    private static string Title(HrDigestTask task)
    {
        var title = Escape(task.Title);
        return task.Kind == HrTaskKinds.Numeric && task.TargetAmount is not null ? $"{title} — {Kpi(task)}" : title;
    }

    private static string When(DateTimeOffset moment, TimeZoneInfo tz) =>
        HrValues.Local(moment, tz).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);

    private static string DoneLine(HrDigestTask task)
    {
        var extras = new List<string>();
        if (!string.IsNullOrEmpty(task.EmployeeComment)) extras.Add($"izoh: {Escape(task.EmployeeComment)}");
        if (task.ProofCount > 0) extras.Add($"{IconProof} {task.ProofCount} ta isbot");
        if (task.IsLate) extras.Add($"{IconLate} kechikib");
        var suffix = extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "";
        return $"   {IconDone} {Title(task)}{suffix}";
    }

    private static string NotDoneLine(HrDigestTask task, TimeZoneInfo tz)
    {
        if (task.Status == HrTaskStatuses.Missed) return $"   {IconMissed} {Title(task)} — bajarilmadi";
        var state = task.Status == HrTaskStatuses.Returned ? "qaytarilgan" : "ochiq";
        return $"   {IconOpen} {Title(task)} — muddat {When(task.DueAt, tz)}, {state}";
    }

    private static string OverdueLine(HrDigestTask task, TimeZoneInfo tz)
    {
        var state = task.Status == HrTaskStatuses.Returned ? "qaytarilgan" : "ochiq";
        return $"   {IconOverdue} {Title(task)} — muddat {When(task.DueAt, tz)} o'tdi, {state}";
    }

    private static string EmployeeBlock(HrDigestEmployee employee, TimeZoneInfo tz)
    {
        var head = $"{IconEmployee} <b>{Escape(employee.FullName)}</b>";
        if (!string.IsNullOrEmpty(employee.PositionName)) head = $"{head} — {Escape(employee.PositionName)}";
        var lines = new List<string> { head };
        lines.AddRange(employee.Done.Select(DoneLine));
        lines.AddRange(employee.NotDone.Select(t => NotDoneLine(t, tz)));
        lines.AddRange(employee.OverdueOpen.Select(t => OverdueLine(t, tz)));
        return string.Join("\n", lines);
    }

    private static string Header(HrDigest digest)
    {
        var done = digest.DoneCount;
        var total = digest.TotalCount;
        var percent = total > 0 ? done * Percent / total : 0;
        return $"📊 <b>{digest.Day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} — kunlik yakun</b>\n\n"
             + $"{IconDone} Bajarilgan: {done} / {total} ({percent}%)\n";
    }

    private static string Footer(HrDigest digest)
    {
        var overdue = digest.Employees.Sum(e => e.OverdueOpen.Count);
        var unaccepted = Math.Max(digest.UnacceptedCount, 0);
        var lines = new List<string> { "" };
        if (digest.SelfCreatedCount > 0) lines.Add($"➕ O'zi qo'shgan topshiriqlar: {digest.SelfCreatedCount} ta");
        if (overdue > 0) lines.Add($"{IconOverdue} Muddati o'tganlar: {overdue} ta");
        if (unaccepted > 0) lines.Add($"{IconUnaccepted} Tasdiqlanmagan: {unaccepted} ta");
        return lines.Count > 1 ? string.Join("\n", lines) : "";
    }

    /// <summary>A block longer than the limit is split by lines (fallback, not expected).</summary>
    private static List<string> SplitLong(string block, int limit)
    {
        var pieces = new List<string>();
        var current = new List<string>();
        var size = 0;
        foreach (var line in block.Split('\n'))
        {
            var chunk = line.Length > limit ? line[..limit] : line;
            var extra = chunk.Length + (current.Count > 0 ? 1 : 0);
            if (current.Count > 0 && size + extra > limit)
            {
                pieces.Add(string.Join("\n", current));
                current = [];
                size = 0;
                extra = chunk.Length;
            }
            current.Add(chunk);
            size += extra;
        }
        if (current.Count > 0) pieces.Add(string.Join("\n", current));
        return pieces.Count > 0 ? pieces : [""];
    }

    /// <summary>Packs blocks into messages ≤ <paramref name="limit"/>, never splitting a block.</summary>
    private static IReadOnlyList<string> Pack(IEnumerable<string> blocks, int limit)
    {
        var expanded = new List<string>();
        foreach (var block in blocks)
            expanded.AddRange(block.Length > limit ? SplitLong(block, limit) : [block]);

        var messages = new List<string>();
        var current = new List<string>();
        var size = 0;
        foreach (var block in expanded)
        {
            var extra = block.Length + (current.Count > 0 ? 1 : 0);
            if (current.Count > 0 && size + extra > limit)
            {
                messages.Add(string.Join("\n", current));
                current = [];
                size = 0;
                extra = block.Length;
            }
            current.Add(block);
            size += extra;
        }
        if (current.Count > 0) messages.Add(string.Join("\n", current));
        return messages;
    }

    /// <summary>
    /// The evening digest — one or more Telegram messages. EMPTY list for an empty digest
    /// (the caller then sends nothing).
    /// </summary>
    public static IReadOnlyList<string> Digest(HrDigest digest, TimeZoneInfo tz, int limit = TelegramMaxLen)
    {
        if (digest.IsEmpty) return [];
        var blocks = new List<string> { Header(digest) };
        blocks.AddRange(digest.Employees.Where(e => !e.IsEmpty).Select(e => EmployeeBlock(e, tz)));
        var footer = Footer(digest);
        if (footer.Length > 0) blocks.Add(footer);
        return Pack(blocks, limit);
    }

    private static string OverdueFor(DateTimeOffset dueAt, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Floor((now - dueAt).TotalSeconds / 60));
        var days = minutes / MinutesInDay;
        var rest = minutes % MinutesInDay;
        if (days > 0) return $"{days} kun";
        var hours = rest / 60;
        var mins = rest % 60;
        return hours > 0 ? $"{hours} soat" : $"{mins} daqiqa";
    }

    private static IEnumerable<string> RestLine(int total)
    {
        var rest = total - MaxMorningLines;
        if (rest > 0) yield return $"…va yana {rest} ta";
    }

    /// <summary>
    /// The morning list: overdue tasks first in a red section (Telegram cannot colour text —
    /// 🔴 + bold + own section), then today's. Empty string = nothing to send.
    /// </summary>
    public static string Morning(IReadOnlyList<HrDigestTask> tasks, TimeZoneInfo tz, DateTimeOffset? now)
    {
        if (tasks.Count == 0) return "";
        var overdue = now is { } n1 ? tasks.Where(t => t.DueAt < n1).ToList() : [];
        var current = now is { } n2 ? tasks.Where(t => t.DueAt >= n2).ToList() : tasks.ToList();
        var lines = new List<string> { "🌅 <b>Bugungi topshiriqlar</b>", "" };
        var number = 1;
        if (overdue.Count > 0 && now is { } n)
        {
            lines.Add($"{IconRed} <b>MUDDATI O‘TGAN — {overdue.Count} ta</b>");
            foreach (var task in overdue.Take(MaxMorningLines))
            {
                lines.Add($"{IconRed} {number}. {Title(task)} — muddat {When(task.DueAt, tz)}, {OverdueFor(task.DueAt, n)} o‘tdi");
                number++;
            }
            lines.AddRange(RestLine(overdue.Count));
            if (current.Count > 0) lines.AddRange(["", $"{IconOpen} <b>Bugungi ishlar</b>"]);
        }
        foreach (var task in current.Take(MaxMorningLines))
        {
            lines.Add($"{number}. {Title(task)} — {When(task.DueAt, tz)}");
            number++;
        }
        lines.AddRange(RestLine(current.Count));
        return string.Join("\n", lines);
    }

    /// <summary>"2 soat", "1 soat 30 daqiqa", "30 daqiqa".</summary>
    public static string HumanOffset(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours > 0 && rest > 0) return $"{hours} soat {rest} daqiqa";
        return hours > 0 ? $"{hours} soat" : $"{rest} daqiqa";
    }

    /// <summary>Deadline reminder (old build_reminder_text): the nearest deadline leads.</summary>
    public static string Reminder(IReadOnlyList<(string Title, int RemainingMinutes)> items)
    {
        if (items.Count == 0) return "";
        var lead = items.Min(i => i.RemainingMinutes);
        var lines = new List<string> { $"⏰ {HumanOffset(lead)}dan keyin muddati tugaydi:" };
        var shown = items.Take(MaxReminderLines).ToList();
        lines.AddRange(shown.Select(i => $"• {Escape(i.Title)}"));
        var rest = items.Count - shown.Count;
        if (rest > 0) lines.Add($"…va yana {rest} ta");
        return string.Join("\n", lines);
    }

    /// <summary>A single hand-given task (old notifier._assigned_text).</summary>
    public static string Assigned(string title, DateTimeOffset dueAt, TimeZoneInfo tz) =>
        "🆕 <b>Sizga yangi topshiriq</b>\n\n"
        + $"{Escape(title)}\n"
        + $"⏰ Muddat: {HrValues.Local(dueAt, tz).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}\n\n"
        + "Bajargach «Topshiriqlarim» bo'limidan belgilang.";

    /// <summary>A batch — ONE message per employee (old notifier._batch_text).</summary>
    public static string AssignedMany(int count, IReadOnlyList<string> titles)
    {
        var head = count == 1
            ? "🆕 <b>Sizga yangi topshiriq berildi</b>"
            : $"🆕 <b>Sizga {count} ta yangi topshiriq berildi</b>";
        var lines = new List<string> { head, "" };
        var shown = titles.Take(HrLimits.MaxEventTitles).ToList();
        lines.AddRange(shown.Select((t, i) => $"{i + 1}. {Escape(t)}"));
        var rest = count - shown.Count;
        if (rest > 0) lines.Add($"…va yana {rest} ta");
        lines.AddRange(["", "Batafsil — «Topshiriqlarim» bo'limida."]);
        return string.Join("\n", lines);
    }

    /// <summary>A returned task (old notifier._returned_text).</summary>
    public static string Returned(string title, string? comment)
    {
        var note = string.IsNullOrEmpty(comment) ? "" : $"\n\n💬 {Escape(comment)}";
        return "↩️ <b>Topshiriq qaytarildi</b>\n\n" + $"{Escape(title)}{note}\n\n" + "Iltimos, qaytadan bajaring.";
    }
}
