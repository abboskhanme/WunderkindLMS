using SchoolLms.Application.Services;

namespace SchoolLms.Application.Dtos;

/// <summary>A teacher certificate or named allowance (docs/modules/teacher-allowances.md).</summary>
/// <param name="ActiveNow">Paid in the current month.</param>
/// <param name="IsExpired">The certificate's expiry date has passed.</param>
public record TeacherAllowanceDto(
    Guid Id, string TeacherId, string Source, string Title, string AmountKind, decimal Amount,
    string FromMonth, string? ToMonth, string? Level, string? Number, string? IssuedOn, string? ExpiresOn,
    string? FileUrl, string? Note, bool ActiveNow, bool IsExpired, DateTimeOffset CreatedAt);

/// <summary>
/// The teacher card's "Sertifikatlari" and "Ustama" tabs: every certificate and allowance, the school-wide ustama
/// percent and the current month's pay broken down, so the effect of each line is visible where it is entered.
/// </summary>
public record TeacherAllowancesDto(
    string TeacherId, string FullName, decimal BonusPct, TeacherPayBreakdown CurrentMonth,
    List<TeacherAllowanceDto> Items);

/// <summary>Create / replace one certificate or allowance. Dates "yyyy-MM-dd", months "yyyy-MM".</summary>
public record TeacherAllowanceRequest(
    string Source, string Title, string AmountKind, decimal Amount, string FromMonth, string? ToMonth,
    string? Level, string? Number, string? IssuedOn, string? ExpiresOn, string? FileUrl, string? Note);
