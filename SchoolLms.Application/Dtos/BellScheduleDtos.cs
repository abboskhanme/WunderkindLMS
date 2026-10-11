namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Qo'ng'iroq jadvallari — har xil sinflarda har xil dars vaqtlari (2026-10-11).
//  Qoidalar: SchoolLms.Domain/BellSchedules.cs.
// ===========================================================================

/// <summary>Qo'shimcha jadval: darslari va unga biriktirilgan egalar (sinf yoki guruh id'lari).</summary>
public sealed record BellScheduleDto(Guid Id, string Name, List<LessonTimeDto> Periods, List<string> OwnerIds);

/// <summary>Biriktirish uchun tanlanadigan ega; <paramref name="ScheduleId"/> — hozir qaysi jadvalda (null = asosiy).</summary>
/// <param name="Kind"><c>class</c> yoki <c>group</c> (<see cref="SchoolLms.Domain.LessonOwnerKind"/>).</param>
public sealed record BellOwnerOptionDto(string Id, string Name, string Kind, bool IsTrack, int Grade, Guid? ScheduleId);

/// <summary>Sozlamalar sahifasi uchun hammasi birga.</summary>
/// <param name="Default">Asosiy jadval (Sozlamalar → Dars vaqtlari).</param>
public sealed record BellSchedulesDto(
    List<LessonTimeDto> Default, List<BellScheduleDto> Schedules, List<BellOwnerOptionDto> Owners);

public sealed record SaveBellScheduleRequest(string Name, List<LessonTimeDto> Periods, List<string> OwnerIds);
