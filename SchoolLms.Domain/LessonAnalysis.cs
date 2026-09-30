namespace SchoolLms.Domain;

// ===========================================================================
//  Dars tahlili — lesson observation checklist + teacher rating.
//  docs/modules/lesson-analysis.md. A 1:1 port of the retired
//  WunderkindEmployeesRating app (FastAPI); its rules are the reference.
// ===========================================================================

/// <summary>Who may use the module and the permission key a staff role carries.</summary>
public static class LessonAnalysisAccess
{
    /// <summary>
    /// Admin-panel permission key. A <see cref="Roles.Staff"/> account with it is an
    /// "observer" (the old MANAGER role); admin/superadmin always pass.
    /// </summary>
    public const string PermissionKey = "lessonAnalysis";

    /// <summary>
    /// Mezon qo'shish/tahrirlash — rolga ALOHIDA beriladigan belgi (Boshqaruv → Rollar, xuddi
    /// <see cref="McpAccess.PermissionKey"/> kabi: menyu sahifasi emas). Mijoz, 2026-09-29: akademik
    /// direktor roliga dars tahlili to'liq ochiq, lekin mezon qo'sha olmasdi — bu faqat admin/superadmin
    /// edi. ⚠️ <see cref="PermissionKey"/> o'zi mezonni OCHMAYDI: mavjud kuzatuvchilarning huquqi
    /// jimgina kengaymasin — mezonlar butun maktab tahlil varag'ini o'zgartiradi. Yozish uchun
    /// sinf darvozasi baribir to'liq <see cref="PermissionKey"/> ni ham talab qiladi.
    /// </summary>
    public const string CriteriaPermissionKey = "lessonAnalysisCriteria";
}

/// <summary><see cref="LessonObservation.Status"/> values (lowercase — API contract).</summary>
public static class LessonObservationStatus
{
    public const string Draft = "draft";
    public const string Completed = "completed";

    public static bool IsValid(string? value) => value is Draft or Completed;
}

/// <summary>A checklist section ("1-BO'LIM: DARSGA TAYYORGARLIK").</summary>
public class LessonObservationSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    /// <summary>Identity colour of the section, <c>#rrggbb</c>.</summary>
    public string Color { get; set; } = "#4b5563";
    /// <summary>Id in the old app — the one-time import keys on it (idempotent re-runs).</summary>
    public int? LegacyId { get; set; }

    public List<LessonObservationCriterion> Criteria { get; set; } = new();
}

/// <summary>
/// One checklist row. Never deleted — deactivated (<see cref="IsActive"/> = false), so
/// answers already given to it stay in history and keep counting in old scores.
/// </summary>
public class LessonObservationCriterion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SectionId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
    public int? LegacyId { get; set; }

    public LessonObservationSection? Section { get; set; }
}

/// <summary>
/// One observed lesson. Subject / class / lesson time are TEXT on purpose (old rule):
/// renaming a class or subject must never rewrite the history of past observations.
/// </summary>
public class LessonObservation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>users.id of the observer (author).</summary>
    public string ObserverUserId { get; set; } = string.Empty;
    public DateOnly LessonDate { get; set; }
    public string? LessonTime { get; set; }
    public string? ClassName { get; set; }
    public string? Subject { get; set; }
    /// <summary><see cref="LessonObservationStatus"/>.</summary>
    public string Status { get; set; } = LessonObservationStatus.Draft;
    public string? Strengths { get; set; }
    public string? GrowthAreas { get; set; }
    public string? NextLessonAdvice { get; set; }
    /// <summary>The text the AI model returned (a JSON object for new rows; plain text for imported ones).</summary>
    public string? AiSummary { get; set; }
    public string? AiError { get; set; }
    /// <summary>Answered details marked done.</summary>
    public int ScoreDone { get; set; }
    /// <summary>Answered details (is_done not null). Unmarked criteria are not scored.</summary>
    public int ScoreTotal { get; set; }
    /// <summary>round(done / total * 100, 2); 0 when nothing is answered. <c>numeric(5,2)</c>.</summary>
    public decimal ScorePercent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public int? LegacyId { get; set; }

    public List<LessonObservationDetail> Details { get; set; } = new();
}

/// <summary>An answer to one criterion. <see cref="IsDone"/> null = unmarked (not scored).</summary>
public class LessonObservationDetail
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ObservationId { get; set; } = string.Empty;
    public string CriterionId { get; set; } = string.Empty;
    public bool? IsDone { get; set; }
    public string? Comment { get; set; }
}
