namespace SchoolLms.Domain;

// ===========================================================================
//  SINF RAHBARLARI — ko'p-ko'pga (2026-10-09)
// ===========================================================================
//
//  Mijoz: "bitta o'qituvchi bir nechta sinfga sinf rahbari sifatida biriktirish mumkin bo'lsin".
//  Ilgari rahbarlik `teachers.homeroom_class` — BITTA sinf NOMI edi: ikkinchi sinfga
//  biriktirish birinchisidan "o'g'irlardi". Endi haqiqat manbai shu jadval: sinf id'si
//  bo'yicha, sinf nomi o'zgarsa ham bog'lanish joyida qoladi.
//
//  `teachers.homeroom_class` ustuni bazada QOLDI (hech narsa o'chirilmadi), lekin ilova
//  uni endi O'QIMAYDI: ekranlarga ketadigan "sinf rahbarligi" matni har safar shu
//  jadvaldan va yo'nalish guruhlaridan yig'iladi (`HomeroomDirectory`).
//
//  Yo'nalish guruhining sinf rahbari bu jadvalda EMAS — u guruh o'qituvchisi
//  (`study_group_teachers`), guruh formasida "Sinf rahbari" deb ataladi (2026-09-29).
// ===========================================================================

/// <summary>O'qituvchi shu sinfning rahbari. Bitta sinfda ham, bitta o'qituvchida ham bir nechta bo'lishi mumkin.</summary>
public class ClassHomeroomTeacher
{
    /// <summary>classes.id</summary>
    public string ClassId { get; set; } = string.Empty;

    /// <summary>teachers.id</summary>
    public string TeacherId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
