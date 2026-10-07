using System.Text.Json.Serialization;

namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Dars tahlili — API contract (docs/modules/lesson-analysis.md §5).
//  Mirrors schoollms.client/src/types/lessonAnalysis.ts field for field:
//  camelCase (web JSON defaults), string ids, lowercase status, dates "yyyy-MM-dd".
// ===========================================================================

/// <summary><c>LaTeacher</c>.</summary>
public record LaTeacherDto(string Id, string FullName, string? Phone, string? Subject, string? PhotoUrl);

/// <summary><c>LaTeacherSummary</c> (<c>GET /teachers</c>, <c>GET /teachers/{id}</c>).</summary>
public record LaTeacherSummaryDto(
    string Id, string FullName, string? Phone, string? Subject, string? PhotoUrl,
    int ObservationsCount, int Drafts, double? Rating, string? LastObservationDate);

/// <summary><c>LaTeacherLesson</c> — one timetable lesson of a teacher on a date.</summary>
public record LaTeacherLessonDto(
    int Period, string? StartTime, string? EndTime, string ClassName, string SubjectName);

public record LaCriterionDto(string Id, string Text, int OrderIndex, bool IsActive);

/// <summary>A lesson analysis sheet (varaq) with its size and use.</summary>
public record LaTemplateDto(
    string Id, string Name, string? Description, int OrderIndex, bool IsActive, bool IsDefault,
    int SectionCount, int CriterionCount, int ObservationCount);

/// <summary><c>POST /templates</c>. <c>CopyFromId</c> — start from another sheet's sections and active criteria.</summary>
public record LaTemplateCreateDto(string? Name, string? Description, string? CopyFromId);

/// <summary><c>PATCH /templates/{id}</c> — only given fields change.</summary>
public record LaTemplatePatchDto(string? Name, string? Description, bool? IsActive, bool? IsDefault, int? OrderIndex);

/// <summary><c>POST /sections</c>.</summary>
public record LaSectionCreateDto(string? TemplateId, string? Title, string? Color);

/// <summary><c>PATCH /sections/{id}</c> — only given fields change.</summary>
public record LaSectionPatchDto(string? Title, string? Color, int? OrderIndex);

public record LaSectionDto(string Id, string Title, int OrderIndex, string Color, List<LaCriterionDto> Criteria);

/// <param name="CriterionText">The text when answered (read-only; null on input).</param>
/// <param name="SectionTitle">The section title when answered (read-only; null on input).</param>
public record LaDetailDto(string CriterionId, bool? IsDone, string? Comment, string? CriterionText = null, string? SectionTitle = null);

public record LaObservationListItemDto(
    string Id, string TeacherId, string TeacherName, string ObserverName, string LessonDate,
    string? ClassName, string? Subject, string Status,
    int ScoreDone, int ScoreTotal, double ScorePercent, bool HasAiSummary, string? TemplateName = null);

public record LaObservationDto(
    string Id, string TeacherId, string TeacherName, string ObserverId, string ObserverName,
    string LessonDate, string? LessonTime, string? ClassName, string? Subject, string Status,
    string? Strengths, string? GrowthAreas, string? NextLessonAdvice,
    string? AiSummary, string? AiError,
    int ScoreDone, int ScoreTotal, double ScorePercent, DateTimeOffset CreatedAt,
    bool CanEdit, List<LaDetailDto> Details,
    // Teacher endpoint only: the checklist (as GET /criteria?includeInactive=true) — a teacher
    // has no /criteria endpoint but needs the criterion texts. Omitted on the staff endpoints
    // (TS: `sections?: LaSection[]` — absent, not null).
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<LaSectionDto>? Sections = null,
    string? TemplateId = null, string? TemplateName = null);

/// <summary><c>POST /observations</c> body (<c>LaObservationInput</c>). PATCH reads the same
/// fields but only the ones present in the body (see <c>LessonAnalysisService.UpdateAsync</c>).</summary>
public record LaObservationInputDto(
    string? TeacherId, string? LessonDate, string? LessonTime, string? ClassName, string? Subject,
    string? Strengths, string? GrowthAreas, string? NextLessonAdvice, List<LaDetailDto>? Details,
    string? TemplateId = null);

/// <summary><c>POST /criteria</c> body. <c>OrderIndex</c> null = end of the section.</summary>
public record LaCriterionCreateDto(string? SectionId, string? Text, int? OrderIndex);

/// <summary><c>PATCH /criteria/{id}</c> body — only given fields change.</summary>
public record LaCriterionPatchDto(string? Text, int? OrderIndex, bool? IsActive, string? SectionId = null);

public record LaRatingRowDto(
    string TeacherId, string TeacherName, string? Subject, int ObservationsCount,
    double Rating, string? LastObservationDate);

public record LaRatingBoardDto(List<LaRatingRowDto> Rows, List<LaTeacherDto> NotEvaluated);

public record LaDashboardDto(
    int TeachersActive, int ObservationsTotal, int ObservationsLast30Days, double AverageRating,
    int Drafts, List<string> NotEvaluated, List<LaRatingRowDto> Top, List<LaRatingRowDto> Bottom);

public record LaMonthlyPointDto(string Month, int Count, double Average);

public record LaSectionProgressDto(string SectionId, string Title, string Color, int Done, int Total, double Percent);

public record LaDistributionBucketDto(string Bucket, int Count);

public record LaTopTeacherDto(string TeacherId, string TeacherName, string? Subject, double Rating, int ObservationsCount);

public record LaCriterionMissDto(
    string CriterionId, string Text, string SectionTitle, int Missed, int Total, double MissPercent);

public record LaAnalyticsDto(
    List<LaMonthlyPointDto> Monthly,
    List<LaSectionProgressDto> Sections,
    List<LaDistributionBucketDto> Distribution,
    List<LaTopTeacherDto> TopTeachers,
    List<LaCriterionMissDto> CriteriaMisses);
