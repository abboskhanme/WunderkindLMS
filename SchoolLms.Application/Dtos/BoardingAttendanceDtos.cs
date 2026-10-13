namespace SchoolLms.Application.Dtos;

/// <summary>Ro'yxatdagi bitta o'quvchi. <c>Eligible = false</c> — shu kuni yotoqxona abonementi
/// yo'q: kulrang, belgilanmaydi, hisobga kirmaydi.</summary>
/// <param name="Status">present | absent | excused; hali belgilanmagan bo'lsa null.</param>
public record BoardingStudentDto(
    string StudentId, string FullName, string ClassName, bool Eligible, string? Status,
    string? ReasonId = null);

/// <summary>Bitta bo'lim — yo'nalish guruhi yoki (guruhsizlar uchun) sinf; yotoqxonada — qizlar / o'g'il bolalar.</summary>
/// <param name="Kind">group | class | gender (dorm, 2026-10-13)</param>
public record BoardingSectionDto(
    string Key, string Title, string Kind, List<BoardingStudentDto> Students,
    int Eligible, int Marked, int Absent);

/// <summary>Kun × sessiya ko'rinishi.</summary>
public record BoardingDayDto(
    string Date, string Session, List<BoardingSectionDto> Sections,
    int Eligible, int Marked, int Absent);

public record BoardingMarkInput(string StudentId, string Status, string? ReasonId = null);

/// <summary>Saqlash — bir bo'limning (yoki bir necha o'quvchining) belgilari.</summary>
public record SaveBoardingRequest(string Date, string Session, List<BoardingMarkInput> Marks);

/// <summary>Saqlash natijasi — nechta ota-onaga xabar ketgani ham.</summary>
public record SaveBoardingResult(int Saved, int Notified);

/// <summary>Ro'yxatdagi o'quvchi (2026-10-11). <c>TrackName</c> — yo'nalish guruhi (bo'lmasa null).</summary>
public record BoardingRosterRowDto(
    string StudentId, string FullName, string ClassName, string? TrackName, string AddedOn, string? AddedByName);

/// <summary>Ro'yxatga qo'shish uchun nomzod — ro'yxatda bo'lmagan faol o'quvchi.</summary>
public record BoardingCandidateDto(string StudentId, string FullName, string ClassName, string? TrackName);

/// <summary>Ro'yxatga o'quvchilar qo'shish.</summary>
public record AddBoardingRosterRequest(string Session, List<string> StudentIds);

/// <summary>Ro'yxatdan chiqarish (bugundan boshlab).</summary>
public record RemoveBoardingRosterRequest(string Session, string StudentId);
