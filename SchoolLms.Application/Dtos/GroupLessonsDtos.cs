namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Cut-over o'chirgichi — docs/modules/students-parity.md §4.3.
// ===========================================================================

/// <summary>
/// Guruh darslari o'chirgichining holati.
/// </summary>
/// <param name="Enabled">
/// <c>school_meta.group_lessons_enabled</c>. false = guruhlar va ro'yxatlar
/// bor, lekin birorta dars, jurnal, hisobot, maosh yoki turniket raqami
/// guruhni KO'RMAYDI.
/// </param>
/// <param name="ActiveGroups">Arxivlanmagan o'quv guruhlari soni.</param>
/// <param name="GroupTemplates">
/// Guruhga tegishli jadval variantlari (qoralama) soni — o'chirgich o'chiq
/// paytda ham yaratilishi mumkin, chunki ular hech qayerda ko'rinmaydi.
/// </param>
/// <param name="GroupWeekAssignments">
/// Haftaga biriktirilgan guruh jadvallari soni. O'chirgich o'chiq bo'lsa bu
/// 0 bo'lishi kerak — endpoint yangisini yozishga yo'l qo'ymaydi.
/// </param>
public record GroupLessonsSwitchDto(
    bool Enabled,
    int ActiveGroups,
    int GroupTemplates,
    int GroupWeekAssignments);

/// <summary>O'chirgichni yoqish/o'chirish so'rovi (faqat tizim egasi).</summary>
public record SaveGroupLessonsSwitchRequest(bool Enabled);

/// <summary>
/// Jadval taxtasidagi "band" belgisi: shu eganing o'quvchilari BOSHQA egada
/// (odatda o'quv guruhida) qatnashadigan soat. Faqat ko'rsatish uchun — G-11.
/// </summary>
/// <param name="OwnerId">Boshqa eganing id'si.</param>
/// <param name="OwnerName">Boshqa eganing nomi (guruh nomi yoki sinf nomi).</param>
/// <param name="OwnerKind"><c>class</c> | <c>group</c>.</param>
/// <param name="StudentCount">Nechta o'quvchi ikkala egada ham bor.</param>
public record PupilOverlaySlotDto(
    int Day,
    int Period,
    string OwnerId,
    string OwnerName,
    string OwnerKind,
    string SubjectId,
    int StudentCount);
