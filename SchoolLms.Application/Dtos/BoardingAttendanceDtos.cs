namespace SchoolLms.Application.Dtos;

/// <summary>Ro'yxatdagi bitta o'quvchi. <c>Eligible = false</c> — shu kuni yotoqxona abonementi
/// yo'q: kulrang, belgilanmaydi, hisobga kirmaydi.</summary>
/// <param name="Status">present | absent | excused; hali belgilanmagan bo'lsa null.</param>
public record BoardingStudentDto(
    string StudentId, string FullName, string ClassName, bool Eligible, string? Status,
    string? ReasonId = null);

/// <summary>Bitta bo'lim — yo'nalish guruhi yoki (guruhsizlar uchun) sinf.</summary>
/// <param name="Kind">group | class</param>
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
