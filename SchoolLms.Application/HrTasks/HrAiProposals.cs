using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  The model's answer → a proposal the code trusts (docs/modules/hr-ai-tasks.md §4.4), and the
//  card the director decides on. Nothing here writes: approval goes through HrTasksService.
// ===========================================================================

/// <summary>A resolved employee or position (id from our tables, name for the card).</summary>
public sealed record HrAiPerson(string Id, string Name, string? Position = null);

/// <summary>One task (or recurring rule) of a proposal.</summary>
/// <param name="Due">One-time: school wall-clock time, <c>yyyy-MM-ddTHH:mm</c>.</param>
/// <param name="Position">Recurring only: the whole position instead of <paramref name="Employees"/>.</param>
public sealed record HrAiProposalItem(
    string Title, string? Description, bool Recurring,
    List<HrAiPerson> Employees, HrAiPerson? Position,
    string? Due,
    string? Recurrence, List<int> Weekdays, List<int> DaysOfMonth, string? DueTime,
    string Kind, decimal? TargetAmount, string? Unit);

/// <summary>What is stored in <c>hr_ai_drafts.proposal</c>.</summary>
public sealed record HrAiProposal(string? Transcript, List<HrAiProposalItem> Items);

/// <summary>Either a proposal for the card, or a question / hint for the director.</summary>
public sealed record HrAiOutcome(HrAiProposal? Proposal, string? Question, string? Transcript);

public static class HrAiProposals
{
    public const int MaxItems = 10;

    /// <summary>A due time this far in the past is still accepted (the director spoke a minute ago).</summary>
    private static readonly TimeSpan PastGrace = TimeSpan.FromMinutes(5);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly string[] DueFormats =
        ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

    /// <summary>
    /// Reads the model's JSON against the refs that were sent. Any doubt — an unknown ref, a past
    /// due, a broken schedule — becomes a question, never a guess.
    /// </summary>
    public static HrAiOutcome Read(
        string json, IReadOnlyDictionary<string, HrAiPerson> employees, IReadOnlyDictionary<string, HrAiPerson> positions,
        DateTime localNow)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Ask(HrAiTexts.NotUnderstood, null);
        }
        if (root.ValueKind != JsonValueKind.Object) return Ask(HrAiTexts.NotUnderstood, null);

        var transcript = Text(root, "transcript");
        var status = Text(root, "status") ?? "ok";
        if (status == "not_task") return Ask(HrAiTexts.NotTaskHint, transcript);
        if (status != "ok") return Ask(Text(root, "question") ?? HrAiTexts.NeedMore, transcript);

        if (!root.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array || tasks.GetArrayLength() == 0)
            return Ask(HrAiTexts.NeedMore, transcript);
        if (tasks.GetArrayLength() > MaxItems)
            return Ask($"Bir xabarda ko'pi bilan {MaxItems} ta topshiriq — bo'lib yuboring.", transcript);

        var items = new List<HrAiProposalItem>();
        var n = 0;
        foreach (var t in tasks.EnumerateArray())
        {
            n++;
            var (item, problem) = ReadItem(t, employees, positions, localNow);
            if (problem is not null) return Ask(tasks.GetArrayLength() > 1 ? $"{n}-topshiriq: {problem}" : problem, transcript);
            items.Add(item!);
        }
        return new HrAiOutcome(new HrAiProposal(transcript, items), null, transcript);
    }

    private static HrAiOutcome Ask(string question, string? transcript) => new(null, question, transcript);

    private static (HrAiProposalItem? Item, string? Problem) ReadItem(
        JsonElement t, IReadOnlyDictionary<string, HrAiPerson> employees, IReadOnlyDictionary<string, HrAiPerson> positions,
        DateTime localNow)
    {
        if (t.ValueKind != JsonValueKind.Object) return (null, HrAiTexts.NotUnderstood);

        var title = Clean(Text(t, "title"), HrLimits.TitleMax);
        if (title is null || title.Length < 2) return (null, "Topshiriq nima ekanini aniqroq ayting.");
        var description = Clean(Text(t, "description"), HrLimits.DescriptionMax);
        var recurring = t.TryGetProperty("recurring", out var rec) && rec.ValueKind == JsonValueKind.True;

        var people = new List<HrAiPerson>();
        foreach (var r in Strings(t, "employees"))
        {
            if (!employees.TryGetValue(r, out var person)) return (null, "Xodim ro'yxatda topilmadi — ismini aniqroq ayting.");
            if (people.All(p => p.Id != person.Id)) people.Add(person);
        }
        if (people.Count > HrLimits.MaxBatchEmployees)
            return (null, $"Bir topshiriq ko'pi bilan {HrLimits.MaxBatchEmployees} xodimga.");
        HrAiPerson? position = null;
        if (Text(t, "position") is { } posRef)
        {
            if (!positions.TryGetValue(posRef, out position)) return (null, "Lavozim topilmadi — aniqroq ayting.");
        }

        var kind = Text(t, "kind") == HrTaskKinds.Numeric ? HrTaskKinds.Numeric : HrTaskKinds.Check;
        decimal? target = null;
        string? unit = null;
        if (kind == HrTaskKinds.Numeric)
        {
            target = t.TryGetProperty("target_amount", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out var d)
                ? d : null;
            if (target is not > 0) return (null, "Raqamli topshiriq uchun nechta (miqdor) ekanini ayting.");
            unit = Clean(Text(t, "unit"), HrLimits.UnitMax);
        }

        if (!recurring)
        {
            if (position is not null && people.Count == 0)
                return (null, "Bir martalik topshiriq lavozimga emas, aniq xodimga beriladi — kimga?");
            if (people.Count == 0) return (null, "Topshiriq kimga berilishini ayting.");
            if (!TryDue(Text(t, "due"), out var due)) return (null, "Muddatni aniqroq ayting (sana va soat).");
            if (due < localNow - PastGrace)
                return (null, $"Muddat o'tib ketgan ({due:dd.MM.yyyy HH:mm}) — boshqa vaqt ayting.");
            return (new HrAiProposalItem(title, description, false, people, null,
                due.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
                null, [], [], null, kind, target, unit), null);
        }

        if (people.Count > 0 && position is not null) position = null; // named people win over a position
        if (people.Count == 0 && position is null) return (null, "Takroriy topshiriq kimga (yoki qaysi lavozimga)?");
        var recurrence = Text(t, "recurrence") ?? "";
        int[] week, month;
        try
        {
            (week, month) = HrRecurrenceRules.Validate(recurrence, Ints(t, "weekdays"), Ints(t, "days_of_month"));
        }
        catch (HrTasksException ex)
        {
            return (null, $"Qachon takrorlanishini aniqroq ayting ({ex.Message}).");
        }
        string? dueTime = null;
        if (Text(t, "due_time") is { } rawTime)
        {
            if (!TimeOnly.TryParseExact(rawTime, ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return (null, "Soatni aniqroq ayting.");
            dueTime = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        return (new HrAiProposalItem(title, description, true, people, position, null,
            recurrence, [.. week], [.. month], dueTime, kind, target, unit), null);
    }

    private static bool TryDue(string? raw, out DateTime due)
    {
        due = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim();
        if (DateTime.TryParseExact(text, DueFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out due)) return true;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            due = day.ToDateTime(new TimeOnly(23, 59));
            return true;
        }
        return false;
    }

    /// <summary>The proposal back in the model's own shape (with the refs of this request) — for a revision.</summary>
    public static string ToAiJson(
        HrAiProposal p, IReadOnlyDictionary<string, string> employeeRefs, IReadOnlyDictionary<string, string> positionRefs) =>
        JsonSerializer.Serialize(new
        {
            transcript = (string?)null,
            status = "ok",
            question = (string?)null,
            tasks = p.Items.Select(i => new
            {
                employees = i.Employees.Select(e => employeeRefs.GetValueOrDefault(e.Id)).Where(r => r is not null).ToList(),
                position = i.Position is null ? null : positionRefs.GetValueOrDefault(i.Position.Id),
                title = i.Title,
                description = i.Description,
                recurring = i.Recurring,
                due = i.Due,
                recurrence = i.Recurrence,
                weekdays = i.Weekdays,
                days_of_month = i.DaysOfMonth,
                due_time = i.DueTime,
                kind = i.Kind,
                target_amount = i.TargetAmount,
                unit = i.Unit,
            }),
        });

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim() : null;

    private static IEnumerable<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0)
            : [];

    private static List<int> Ints(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32())]
            : [];

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}

/// <summary>Bot texts of the AI task flow (Uzbek, Telegram HTML; user text goes through Escape).</summary>
public static class HrAiTexts
{
    public const string AiOff =
        "🤖 AI orqali topshiriq yaratish hali yoqilmagan (Gemini kaliti kiritilmagan). Hozircha topshiriqni panel orqali bering.";
    public const string NotTaskHint =
        "🤖 Men xodimlarga topshiriq berishga yordam beraman. Masalan: «Alisherga ertaga soat 10 gacha kassa hisobotini " +
        "topshirsin» yoki «har juma kechqurun buxgalter moliya hisobotini tekshirsin». Yozing yoki ovozli xabar yuboring.";
    public const string NotUnderstood = "AI javobini tushunib bo'lmadi — qaytadan yozing.";
    public const string NeedMore = "Aniqroq ayting: kimga va qanday topshiriq?";
    public const string AiFailed = "🤖 AI hozir javob bermadi — birozdan keyin qayta urinib ko'ring.";
    public const string TooMany = "🤖 Juda ko'p so'rov — bir soatdan keyin davom eting yoki panel orqali bering.";
    public const string VoiceFailed = "🎙 Ovozli xabarni yuklab bo'lmadi — qaytadan yuboring yoki yozib yuboring.";
    public const string EditPrompt = "✏️ Nimani o'zgartiramiz? Yozing yoki ovozli xabar yuboring.";
    public const string NotYours = "Bu tugma siz uchun emas.";
    public const string AlreadyDecided = "Bu loyiha allaqachon ko'rib chiqilgan.";
    public const string ExpiredToast = "Muddati o'tgan — qaytadan yozing.";

    public static string VoiceTooLong(int max) => $"🎙 Ovozli xabar {max / 60} daqiqadan oshmasin.";

    /// <summary>"🤔 …" — the AI's question (or our validation message) for the director.</summary>
    public static string Question(string question, string? transcript) =>
        (transcript is null ? "" : $"🎙 <i>Siz aytdingiz:</i> «{HrTasksTexts.Escape(Short(transcript, 600))}»\n\n")
        + "🤔 " + HrTasksTexts.Escape(question);

    /// <summary>Names listed on the card per task; the rest are counted ("va yana 12 kishi").</summary>
    private const int NamesShown = 8;

    /// <summary>
    /// The card stays under Telegram's 4096 characters even with the decision appended (≤ ~1 200: five
    /// errors of 200); items that do not fit are counted.
    /// </summary>
    private const int CardBudget = 2800;

    public static string Card(HrAiProposal p, string source)
    {
        var sb = new StringBuilder("🤖 <b>Topshiriq loyihasi</b>\n");
        if (source == HrAiSources.Voice && !string.IsNullOrWhiteSpace(p.Transcript))
            sb.Append("🎙 <i>Siz aytdingiz:</i> «").Append(HrTasksTexts.Escape(Short(p.Transcript, 800))).Append("»\n");
        var n = 0;
        foreach (var i in p.Items)
        {
            var item = Item(++n, i);
            if (sb.Length + item.Length > CardBudget)
            {
                sb.Append($"\n… va yana {p.Items.Count - n + 1} ta topshiriq (hammasi tasdiqlansa yaratiladi)\n");
                break;
            }
            sb.Append(item);
        }
        sb.Append("\nTasdiqlasangiz, topshiriq xodimga darhol yuboriladi.");
        return sb.ToString();
    }

    private static string Item(int n, HrAiProposalItem i)
    {
        var sb = new StringBuilder();
        sb.Append('\n').Append(n).Append(". <b>").Append(HrTasksTexts.Escape(i.Title)).Append("</b>\n");
        string who;
        if (i.Position is not null) who = $"🏷 Lavozim: {HrTasksTexts.Escape(i.Position.Name)} (barcha xodimlar)";
        else
        {
            who = "👤 " + string.Join(", ", i.Employees.Take(NamesShown).Select(e =>
                HrTasksTexts.Escape(e.Name) + (string.IsNullOrWhiteSpace(e.Position) ? "" : $" ({HrTasksTexts.Escape(e.Position)})")));
            if (i.Employees.Count > NamesShown) who += $" va yana {i.Employees.Count - NamesShown} kishi";
        }
        sb.Append("   ").Append(who).Append('\n');
        sb.Append("   ").Append(i.Recurring ? "🔁 " + Schedule(i) : "📅 " + DueText(i.Due) + " · bir martalik").Append('\n');
        if (i.Kind == HrTaskKinds.Numeric && i.TargetAmount is { } amount)
            sb.Append("   📏 Maqsad: ").Append(amount.ToString("0.##", CultureInfo.InvariantCulture))
              .Append(string.IsNullOrWhiteSpace(i.Unit) ? "" : " " + HrTasksTexts.Escape(i.Unit)).Append('\n');
        if (!string.IsNullOrWhiteSpace(i.Description))
            sb.Append("   📝 ").Append(HrTasksTexts.Escape(Short(i.Description, 300))).Append('\n');
        return sb.ToString();
    }

    /// <summary>The card after a decision: the proposal, then what happened — no buttons.</summary>
    public static string Decided(HrAiProposal p, string source, string outcome) =>
        Card(p, source).Replace("\nTasdiqlasangiz, topshiriq xodimga darhol yuboriladi.", "") + "\n" + outcome;

    public static string ApprovedLine(int tasks, int templates, IReadOnlyList<string> errors)
    {
        var made = new List<string>();
        if (tasks > 0) made.Add($"{tasks} ta topshiriq");
        if (templates > 0) made.Add($"{templates} ta takroriy topshiriq");
        var head = made.Count > 0 ? $"✅ <b>Tasdiqlandi</b>: {string.Join(", ", made)} yaratildi." : "⚠️ <b>Hech narsa yaratilmadi.</b>";
        if (errors.Count == 0) return head;
        var shown = errors.Take(5).Select(e => "• " + HrTasksTexts.Escape(Short(e, 200)));
        return head + "\n" + string.Join("\n", shown) + (errors.Count > 5 ? $"\n… va yana {errors.Count - 5} ta xato" : "");
    }

    public const string CancelledLine = "❌ <b>Bekor qilindi.</b>";
    public const string ExpiredLine = "⌛ <b>Muddati o'tgan.</b>";
    public const string ReplacedLine = "✏️ <b>O'zgartirildi</b> — yangi loyiha pastda.";
    public const string EditingLine = "✏️ <b>O'zgartirilmoqda</b> — tuzatishni yozing yoki ovozli xabar yuboring.";

    public static object Keyboard(string draftId) => new
    {
        inline_keyboard = new[]
        {
            new object[]
            {
                new { text = "✅ Tasdiqlash", callback_data = $"hai:ok:{draftId}" },
                new { text = "✏️ O'zgartirish", callback_data = $"hai:ed:{draftId}" },
                new { text = "❌ Bekor", callback_data = $"hai:no:{draftId}" },
            },
        },
    };

    private static string Schedule(HrAiProposalItem i)
    {
        var at = i.DueTime is null ? "kun oxirigacha" : $"soat {i.DueTime}";
        var days = string.Join(", ", i.Weekdays.Select(d => HrAiPrompt.WeekdayNames[d - 1]));
        return i.Recurrence switch
        {
            HrRecurrences.Monthly => $"Har oy: {string.Join(", ", i.DaysOfMonth)}-sanalarda, {at}",
            HrRecurrences.Daily when i.Weekdays.Count == 7 => $"Har kuni, {at}",
            HrRecurrences.Daily => $"Har kuni ({days}), {at}",
            _ => $"Har hafta: {days}, {at}",
        };
    }

    private static string DueText(string? due) =>
        DateTime.TryParseExact(due, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture) : due ?? "";

    private static string Short(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
