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
//  Not to be confused with `Survey` — that is the public enrolment form of the leads funnel.
// ===========================================================================

public static class PollAudience
{
    public const string Parents = "parents";
    public const string Teachers = "teachers";
    public static bool IsValid(string? v) => v is Parents or Teachers;
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
    /// <summary>One of <see cref="PollQuestion.Options"/>.</summary>
    public const string Choice = "choice";
    public const string Text = "text";
    public static bool IsValid(string? v) => v is Nps or Stars or Choice or Text;
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
    public List<PollQuestion> Questions { get; set; } = [];
}

public class PollQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PollId { get; set; }
    public int Position { get; set; }
    public string Kind { get; set; } = PollQuestionKind.Nps;
    public string Text { get; set; } = string.Empty;
    /// <summary>Choice options, one per line.</summary>
    public string? Options { get; set; }
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
    public int? Score { get; set; }
    public string? Choice { get; set; }
    public string? Text { get; set; }
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
