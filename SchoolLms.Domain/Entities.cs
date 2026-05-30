namespace SchoolLms.Domain;

// Frontend (schoollms.client/src/types/index.ts) dagi tiplarga mos keluvchi
// EF Core entity'lari. ID'lar string (frontend uid() — UUID ishlatadi),
// sanalar esa ISO ("YYYY-MM-DD") ko'rinishida string sifatida saqlanadi.

/// <summary>Tizim foydalanuvchisi (autentifikatsiya uchun).</summary>
public class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    /// <summary>admin | teacher | student | parent</summary>
    public string Role { get; set; } = "admin";
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Birinchi muvaffaqiyatli login vaqti (ISO "yyyy-MM-ddTHH:mm:ss") — "ilova aktivlashtirilgan" sifatida ishlatiladi.</summary>
    public string? FirstLoginAt { get; set; }
    /// <summary>Oxirgi muvaffaqiyatli login vaqti — har kirilganda yangilanadi.</summary>
    public string? LastLoginAt { get; set; }
    /// <summary>Xodim (role="staff") lavozimi — Kassir/Administrator/... (faqat ko'rsatish uchun yorliq).</summary>
    public string Position { get; set; } = string.Empty;
    /// <summary>
    /// Xodimga ochiq admin bo'limlari (adminPermissions kalitlari). FAQAT role="staff" uchun ishlatiladi;
    /// admin/superadmin uchun bo'sh (ular hamma narsani ko'radi). EF Core 8 primitive collection (JSON).
    /// </summary>
    public List<string> Permissions { get; set; } = new();
}

/// <summary>Maktab filiali — nomi, manzil, GPS joylashuv va radius (mobil geo-yo'qlama uchun).</summary>
public class Branch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>Ruxsat etilgan radius (metr) — shu doira ichida yo'qlama hisoblanadi.</summary>
    public int RadiusMeters { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Ota-ona ilova orqali yuborgan taklif yoki shikoyat.</summary>
public class Feedback
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Yuborgan o'quvchi (ota-ona o'quvchi akkaunti orqali) id'si.</summary>
    public string StudentId { get; set; } = string.Empty;
    public string ParentName { get; set; } = string.Empty;
    /// <summary>suggestion | complaint</summary>
    public string Type { get; set; } = "suggestion";
    public string Text { get; set; } = string.Empty;
    /// <summary>Ixtiyoriy biriktirilgan rasm (kameradan) — "/uploads/...". Yo'q bo'lsa null.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Yuboruvchi roli: parent | teacher.</summary>
    public string SenderRole { get; set; } = "parent";
    /// <summary>Yuboruvchining ko'rsatiladigan ismi (ota-ona FISH yoki o'qituvchi FISH).</summary>
    public string SenderName { get; set; } = string.Empty;
    /// <summary>O'qituvchi yuborgan bo'lsa — uning id'si (parent bo'lsa null).</summary>
    public string? TeacherId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>new | resolved</summary>
    public string Status { get; set; } = "new";
}

/// <summary>O'quvchi.</summary>
public class Student
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>To'liq FISH — saqlanadi (ko'rsatish, qidiruv, hisobotlar). Parts'dan join qilinadi.</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>Familiya (alohida). FullName parts'dan join qilinadi.</summary>
    public string LastName { get; set; } = string.Empty;
    /// <summary>Ism (alohida).</summary>
    public string FirstName { get; set; } = string.Empty;
    /// <summary>Otasining ismi / Sharifi (alohida).</summary>
    public string MiddleName { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    /// <summary>Metrika (tug'ilganlik haqida guvohnoma) rasm/skani manzili (`/uploads/...`).</summary>
    public string? BirthCertificateUrl { get; set; }
    public string Address { get; set; } = string.Empty;
    /// <summary>male | female</summary>
    public string Gender { get; set; } = "male";
    /// <summary>To'liq ota-ona FISH — saqlanadi. Parts'dan join qilinadi.</summary>
    public string ParentFullName { get; set; } = string.Empty;
    /// <summary>Ota-ona familiyasi (alohida).</summary>
    public string ParentLastName { get; set; } = string.Empty;
    /// <summary>Ota-ona ismi (alohida).</summary>
    public string ParentFirstName { get; set; } = string.Empty;
    /// <summary>Ota-ona otasining ismi / sharifi (alohida).</summary>
    public string ParentMiddleName { get; set; } = string.Empty;
    public string ParentPhone { get; set; } = string.Empty;
    /// <summary>Ota-ona passport rasm/skani manzili (`/uploads/...`).</summary>
    public string? ParentPassportUrl { get; set; }
    public string ClassName { get; set; } = string.Empty;
    /// <summary>Maktabga kelgan (qabul) sanasi (ISO "YYYY-MM-DD"). Oylik to'lov shu oydan boshlanadi.</summary>
    public string EnrollmentDate { get; set; } = string.Empty;
    /// <summary>Balans (so'm): manfiy = qarzdor, 0 = qarzsiz, musbat = avans.</summary>
    public decimal Balance { get; set; }
    /// <summary>Shu o'quvchiga biriktirilgan tizim akkaunti (AppUser) id'si.</summary>
    public string? UserId { get; set; }
    /// <summary>
    /// Oylik to'lov chegirmasi — foiz (0..100). Avval shu foiz olib tashlanadi, keyin
    /// <see cref="DiscountAmount"/> ayriladi. Hisoblangan oylik 0 dan past bo'lmaydi.
    /// </summary>
    public int DiscountPct { get; set; }
    /// <summary>Oylik to'lov chegirmasi — aniq summa (so'm). Foizdan keyin ayriladi.</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>Chegirma sababi/izohi (admin uchun, ko'rsatish uchun saqlanadi).</summary>
    public string DiscountNote { get; set; } = string.Empty;
    /// <summary>
    /// Sinf ichidagi kichik guruh: 0 = guruhsiz (yoki butun sinf), 1 = birinchi guruh, 2 = ikkinchi guruh.
    /// Faqat o'quv yili boshida (jurnal yozuvlari hali yo'q paytda) o'zgartirilishi mumkin.
    /// Bo'lingan darslarda (ScheduleLesson.SubGroup != 0) faqat shu guruhdagi o'quvchilar qatnashadi.
    /// </summary>
    public int SubGroup { get; set; }
    /// <summary>
    /// O'quvchi arxivga ko'chirilganmi (boshqa maktabga ketgan, o'qishdan chiqarilgan, ...).
    /// Arxivlangan o'quvchi faol ro'yxatdan yashirinadi, oylik to'lov hisoblanmaydi, login bloklanadi,
    /// lekin tarixiy ma'lumotlari (jurnal, davomat, to'lovlar) saqlanadi.
    /// </summary>
    public bool IsArchived { get; set; }
    /// <summary>Arxivga ko'chirilgan sana (ISO "YYYY-MM-DD").</summary>
    public string? ArchivedAt { get; set; }
    /// <summary>Arxivga ko'chirish sababi (admin kiritadi: "boshqa maktabga ketdi", ...).</summary>
    public string? ArchiveReason { get; set; }
    /// <summary>O'quvchi uy joylashuvi — kenglik (latitude). Mobil ilovadan GPS orqali keladi.</summary>
    public double? Latitude { get; set; }
    /// <summary>O'quvchi uy joylashuvi — uzunlik (longitude).</summary>
    public double? Longitude { get; set; }
    /// <summary>Joylashuv manzili (reverse geocode'dan keladigan matn, ixtiyoriy).</summary>
    public string? LocationAddress { get; set; }
    /// <summary>Joylashuv oxirgi yangilangan vaqt (ISO).</summary>
    public string? LocationUpdatedAt { get; set; }
}

/// <summary>O'qituvchi.</summary>
public class Teacher
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Gender { get; set; } = "male";
    /// <summary>Telefon raqami — Telegram bot orqali ro'yxatdan o'tishda moslashtiriladi (shartnoma).</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Sinf rahbari bo'lsa biriktirilgan sinf nomi; aks holda bo'sh.</summary>
    public string HomeroomClass { get; set; } = string.Empty;
    /// <summary>Dars beradigan fanlar (Subject id'lari). EF Core 8 primitive collection.</summary>
    public List<string> SubjectIds { get; set; } = new();
    /// <summary>Oylik ish haqi (so'm).</summary>
    public decimal Salary { get; set; }
    /// <summary>
    /// Oylik qaysi oydan hisoblana boshlasin ("YYYY-MM"). Bo'sh bo'lsa — hisobot davrining
    /// boshidan. Bu o'qituvchi ishga kirgan oydan oldin maktabni qarzdor qilmaslik uchun.
    /// </summary>
    public string SalaryStartMonth { get; set; } = string.Empty;
    /// <summary>Shu o'qituvchiga biriktirilgan tizim akkaunti (AppUser) id'si.</summary>
    public string? UserId { get; set; }
    /// <summary>
    /// O'qituvchi web panelida foydalana oladigan bo'limlar (TeacherPermissions kalitlari).
    /// Admin belgilaydi. Bo'sh = faqat Bosh sahifa. EF Core 8 primitive collection (JSON).
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>Arxivlanganmi (ishdan ketgan/to'xtatilgan). Faol ro'yxatdan yashiriladi, login bloklanadi.</summary>
    public bool IsArchived { get; set; }
    /// <summary>Arxivga olingan sana ("YYYY-MM-DD").</summary>
    public string? ArchivedAt { get; set; }
    /// <summary>Arxivga olish sababi.</summary>
    public string? ArchiveReason { get; set; }
}

/// <summary>Fan.</summary>
public class Subject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
}

/// <summary>Sinf.</summary>
public class SchoolClass
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int Grade { get; set; }
    /// <summary>uz | ru</summary>
    public string Language { get; set; } = "uz";
    public decimal MonthlyFee { get; set; }
    public string? Room { get; set; }
}

/// <summary>Lid (maktabga qiziqqan).</summary>
public class Lead
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = "male";
    public string BirthDate { get; set; } = string.Empty;
    public string ParentFullName { get; set; } = string.Empty;
    public string ParentPhone { get; set; } = string.Empty;
    public int TargetGrade { get; set; }
    public string? Note { get; set; }
    /// <summary>Tegishli ustun (LeadStage) id'si.</summary>
    public string Stage { get; set; } = string.Empty;
}

/// <summary>Lid bosqichi (kanban ustuni).</summary>
public class LeadStage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    /// <summary>slate | blue | emerald | amber | violet | rose | cyan | orange</summary>
    public string Color { get; set; } = "slate";
    /// <summary>Ustunlar tartibi.</summary>
    public int Order { get; set; }
}

/// <summary>Oshxona taomi — muayyan sana va ovqat turiga biriktirilgan.</summary>
public class Dish
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Date { get; set; } = string.Empty;
    /// <summary>breakfast | lunch | dinner</summary>
    public string Meal { get; set; } = "breakfast";
    public string Name { get; set; } = string.Empty;
    public string Ingredients { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

/// <summary>Jurnal katagi — baho yoki davomat sababi.</summary>
public class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    /// <summary>Dars raqami (1-10) — bir kunda bir fan bir necha marta bo'lsa farqlash uchun.</summary>
    public int Period { get; set; }
    public int? Grade { get; set; }
    public string? ReasonId { get; set; }
    /// <summary>Bo'linish: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh. ScheduleLesson.SubGroup'ga mos.</summary>
    public int SubGroup { get; set; }
}

/// <summary>
/// O'quvchining fan bo'yicha chorak (yakuniy) bahosi. Kunlik baholardan hisoblanган tavsiya
/// (o'rtacha) o'rniga o'qituvchi qo'ygan rasmiy chorak bahosi — hisobotlarda shu ishlatiladi.
/// (ClassId, SubjectId, Quarter, StudentId) bo'yicha yagona.
/// </summary>
public class QuarterGrade
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Chorak bahosi (2-5).</summary>
    public int Grade { get; set; }
}

/// <summary>Dars mavzusi va uyga vazifa (sana bo'yicha).</summary>
public class LessonNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public string Date { get; set; } = string.Empty;
    /// <summary>Dars raqami (1-10) — bir kunda bir fan bir necha marta bo'lsa farqlash uchun.</summary>
    public int Period { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Homework { get; set; }
    /// <summary>Dars o'tildimi (ptichka). false = dars o'tilmadi.</summary>
    public bool Conducted { get; set; }
    /// <summary>Bo'linish: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh. ScheduleLesson.SubGroup'ga mos.</summary>
    public int SubGroup { get; set; }
}

/// <summary>Sinf uchun nomli dars jadvali varianti.</summary>
public class ScheduleTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<ScheduleLesson> Lessons { get; set; } = new();
}

/// <summary>Jadvaldagi bitta dars katagi.</summary>
public class ScheduleLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    /// <summary>Hafta kuni: 0=Dushanba ... 5=Shanba</summary>
    public int Day { get; set; }
    /// <summary>Dars raqami: 1-10</summary>
    public int Period { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>
    /// Bo'linish: 0 = butun sinfga (default), 1 = faqat 1-guruhga, 2 = faqat 2-guruhga. Bitta
    /// (Day,Period) uchun YOKI bitta SubGroup=0 lesson, YOKI ikki yozuv (SubGroup=1 va SubGroup=2)
    /// saqlanadi.
    /// </summary>
    public int SubGroup { get; set; }
}

/// <summary>Chorak ichidagi haftaga jadval biriktirish.</summary>
public class WeekAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public int Week { get; set; }
    public string? TemplateId { get; set; }
}

/// <summary>Davomat sababi (kelmaganlik turi).</summary>
public class AbsenceReason
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Short { get; set; } = string.Empty;
    /// <summary>
    /// "Kech keldi" turi — o'quvchi DARSDA QATNASHGAN, faqat kech kelgan. Bunday belgi yo'qlik
    /// (absence) sifatida hisoblanmaydi (davomat foiziga ta'sir qilmaydi) va unga BAHO ham qo'yса bo'ladi.
    /// </summary>
    public bool IsLate { get; set; }
}

/// <summary>Chorak davri.</summary>
public class QuarterPeriod
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Quarter { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    /// <summary>
    /// O'qituvchilarga shu chorak bahosini kiritish OCHIQmi (admin boshqaradi). Default — yopiq:
    /// ochilmaguncha o'qituvchi chorak bahosini qo'ya olmaydi (admin baribir qo'ya oladi).
    /// </summary>
    public bool GradesOpen { get; set; }
}

/// <summary>Dars vaqti (qo'ng'iroqlar jadvali).</summary>
public class LessonTime
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Period { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
}

/// <summary>O'quvchiga oy uchun hisoblangan oylik to'lov (qarz yozuvi/tarix).</summary>
public class MonthlyCharge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Oy ("YYYY-MM").</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary>Hisoblangan TO'LIQ summa (o'sha paytdagi sinf oylik to'lovi). Chegirma ALOHIDA.</summary>
    public decimal Amount { get; set; }
    /// <summary>Shu oy uchun berilgan chegirma summasi (so'm). Haqiqiy to'lash kerak bo'lgan summa = Amount - Discount.</summary>
    public decimal Discount { get; set; }
    /// <summary>Hisoblangan sana (ISO "YYYY-MM-DD").</summary>
    public string Date { get; set; } = string.Empty;
}

/// <summary>Moliyaviy amal — kirim yoki chiqim.</summary>
public class FinanceTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Sana (ISO "YYYY-MM-DD").</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>income (kirim) | expense (chiqim)</summary>
    public string Direction { get; set; } = "income";
    /// <summary>Toifa: tuition, salary, utilities, supplies, rent, donation, other ...</summary>
    public string Category { get; set; } = "other";
    /// <summary>Summa (har doim musbat; yo'nalish belgini aniqlaydi).</summary>
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    /// <summary>O'quvchi to'lovi bo'lsa — tegishli o'quvchi id'si.</summary>
    public string? StudentId { get; set; }
    /// <summary>O'qituvchi maoshi bo'lsa — tegishli o'qituvchi id'si.</summary>
    public string? TeacherId { get; set; }
    /// <summary>Oylik to'lov bo'lsa — qaysi oy uchun ("YYYY-MM"). Boshqa amallar uchun null.</summary>
    public string? Month { get; set; }
}

/// <summary>Maktab umumiy holati va ma'lumotlari (bitta qator) — joriy o'quv yili + maktab profili.</summary>
public class SchoolMeta
{
    public string Id { get; set; } = "current";
    /// <summary>Joriy o'quv yili, masalan "2025/2026".</summary>
    public string CurrentYear { get; set; } = string.Empty;
    /// <summary>Maktab nomi.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Direktor F.I.SH.</summary>
    public string Director { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    /// <summary>Telegram bot tokeni (BotFather'dan). Bo'sh bo'lsa bot ishlamaydi (e'lon yuborilmaydi).</summary>
    public string TelegramBotToken { get; set; } = string.Empty;
    /// <summary>Telegram bot foydalanuvchi nomi (@siz) — ko'rsatish uchun, ixtiyoriy.</summary>
    public string TelegramBotUsername { get; set; } = string.Empty;
}

/// <summary>Yangi o'quv yiliga o'tishda saqlangan eski o'quv yili arxivi (to'liq snapshot).</summary>
public class SchoolYearArchive
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Arxivlangan o'quv yili ("2025/2026").</summary>
    public string Year { get; set; } = string.Empty;
    /// <summary>Arxivlangan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
    public int StudentsCount { get; set; }
    public int ClassesCount { get; set; }
    public int JournalCount { get; set; }
    public int FinanceCount { get; set; }
    /// <summary>To'liq ma'lumot snapshot (JSON).</summary>
    public string Data { get; set; } = string.Empty;
}

/// <summary>
/// O'zgarishlar tarixi (audit) yozuvi. Moliyaga oid ma'lumot yaratilganda/tahrirlanganda/
/// o'chirilganda eski va yangi holat shu yerda saqlanadi — keyin "tarix" sifatida ko'riladi.
/// </summary>
public class AuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Ob'ekt turi: FinanceTransaction | TeacherSalary | ClassFee.</summary>
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Tegishli yozuv id'si (amal/ o'qituvchi/ sinf id'si).</summary>
    public string EntityId { get; set; } = string.Empty;
    /// <summary>Amal: create | update | delete.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string Timestamp { get; set; } = string.Empty;
    /// <summary>O'zgartirgan foydalanuvchi id'si (yo'q bo'lsa — tizim).</summary>
    public string? ActorId { get; set; }
    /// <summary>O'zgartirgan foydalanuvchi nomi (yoki "Tizim").</summary>
    public string? ActorName { get; set; }
    /// <summary>O'qiladigan o'zbekcha izoh.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>O'zgarishdan oldingi holat (JSON). create uchun null.</summary>
    public string? Before { get; set; }
    /// <summary>O'zgarishdan keyingi holat (JSON). delete uchun null.</summary>
    public string? After { get; set; }
    /// <summary>Tegishli o'quvchi (o'quvchi to'lovi bo'lsa) — joyida filtrlash uchun.</summary>
    public string? StudentId { get; set; }
    /// <summary>Tegishli o'qituvchi (maosh bo'lsa) — joyida filtrlash uchun.</summary>
    public string? TeacherId { get; set; }
}

/// <summary>
/// Sinf guruh chati xabari. A'zolar: shu sinf o'quvchilari, shu sinfga dars beradigan
/// o'qituvchilar va admin. Chat sinf nomi (ClassName) bo'yicha guruhlanadi.
/// </summary>
public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi sinf chati (sinf nomi, masalan "3-A").</summary>
    public string ClassName { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    /// <summary>admin | teacher | student</summary>
    public string SenderRole { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Sinf ota-onalariga Telegram bot orqali yuborilgan e'lon (bir tomonlama xabar).</summary>
public class Broadcast
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Yuborish vaqtida shu sinfda Telegramda ro'yxatdan o'tgan ota-onalar soni.</summary>
    public int RecipientCount { get; set; }
    /// <summary>Telegram orqali muvaffaqiyatli yetkazilganlar soni.</summary>
    public int SentCount { get; set; }
}

/// <summary>
/// Ota-onaning Telegram ro'yxati — Telegram chatId o'quvchiga bog'lanadi (sinf o'quvchidan
/// kelib chiqadi). Ota-ona botga kontaktini ulashganda raqami o'quvchining ParentPhone'i
/// bilan solishtirilib yoziladi. Bitta ota-onaning bir nechta farzandi bo'lishi mumkin.
/// </summary>
public class TelegramRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Bog'langan o'quvchi id'si (ota-ona ro'yxati uchun). Xodim yozuvida bo'sh.</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Bog'langan o'qituvchi id'si (xodim ro'yxati uchun). Ota-ona yozuvida null.</summary>
    public string? TeacherId { get; set; }
    /// <summary>Telegram chat (foydalanuvchi) id'si — bot shu manzilga e'lon yuboradi.</summary>
    public long ChatId { get; set; }
    /// <summary>Ulashgan foydalanuvchining Telegram ismi (ko'rsatish uchun).</summary>
    public string ParentName { get; set; } = string.Empty;
    /// <summary>Ulashilgan telefon raqami (faqat raqamlar — normallashtirilgan).</summary>
    public string Phone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// O'qituvchi yaratadigan topshiriq/test. Format: written | file | test | video. Bir nechta
/// sinfga beriladi (ClassIds). Materiallar (fayllar) va test savollari bog'liq jadvallarda.
/// </summary>
public class Assignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Yaratgan o'qituvchining AppUser id'si.</summary>
    public string CreatedByUserId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Topshiriq turi (formati): written | file | test | video.</summary>
    public string Format { get; set; } = "written";
    /// <summary>Beriladigan sinflar (Class id'lari).</summary>
    public List<string> ClassIds { get; set; } = new();
    /// <summary>Boshlash vaqti (ISO "yyyy-MM-ddTHH:mm"), ixtiyoriy.</summary>
    public string? StartDate { get; set; }
    /// <summary>Tugash/topshirish muddati (ISO "yyyy-MM-ddTHH:mm"), ixtiyoriy.</summary>
    public string? DueDate { get; set; }
    /// <summary>Muddatdan keyin qabul qilinadimi.</summary>
    public bool LateAccept { get; set; }
    /// <summary>Kechikish jarimasi (% har kun uchun).</summary>
    public int LatePenaltyPct { get; set; }
    /// <summary>Maksimal ball.</summary>
    public int MaxScore { get; set; } = 100;
    /// <summary>Test uchun avto-baholash yoqilganmi.</summary>
    public bool AutoGrade { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Eski (oddiy topshiriq) ustunlari — orqaga moslik uchun saqlanadi, yangi forma ishlatmaydi.
    public string ClassId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public string? Date { get; set; }
    public int? Period { get; set; }
    public string? TypeId { get; set; }

    public List<AssignmentMaterial> Materials { get; set; } = new();
    public List<TestQuestion> Questions { get; set; } = new();
}

/// <summary>Topshiriqqa biriktirilgan material (serverga yuklangan fayl yoki havola).</summary>
public class AssignmentMaterial
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AssignmentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Fayl manzili (masalan "/uploads/xxx.pdf") yoki tashqi havola.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Fayl hajmi (bayt).</summary>
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

/// <summary>Test (format=test) savoli — matn + variantlar + to'g'ri javob indeksi.</summary>
public class TestQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AssignmentId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>Javob variantlari (EF Core 8 primitive collection).</summary>
    public List<string> Options { get; set; } = new();
    /// <summary>To'g'ri variant indeksi (Options ichida).</summary>
    public int CorrectIndex { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// O'quvchining topshiriqni bajarish holati (kim bajardi/bajarmadi). Hozircha o'qituvchi qo'lda
/// belgilaydi; keyinroq mobil ilovada o'quvchi topshirsa shu yozuv to'ldiriladi.
/// </summary>
public class AssignmentSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AssignmentId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Bajarganmi (true=bajardi).</summary>
    public bool Completed { get; set; }
    /// <summary>Bajarilgan/topshirilgan vaqt (ISO), ixtiyoriy.</summary>
    public string? SubmittedAt { get; set; }
    /// <summary>Qo'yilgan ball (ixtiyoriy). Test uchun avto-hisoblanadi.</summary>
    public int? Score { get; set; }
    /// <summary>O'quvchi yozma javobi (format=written), ixtiyoriy.</summary>
    public string? AnswerText { get; set; }
    /// <summary>O'quvchi yuklagan fayl manzili (format=file/video), ixtiyoriy.</summary>
    public string? FileUrl { get; set; }
}

/// <summary>Qo'shimcha topshiriq turi (Uy vazifasi, Mustaqil ish, Test ...). Sozlamalarda boshqariladi.</summary>
public class AssignmentType
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Foydalanuvchining shaxsiy sozlamalari (asosan o'quvchi/o'qituvchi ilovasi uchun): til, tema,
/// bildirishnoma yoqilganmi. Har foydalanuvchi uchun bitta qator (UserId — PK).
/// </summary>
public class UserSettings
{
    /// <summary>AppUser.Id — birlamchi kalit (har foydalanuvchi uchun bitta yozuv).</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>Ilova tili: uz | ru | en. Default "uz".</summary>
    public string Language { get; set; } = "uz";
    /// <summary>Tema: light | dark | system. Default "system".</summary>
    public string Theme { get; set; } = "system";
    /// <summary>Push bildirishnoma yoqilganmi.</summary>
    public bool NotificationsEnabled { get; set; } = true;
    /// <summary>Oxirgi yangilanish vaqti (UTC, ISO).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Mobil/desktop ilovaning push bildirishnoma uchun ro'yxatdan o'tgan qurilma tokeni
/// (FCM/APNs/WebPush). Bir foydalanuvchining bir nechta qurilmasi bo'lishi mumkin.
/// </summary>
public class DeviceToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    /// <summary>Push provayder tokeni (unique).</summary>
    public string Token { get; set; } = string.Empty;
    /// <summary>android | ios | web</summary>
    public string Platform { get; set; } = "android";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Shartnoma uchun yuklangan Word (.docx) andoza. Har target uchun (ota-ona / xodim) alohida.
/// Ichida `@` bilan boshlanuvchi o'rinbosarlar (masalan @fish) bo'ladi — yuborishda almashtiriladi.
/// </summary>
public class ContractTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>parent | staff</summary>
    public string Target { get; set; } = "parent";
    public string Name { get; set; } = string.Empty;
    /// <summary>Yuklangan fayl manzili ("/uploads/...").</summary>
    public string FileUrl { get; set; } = string.Empty;
    /// <summary>Asl fayl nomi (ko'rsatish uchun).</summary>
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/* =========================================================
   LMS — Ta'lim bo'limi (kurslar, dars, materiallar, progress)
   ========================================================= */

/// <summary>LMS fani (kurs) — bitta sinfga tegishli.</summary>
public class LmsSubject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Tegishli sinf (SchoolClass.Id). Sinfga kiruvchi o'quvchilar ko'radi.</summary>
    public string ClassId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Ochilish tartibi: "all" | "sequential" | "batch".</summary>
    public string UnlockMode { get; set; } = "all";
    /// <summary>UnlockMode="batch" bo'lganda bir vaqtda ochiq mavzular soni.</summary>
    public int BatchSize { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<LmsTopic> Topics { get; set; } = new List<LmsTopic>();
}

/// <summary>LMS mavzusi (dars) — bitta bo'lim: video + materiallar + matn.</summary>
public class LmsTopic
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public LmsSubject Subject { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Video havolasi — YouTube, to'g'ri mp4, Vimeo va h.k. Ixtiyoriy.</summary>
    public string? VideoUrl { get; set; }
    /// <summary>Matn mazmuni (video bo'lmasa yoki qo'shimcha izoh). Ixtiyoriy.</summary>
    public string? TextContent { get; set; }
    /// <summary>Tartib raqami (1 dan). Ochilish mantiqida ishlatiladi.</summary>
    public int Order { get; set; }
    public ICollection<LmsMaterial> Materials { get; set; } = new List<LmsMaterial>();
    public ICollection<LmsProgress> Progresses { get; set; } = new List<LmsProgress>();
}

/// <summary>LMS mavzusiga biriktirilgan fayl yoki havola.</summary>
public class LmsMaterial
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TopicId { get; set; } = string.Empty;
    public LmsTopic Topic { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

/// <summary>O'quvchining LMS mavzusini tugallash yozuvi.</summary>
public class LmsProgress
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public LmsTopic Topic { get; set; } = null!;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Yuborilgan shartnoma yozuvi (kim, qachon, qaysi raqam bilan).</summary>
public class Contract
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>parent | staff</summary>
    public string Target { get; set; } = "parent";
    /// <summary>Oluvchi kaliti: ota-ona telefon kaliti (PhoneUtil.Key) yoki teacherId.</summary>
    public string RecipientKey { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    /// <summary>Ketma-ket shartnoma raqami.</summary>
    public int Number { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    /// <summary>Telegram orqali muvaffaqiyatli yetkazildimi.</summary>
    public bool Delivered { get; set; }
    /// <summary>sent</summary>
    public string Status { get; set; } = "sent";
}
