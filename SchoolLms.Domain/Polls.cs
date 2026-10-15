namespace SchoolLms.Domain;

// ===========================================================================
//  So'rovnomalar va NPS (client, 2026-10-07: "ota-onalar va o'qituvchilar shaxsiy kabinetiga
//  so'rovnoma olishni kiritib ketish va NPS ko'rsatkichini o'lchay olish — hodimlar va maktab
//  rahbariyatining samaradorlik ko'rsatkichini o'lchash").
//
//  A poll goes to one audience (parents or teachers) through the Telegram Mini App. A question
//  rates either the school as a whole, each teacher of the parent's child, or each member of the
//  management (HR director/manager) — the last two give per-person NPS for the KPI board.
//
//  Anonymous polls (the default) keep no respondent id at all: the response row holds only a
//  hash of (poll, respondent) so the same person cannot answer twice, plus the child's class for
//  the class breakdown.
//
//  Google Forms parity (client, 2026-10-15: "So'rovnoma olish — so'rovnoma Google form holatda
//  bo'lishi"): more question kinds (checkboxes, dropdown, linear scale, number, date, short text,
//  sections), and a third audience — `public`, anyone with the link (`/sorovnoma/{token}`). A
//  parents/teachers poll can be opened on the link too (`PublicLink`). docs/modules/surveys-forms.md.
//
//  Not to be confused with `Survey` — that is the public enrolment form of the leads funnel.
// ===========================================================================

public static class PollAudience
{
    public const string Parents = "parents";
    public const string Teachers = "teachers";
    /// <summary>Anyone with the link — answered only on the public page, never in the Mini App.</summary>
    public const string Public = "public";
    public static bool IsValid(string? v) => v is Parents or Teachers or Public;
}

public static class PollStatus
{
    public const string Draft = "draft";
    public const string Open = "open";
    public const string Closed = "closed";
}

public static class PollQuestionKind
{
    /// <summary>0–10, "would you recommend" — promoters 9–10, detractors 0–6.</summary>
    public const string Nps = "nps";
    /// <summary>1–5 stars.</summary>
    public const string Stars = "stars";
    /// <summary>One of <see cref="PollQuestion.Options"/> (or the "Boshqa" text when allowed).</summary>
    public const string Choice = "choice";
    /// <summary>Paragraph — free text up to 2000 characters.</summary>
    public const string Text = "text";
    /// <summary>Checkboxes: any number of the options, optionally bounded by Min/MaxChoices.</summary>
    public const string Multi = "multi";
    /// <summary>One of the options, shown as a select (long lists).</summary>
    public const string Dropdown = "dropdown";
    /// <summary>One line of text.</summary>
    public const string Short = "short";
    /// <summary>Linear scale MinValue (0 or 1) .. MaxValue (2..10), stored in <see cref="PollAnswer.Score"/>.</summary>
    public const string Scale = "scale";
    /// <summary>A number, optionally within MinValue..MaxValue.</summary>
    public const string Number = "number";
    public const string Date = "date";
    /// <summary>Not a question: a titled block that splits the form into parts. Never answered.</summary>
    public const string Section = "section";

    public static bool IsValid(string? v) =>
        v is Nps or Stars or Choice or Text or Multi or Dropdown or Short or Scale or Number or Date or Section;

    /// <summary>Kinds whose answer is one or more of the question's options.</summary>
    public static bool HasOptions(string? v) => v is Choice or Multi or Dropdown;
}

/// <summary>What a question rates.</summary>
public static class PollTarget
{
    public const string School = "school";
    /// <summary>Each teacher of the parent's child (parents only).</summary>
    public const string Teachers = "teachers";
    /// <summary>Each HR director/manager (teachers only).</summary>
    public const string Management = "management";
    public static bool IsValid(string? v) => v is School or Teachers or Management;
}

public class Poll
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Audience { get; set; } = PollAudience.Parents;
    public bool Anonymous { get; set; } = true;
    public string Status { get; set; } = PollStatus.Draft;
    /// <summary>Last day answers are taken (inclusive); null — until closed by hand.</summary>
    public DateOnly? ClosesOn { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>When the bot announcement went out (once).</summary>
    public DateTimeOffset? NotifiedAt { get; set; }
    /// <summary>
    /// The unguessable part of the public link <c>/sorovnoma/{token}</c>. Given to every poll created
    /// from 2026-10-15; an older poll gets one the first time it is saved.
    /// </summary>
    public string? PublicToken { get; set; }
    /// <summary>
    /// "Havola orqali ham ochiq": a parents/teachers poll may be answered on the public link too.
    /// Always true for <see cref="PollAudience.Public"/>.
    /// </summary>
    public bool PublicLink { get; set; }
    /// <summary>
    /// The public page asks for a name and a phone (never on an anonymous poll). Takes effect only
    /// while the link is open; kept as set when the link is switched off.
    /// </summary>
    public bool CollectContact { get; set; }
    public List<PollQuestion> Questions { get; set; } = [];
}

public class PollQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PollId { get; set; }
    public int Position { get; set; }
    public string Kind { get; set; } = PollQuestionKind.Nps;
    /// <summary>The question — or the section's title.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Help text under the question — or the section's description.</summary>
    public string? Description { get; set; }
    /// <summary>Choice / multi / dropdown options, one per line.</summary>
    public string? Options { get; set; }
    /// <summary>Choice and multi: a "Boshqa: ___" option with the respondent's own text.</summary>
    public bool AllowOther { get; set; }
    /// <summary>Scale: the lowest point (0 or 1). Number: the smallest allowed answer, if any.</summary>
    public decimal? MinValue { get; set; }
    /// <summary>Scale: the highest point (2..10). Number: the largest allowed answer, if any.</summary>
    public decimal? MaxValue { get; set; }
    /// <summary>Scale: the words under the two ends ("Yomon" … "A'lo").</summary>
    public string? MinLabel { get; set; }
    public string? MaxLabel { get; set; }
    /// <summary>Multi: at least / at most this many ticks, when answered.</summary>
    public int? MinChoices { get; set; }
    public int? MaxChoices { get; set; }
    public bool Required { get; set; } = true;
    public string Target { get; set; } = PollTarget.School;
}

public class PollResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PollId { get; set; }
    /// <summary>"student:{id}" / "teacher:{id}" — or its SHA-256 (with the poll id) when anonymous.</summary>
    public string RespondentKey { get; set; } = string.Empty;
    /// <summary>Null on anonymous polls.</summary>
    public string? StudentId { get; set; }
    /// <summary>Null on anonymous polls.</summary>
    public string? TeacherId { get; set; }
    /// <summary>The child's class at submit time — the class breakdown (kept on anonymous polls too).</summary>
    public string? ClassName { get; set; }
    /// <summary>Submitted on the public page; the key is then a hash of the browser's token.</summary>
    public bool ViaLink { get; set; }
    /// <summary>Name and phone (digits, "998901234567") typed on the public page — only when the poll asks.</summary>
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public List<PollAnswer> Answers { get; set; } = [];
}

public class PollAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResponseId { get; set; }
    public Guid QuestionId { get; set; }
    /// <summary>The rated person: a teacher id (target teachers) or an HR employee id (management).</summary>
    public string? SubjectId { get; set; }
    /// <summary>NPS, stars, scale.</summary>
    public int? Score { get; set; }
    /// <summary>Choice, dropdown.</summary>
    public string? Choice { get; set; }
    /// <summary>Multi: the ticked options, in the question's order.</summary>
    public List<string>? Choices { get; set; }
    /// <summary>Text and short answers — and the "Boshqa" text of a choice/multi answer.</summary>
    public string? Text { get; set; }
    public decimal? Number { get; set; }
    public DateOnly? Date { get; set; }
}

/// <summary>KPI weights (one row). A component with weight 0 is left out of the total.</summary>
public class KpiSettings
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public int WeightTasks { get; set; } = 25;
    public int WeightLessonAnalysis { get; set; } = 25;
    public int WeightJournal { get; set; } = 10;
    public int WeightParentNps { get; set; } = 15;
    public int WeightStaffNps { get; set; } = 10;
    public int WeightPenalties { get; set; } = 10;
    public int WeightAttendance { get; set; } = 5;
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
