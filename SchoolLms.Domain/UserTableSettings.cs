namespace SchoolLms.Domain;

// ===========================================================================
//  Jadval ko'rinishi sozlamalari — docs/modules/students-parity.md §2.11 (X-1),
//  §3.3.
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  EduSchool'da har bir katta ro'yxatda ustunlarni yashirish, tartibini
//  o'zgartirish va qadab qo'yish mumkin, va tanlov SERVERDA saqlanadi —
//  xodim uyda ham, ishda ham bir xil ko'rinishni ko'radi. Bizda bunday
//  narsa yo'q.
//
//  NEGA MAVJUD `user_settings` GA USTUN QO'SHILMAYDI
//  -------------------------------------------------
//  `user_settings` — foydalanuvchiga BITTA qator (til, tema, bildirishnoma).
//  Jadval sozlamasi esa foydalanuvchi × EKRAN: o'quvchilar ro'yxati,
//  to'lovlar, qarzdorlar, xodimlar... Ularni bitta qatorga tiqish `settings`
//  ichida ekran nomlarini kalit qilishni talab qilardi, ya'ni bitta ekranni
//  saqlash boshqasining sozlamasini ham qayta yozardi (yo'qotish poygasi).
//  Shuning uchun alohida jadval va (user_id, page) kaliti.
//
//  NEGA `jsonb`, MATN EMAS
//  -----------------------
//  Ustunlar ro'yxati, ularning tartibi va kengligi — mijoz tomonidagi
//  tuzilma va u ekran qo'shilgani sayin o'zgaradi; uni ustunlarga yoyish
//  har bir yangi ekranda migratsiya talab qilardi. `jsonb` esa yaroqsiz
//  JSON ni INSERT paytida O'ZI rad etadi (`audit_log` dagi bir xil sabab)
//  va keyinchalik indekslash imkonini qoldiradi. Sxema bu obyekt ICHIGA
//  qaramaydi — u ekranning shartnomasi.
//
//  KIMDA — SHUNDA QOLADI
//  ---------------------
//  Foydalanuvchi o'chirilsa sozlamalari ham ketadi (`on delete cascade`):
//  ular faqat o'shanga tegishli va boshqa hech kimga ma'no bermaydi.
// ===========================================================================

/// <summary>
/// Bitta foydalanuvchining bitta ekrandagi jadval ko'rinishi (X-1).
/// Kalit — (<see cref="UserId"/>, <see cref="Page"/>).
/// </summary>
public class UserTableSetting
{
    /// <summary>users.id — `text`.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Ekran kaliti — mijoz tomoni beradigan barqaror satr
    /// ("admin.students", "admin.finance.invoices"). Bo'sh bo'la olmaydi.
    /// </summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>
    /// Ko'rinish (yashirilgan ustunlar, tartib, qadalganlari) — `jsonb`.
    /// Sukut <c>{}</c> = "hech narsa o'zgartirilmagan".
    /// Npgsql <c>string</c> ni <c>jsonb</c> ga o'zi moslaydi.
    /// </summary>
    public string Settings { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}
