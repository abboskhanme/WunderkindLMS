namespace SchoolLms.Domain;

// ===========================================================================
//  QO'NG'IROQ JADVALLARI — har xil sinflarda har xil dars vaqtlari (2026-10-11)
// ===========================================================================
//
//  Mijoz: "istalgancha tahrirlash mumkin bo'lsin, hammaniki bir xil bo'lmasligi mumkin. yani kichik
//  sinflarda dars soatlari va vaqtlari boshqacha bo'lishi mumkin kattalarda boshqacha, yana boshqalarda
//  boshqacha".
//
//  · ASOSIY jadval — avvalgi umumiy `lesson_times` (Sozlamalar → Dars vaqtlari), o'zgarmagan.
//  · QO'SHIMCHA jadvallar — istalgancha (`bell_schedules`), har birida o'z darslari soni va vaqtlari.
//  · BIRIKTIRISH — sinf yoki yo'nalish guruhi (dars egasi) bitta qo'shimcha jadvalga
//    (`bell_schedule_owners`); biriktirilmagan ega — asosiy jadvalda.
//  · Vaqtni o'qiydigan har bir joy `BellTimes` orqali o'qiydi — darsning egasi bo'yicha.
// ===========================================================================

/// <summary>Qo'shimcha qo'ng'iroq jadvali (masalan "Boshlang'ich sinflar").</summary>
public class BellSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Jadvaldagi bitta dars: raqami, boshlanishi va tugashi ("HH:mm").</summary>
public class BellSchedulePeriod
{
    public Guid ScheduleId { get; set; }
    public int Period { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
}

/// <summary>
/// Dars egasi (sinf id'si yoki o'quv guruhi id'si — jurnal qatorlaridagi <c>class_id</c> ma'nosida)
/// qaysi qo'shimcha jadvalda. Bitta ega — bitta jadval. FK yo'q (ega ikki xil jadvaldan).
/// </summary>
public class BellScheduleOwner
{
    public string OwnerId { get; set; } = string.Empty;
    public Guid ScheduleId { get; set; }
}
