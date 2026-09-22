namespace SchoolLms.Application.Dtos;

/// <summary>
/// Lidni o'quvchiga aylantirish so'rovi (<c>POST /api/admin/leads/{id}/enrol</c>,
/// admission-and-testing.md §6.1).
/// </summary>
/// <param name="Student">
/// Oddiy "Yangi o'quvchi" formasi bilan AYNAN bir xil yuk — sinf, vasiylar va
/// hujjatlar shu yerda. Forma lid ma'lumotlari bilan oldindan to'ldirilgan bo'ladi.
/// </param>
public record LeadEnrolPayload(StudentPayload Student);

/// <summary>Yaratilgan o'quvchi. Lid o'chirilgan — doska uni ro'yxatdan olib tashlaydi.</summary>
public record LeadEnrolResultDto(StudentDto Student);
