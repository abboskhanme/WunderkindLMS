namespace SchoolLms.Application.Dtos;

// The student flow report (docs/modules/student-flow.md, 2026-10-15).

/// <param name="New">Pupils who arrived in the period (enrolled events).</param>
/// <param name="Activated">Pupils whose FIRST payment ever falls in the period.</param>
/// <param name="Left">Departures in the period.</param>
/// <param name="Returned">Pupils brought back from the archive in the period.</param>
/// <param name="Net">New + returned − left.</param>
/// <param name="HeadcountStart">Active pupils at the start of the first day.</param>
/// <param name="HeadcountEnd">Active pupils at the end of the last day.</param>
public record StudentFlowTotalsDto(
    int New, int Activated, int Left, int Returned, int Net, int HeadcountStart, int HeadcountEnd);

/// <summary>Departures per catalog reason. No reason (or a deleted one) is counted under "Boshqa".</summary>
public record StudentFlowReasonDto(Guid? ReasonId, string Name, int Count, double Percent);

public record StudentFlowClassDto(string? ClassId, string ClassName, int New, int Left, int Returned, int Net);

/// <summary>One pupil in one of the lists.</summary>
/// <param name="EventId">The event (null for "Faollashgan" — that list comes from payments).</param>
/// <param name="Reason">Catalog reason name (departures only).</param>
/// <param name="ReasonText">The note typed with it.</param>
/// <param name="StudentExists">False — the pupil was deleted; no link to the card.</param>
public record StudentFlowRowDto(
    Guid? EventId, string StudentId, string StudentName, string? ClassName, string Date,
    Guid? ReasonId, string? Reason, string? ReasonText, string Source, string? ActorName, bool StudentExists);

public record StudentFlowReportDto(
    string From, string To, string? ClassId, string? ClassName,
    StudentFlowTotalsDto Totals,
    IReadOnlyList<StudentFlowReasonDto> ByReason,
    IReadOnlyList<StudentFlowClassDto> ByClass,
    IReadOnlyList<StudentFlowRowDto> NewList,
    IReadOnlyList<StudentFlowRowDto> ActivatedList,
    IReadOnlyList<StudentFlowRowDto> LeftList,
    IReadOnlyList<StudentFlowRowDto> ReturnedList);

/// <summary>Change a departure's reason later (from the report).</summary>
/// <param name="ReasonId">Catalog row; null — "Boshqa" / none.</param>
/// <param name="ReasonText">The note; null — unchanged, empty — cleared.</param>
public record UpdateStudentEventReasonRequest(Guid? ReasonId, string? ReasonText);
