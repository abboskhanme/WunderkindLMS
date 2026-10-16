namespace SchoolLms.Application.Dtos;

// The audit screen (docs/modules/audit-log.md, 2026-10-15).

/// <summary>One row of "O'zgarishlar tarixi".</summary>
/// <param name="Kind">create | update | delete — the badge ("Yaratdi" / "Tahrirladi" / "O'chirdi").</param>
/// <param name="ActionLabel">Sub-label for actions beyond the three (Storno, Tasdiqladi…); null for them.</param>
/// <param name="Section">Section key (students, finance, …) and its label.</param>
/// <param name="StudentName">Name of the pupil the row is about (a link), when the pupil still exists.</param>
/// <param name="TeacherName">Same for a teacher.</param>
public record AuditEntryDto(
    string Id, string EntityType, string EntityId, string Action, string Kind, string? ActionLabel,
    string Section, string SectionLabel, string Timestamp, string? ActorId, string? ActorName,
    string Summary, string? Before, string? After,
    string? StudentId, string? StudentName, string? TeacherId, string? TeacherName);

/// <param name="Total">Rows matching the filters (all pages).</param>
/// <param name="NextCursor">Pass back as <c>cursor</c> for the next page; null — this was the last one.</param>
public record AuditPageDto(IReadOnlyList<AuditEntryDto> Items, int Total, string? NextCursor);

/// <summary>Someone who appears in the log. <c>Id</c> "system" — rows written by the system.</summary>
public record AuditActorDto(string Id, string Name, int Count);

public record AuditSectionDto(string Key, string Label);
