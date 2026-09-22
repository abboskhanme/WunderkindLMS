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
    /// <summary>
    /// Admin yaratgan/tiklagan dastlabki parol — OCHIQ matnda, FAQAT foydalanuvchi hali u bilan
    /// kirmaguncha. Superadmin'ga ko'rsatish/eksport uchun. Birinchi login'da yoki foydalanuvchi
    /// o'zi parolni o'zgartirsa null bo'ladi (faqat hash qoladi).
    /// </summary>
    public string? InitialPassword { get; set; }
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
    public DateTime CreatedAt { get; set; } = AppClock.Now;
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
    public DateTime CreatedAt { get; set; } = AppClock.Now;
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
    /// <summary>O'quvchining rasmi (profil surati) manzili (`/uploads/...`). Ilova profilida ko'rinadi.</summary>
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
    /// <summary>Ota-onaning rasmi (profil surati) manzili (`/uploads/...`).</summary>
    public string? ParentPassportUrl { get; set; }
    public string ClassName { get; set; } = string.Empty;
    /// <summary>Maktabga kelgan (qabul) sanasi (ISO "YYYY-MM-DD"). Obuna odatda shu oydan boshlanadi.</summary>
    public string EnrollmentDate { get; set; } = string.Empty;
    /// <summary>Shu o'quvchiga biriktirilgan tizim akkaunti (AppUser) id'si.</summary>
    public string? UserId { get; set; }

    // P1-21: `balance`, `discount_pct`, `discount_amount`, `discount_note`
    // ustunlari o'chirildi (`RetireLegacyFinance`).
    //   · qoldiq  -> `StudentBalanceQuery` (invoices + payment_allocations dan
    //                HAR SAFAR hisoblanadi — saqlanmaydi, ya'ni buzila olmaydi);
    //   · chegirma -> `discounts` jadvali, direktor tasdig'i bilan (SPEC §8.1 Q5).
    // O'quvchi qatorida pul saqlanmaydi: SPEC §3.7 va docs/TASKS.md §1.4.
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
    /// <summary>
    /// Arxivlash sababi KATALOGDAN (<see cref="StudentArchiveReason"/>, §2.2).
    /// null = eski yozuv yoki "Boshqa" tanlangan — bunday holatda tafsilot
    /// yuqoridagi erkin matnda (<see cref="ArchiveReason"/>) qoladi.
    /// Ikkala ustun ham kerak: bu guruhlash uchun, u tafsilot uchun.
    /// </summary>
    public Guid? ArchiveReasonId { get; set; }
    /// <summary>Sinf arxivlanishi tufayli arxivlangan bo'lsa true — sinf arxivdan chiqarilganda
    /// faqat shu o'quvchilar avtomatik qaytariladi (alohida arxivlanganlar tegilmaydi).</summary>
    public bool ArchivedWithClass { get; set; }
    /// <summary>O'quvchi uy joylashuvi — kenglik (latitude). Mobil ilovadan GPS orqali keladi.</summary>
    public double? Latitude { get; set; }
    /// <summary>O'quvchi uy joylashuvi — uzunlik (longitude).</summary>
    public double? Longitude { get; set; }
    /// <summary>Joylashuv manzili (reverse geocode'dan keladigan matn, ixtiyoriy).</summary>
    public string? LocationAddress { get; set; }
    /// <summary>Joylashuv oxirgi yangilangan vaqt (ISO).</summary>
    public string? LocationUpdatedAt { get; set; }
    /// <summary>Turniket/FaceID qurilmasidagi shaxs ID'si (personId/employeeNo). Turniket o'tish
    /// hodisalari shu ID orqali o'quvchiga bog'lanadi (kirgan/chiqqan vaqt). Bo'sh = moslanmagan.</summary>
    public string DeviceUserId { get; set; } = string.Empty;

    // =======================================================================
    //  students-parity.md §2.3 (S-5, S-8) — hammasi QO'SHIMCHA va null bo'la
    //  oladi. Mavjud birorta ustun ko'chirilmadi va ma'nosi o'zgarmadi.
    // =======================================================================

    /// <summary>
    /// O'quvchining O'Z telefoni (S-8). Ota-onaning raqami
    /// <see cref="ParentPhone"/> da qoladi — bu uning o'rniga emas, yoniga.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// O'qish tili: <c>uz</c> | <c>ru</c> | <c>en</c> | <c>kaa</c> (S-8).
    /// null = ko'rsatilmagan (sinf tili amal qiladi).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Hujjat NUSXASI — tug'ilganlik guvohnomasi yoki pasport skani (S-8).
    ///
    /// <para>
    /// <b>Nega yangi ustun:</b> <see cref="BirthCertificateUrl"/> nomi
    /// aldamchi — u amalda o'quvchining PROFIL SURATI. §2.3.2 shu ma'noni
    /// qayd etadi va uni o'zgartirmaydi: hujjat skani shu yerga tushadi,
    /// birorta mavjud fayl ko'chirilmaydi.
    /// </para>
    /// </summary>
    public string? DocumentUrl { get; set; }

    /// <summary>
    /// Holat tagi (<see cref="StudentStatus"/>, S-5). null = holat qo'yilmagan —
    /// migratsiyadan keyin HAMMA o'quvchi shunday.
    /// </summary>
    public Guid? StatusId { get; set; }

    // =======================================================================
    //  students-parity.md §3.3 (Batch C, S-9) — qo'shimcha va null bo'la oladi.
    // =======================================================================

    /// <summary>
    /// Sinfi hali YO'Q o'quvchining mo'ljaldagi sinf darajasi (S-9): qabul
    /// qilindi, lekin qaysi sinfga tushishi keyin hal qilinadi.
    ///
    /// <para>
    /// null = mo'ljal ko'rsatilmagan (bugungi HAMMA o'quvchi shunday) — ya'ni
    /// bu ustun <see cref="ClassName"/> ning o'rniga emas, u BO'SH bo'lgan
    /// holat uchun. 0 = maktabgacha tayyorlov (<see cref="Lead.TargetGrade"/>
    /// bilan bir xil o'lchov).
    /// </para>
    /// </summary>
    public short? TargetGrade { get; set; }
}

/// <summary>O'qituvchi.</summary>
public class Teacher
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Gender { get; set; } = "male";
    /// <summary>O'qituvchining rasmi (profil surati) manzili (`/uploads/...`).</summary>
    public string? PhotoUrl { get; set; }
    /// <summary>Telefon raqami — Telegram bot orqali ro'yxatdan o'tishda moslashtiriladi (shartnoma).</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Turniket/FaceID qurilmasidagi xodim ID'si (personId/employeeNo). Davomat hodisalari shu
    /// ID orqali o'qituvchiga bog'lanadi. Bo'sh = qurilmada moslashtirilmagan.</summary>
    public string DeviceUserId { get; set; } = string.Empty;
    /// <summary>Sinf rahbari bo'lsa biriktirilgan sinf nomi; aks holda bo'sh.</summary>
    public string HomeroomClass { get; set; } = string.Empty;
    /// <summary>Dars beradigan fanlar (Subject id'lari). EF Core 8 primitive collection.</summary>
    public List<string> SubjectIds { get; set; } = new();
    /// <summary>Oylik ish haqi (so'm). ESKI — endi oylik dars jadvali + toifa soat narxidan
    /// avtomatik hisoblanadi (<see cref="Category"/>). Qo'lda kiritilmaydi.</summary>
    public decimal Salary { get; set; }
    /// <summary>O'qituvchi toifasi — bir soat dars narxini belgilaydi: "oliy" | "1" | "2" | "mutaxasis"
    /// (bo'sh = hali belgilanmagan, narxi 0). Soat narxlari SchoolMeta'da toifa bo'yicha saqlanadi.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Ustama foizi (%). Oylik maoshga shu foiz qo'shiladi (0 = ustama yo'q). Masalan 50 → +50%.</summary>
    public decimal BonusPct { get; set; }
    /// <summary>
    /// Oylik qaysi oydan hisoblana boshlasin ("YYYY-MM"). ESKI maydon — endi <see cref="SalaryStartDate"/>
    /// ishlatiladi (to'liq sana). Zaxira sifatida qoldirilgan (SalaryStartDate bo'sh bo'lsa o'qiladi).
    /// </summary>
    public string SalaryStartMonth { get; set; } = string.Empty;
    /// <summary>
    /// Maosh qaysi KUNdan hisoblana boshlasin ("YYYY-MM-DD"). O'qituvchi oy o'rtasida kelsa — birinchi
    /// oy shu kundan oy oxirigacha QISMAN (haqiqiy darslar soni bo'yicha) hisoblanadi. Keyingi oylar to'liq.
    /// </summary>
    public string SalaryStartDate { get; set; } = string.Empty;
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

    /// <summary>
    /// Fan "guruhlarga bo'linadi" — faqat shunday fanga o'quv guruhi
    /// (<see cref="StudyGroup"/>) ochiladi (students-parity.md §2.1.1, G-9).
    /// Sukut bo'yicha <b>false</b>: mavjud fanlarning hech biri guruhli emas,
    /// maktab kerakli fanni o'zi belgilaydi.
    /// </summary>
    public bool IsGroupable { get; set; }

    // =======================================================================
    //  students-parity.md §3.3 (Batch C, F-3).
    // =======================================================================

    /// <summary>
    /// Jadval katakchasini bo'yaydigan rang <c>#RRGGBB</c> ko'rinishida
    /// (F-3). null = neytral rang — migratsiyadan keyin HAMMA fan shunday.
    /// Ro'yxat <see cref="StudentStatus.Color"/> bilan bir xil shaklda.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Fan faolmi (F-3). <b>false = o'chirish o'rniga arxivlash</b>: yangi
    /// jadvalda va tanlovlarda ko'rinmaydi, lekin unga bog'langan jurnal,
    /// baho va sertifikat JOYIDA qoladi.
    ///
    /// <para>
    /// Sukut <b>true</b> — mavjud har bir fan faol bo'lib qoladi, ya'ni
    /// bugungi birorta ekran o'zgarmaydi.
    /// </para>
    /// </summary>
    public bool IsActive { get; set; } = true;
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
    /// <summary>Sinf arxivlangan (faol ro'yxatdan olib qo'yilgan). Arxivlanganda unga bog'langan
    /// o'quvchilar ham arxivlanadi; arxivdan chiqarilganda — qaytariladi.</summary>
    public bool IsArchived { get; set; }
    public string? ArchivedAt { get; set; }

    // =======================================================================
    //  students-parity.md §3.3 (Batch C, C-4).
    // =======================================================================

    /// <summary>
    /// Sinfga nechta o'quvchi sig'adi (C-4). null = chek YO'Q —
    /// migratsiyadan keyin HAMMA sinf shunday, ya'ni bugungi qo'shish va
    /// ko'chirish oqimlari o'zgarmaydi.
    ///
    /// <para>
    /// Bu <b>OGOHLANTIRISH</b> chegarasi, taqiq emas (§2.2.3 C-4): sinf
    /// to'lganda ekran ogohlantiradi, lekin admin baribir qo'sha oladi —
    /// maktab hayotida "bitta ortiqcha bola" haqiqiy holat.
    /// </para>
    /// </summary>
    public short? Capacity { get; set; }
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

    // --- Savdo va marketing (sales-marketing.md §4.4) qo'shgan uchta ustun ---
    // QO'SHIMCHA (additive): doskaning o'zi (dizayni muzlatilgan
    // `pages/admin/leads/*`) ilgarigi maydonlarni chizishda davom etadi.
    // Konfiguratsiya SalesMarketingModel.cs da — ustunni qaysi migratsiya
    // qo'shgan bo'lsa, o'sha faylda turadi.

    /// <summary>manual | survey — <see cref="LeadSource"/>. Bugungi har bir qator `manual`: ular haqiqatan ham doskada qo'lda kiritilgan.</summary>
    public string Source { get; set; } = LeadSource.Manual;

    /// <summary>
    /// Qaysi arizadan kelgani (<see cref="Survey.Id"/>). <c>on delete restrict</c> —
    /// <c>ck_leads_source_survey</c> bilan hech qachon qarama-qarshi bo'lib qolmasin
    /// (arizada lid bo'lsa u arxivlanadi, o'chirilmaydi — D7).
    /// </summary>
    public Guid? SurveyId { get; set; }

    /// <summary>
    /// Lid yaratilgan vaqt. <b>NULLABLE va eski qatorlar NULL bo'lib qoladi</b>
    /// (§4.4): <c>not null default now()</c> butun tarixni migratsiya
    /// vaqti bilan tamg'alardi — fakt kabi o'qiladigan, lekin fakt bo'lmagan
    /// son. <c>NULL</c> = "migratsiyadan oldin yaratilgan, aniq sanasi
    /// noma'lum", va voronka o'sha qatorlarni yashirmasdan sanab ko'rsatadi.
    ///
    /// <para>
    /// Yangi qator qiymatni SHU INITSIALIZATORDAN oladi (<see cref="Broadcast.CreatedAt"/>
    /// naqshi), ya'ni birorta kontroller tahrirlanmaydi; bazadan o'qilgan
    /// qator esa <c>NULL</c> ligicha qoladi — EF ustun qiymatini initsializator
    /// ishlagandan KEYIN o'rnatadi.
    /// </para>
    ///
    /// <para>
    /// Tipi <c>DateTime?</c> (→ <c>timestamp without time zone</c>), yangi
    /// jadvallardagi <c>DateTimeOffset</c> emas: bu ESKI entity va yonidagi
    /// <see cref="Broadcast.CreatedAt"/> bilan bir xil semantikada turishi
    /// kerak (§4 ning vaqt turi haqidagi ogohlantirishi).
    /// </para>
    /// </summary>
    public DateTime? CreatedAt { get; set; } = AppClock.Now;

    // --- Admission (admission-and-testing.md §2.2, §5.13) ---
    // Additive, like the three columns above: `GET /api/admin/leads` returns it
    // as one more JSON field, and the design-frozen board (`pages/admin/leads/*`)
    // neither reads nor renders it. Mapping lives in ExamModel.cs — the file of
    // the migration that adds the column.

    /// <summary>
    /// Where the lead is in the admission pipeline — <see cref="LeadAdmissionStatus"/>.
    /// <c>none</c> for every ordinary lead (and every row that existed before
    /// the <c>AdmissionAndExams</c> migration). Never <c>enrolled</c>: enrolment
    /// deletes the lead (2026-09-22), so that state has no row to live on.
    /// </summary>
    public string AdmissionStatus { get; set; } = LeadAdmissionStatus.None;
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
    /// <summary>Uyga vazifa bajarilishi: 0 = belgilanmagan, 1 = qildi, 2 = qilmadi.</summary>
    public int Homework { get; set; }
    /// <summary>Xulq: 0 = belgilanmagan, 1 = yaxshi, 2 = yomon.</summary>
    public int Behavior { get; set; }
    /// <summary>Shu darsni o'zlashtirish foizi (0-100). null = belgilanmagan.</summary>
    public int? Mastery { get; set; }
    /// <summary>Bo'linish: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh. ScheduleLesson.SubGroup'ga mos.</summary>
    public int SubGroup { get; set; }

    /// <summary>
    /// Qatorning egasi: <c>class</c> (<see cref="ClassId"/> — sinf id'si) yoki
    /// <c>group</c> (<see cref="ClassId"/> — o'quv guruhi id'si). Qiymatlar:
    /// <see cref="LessonOwnerKind"/>. Mavjud har bir qator va yangi yozilgan har
    /// bir qator sukut bo'yicha <c>class</c> — ya'ni bugungi kod o'zgarishsiz
    /// ishlaydi (students-parity.md §2.1.4, §3.1).
    /// </summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
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

    /// <summary>
    /// Qatorning egasi: <c>class</c> (<see cref="ClassId"/> — sinf id'si) yoki
    /// <c>group</c> (<see cref="ClassId"/> — o'quv guruhi id'si). Qiymatlar:
    /// <see cref="LessonOwnerKind"/>. Mavjud har bir qator va yangi yozilgan har
    /// bir qator sukut bo'yicha <c>class</c> — ya'ni bugungi kod o'zgarishsiz
    /// ishlaydi (students-parity.md §2.1.4, §3.1).
    /// </summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
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

    /// <summary>
    /// Qatorning egasi: <c>class</c> (<see cref="ClassId"/> — sinf id'si) yoki
    /// <c>group</c> (<see cref="ClassId"/> — o'quv guruhi id'si). Qiymatlar:
    /// <see cref="LessonOwnerKind"/>. Mavjud har bir qator va yangi yozilgan har
    /// bir qator sukut bo'yicha <c>class</c> — ya'ni bugungi kod o'zgarishsiz
    /// ishlaydi (students-parity.md §2.1.4, §3.1).
    /// </summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
}

/// <summary>Sinf uchun nomli dars jadvali varianti.</summary>
public class ScheduleTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<ScheduleLesson> Lessons { get; set; } = new();

    /// <summary>
    /// Qatorning egasi: <c>class</c> (<see cref="ClassId"/> — sinf id'si) yoki
    /// <c>group</c> (<see cref="ClassId"/> — o'quv guruhi id'si). Qiymatlar:
    /// <see cref="LessonOwnerKind"/>. Mavjud har bir qator va yangi yozilgan har
    /// bir qator sukut bo'yicha <c>class</c> — ya'ni bugungi kod o'zgarishsiz
    /// ishlaydi (students-parity.md §2.1.4, §3.1).
    /// </summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
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

    /// <summary>
    /// Qatorning egasi: <c>class</c> (<see cref="ClassId"/> — sinf id'si) yoki
    /// <c>group</c> (<see cref="ClassId"/> — o'quv guruhi id'si). Qiymatlar:
    /// <see cref="LessonOwnerKind"/>. Mavjud har bir qator va yangi yozilgan har
    /// bir qator sukut bo'yicha <c>class</c> — ya'ni bugungi kod o'zgarishsiz
    /// ishlaydi (students-parity.md §2.1.4, §3.1).
    /// </summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
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
    /// <summary>
    /// Intizomiy ball o'zgarishi — jurnalda shu sabab bilan davomat belgilansa, o'quvchining
    /// intizomiy balliga shu qiymat qo'shiladi (manfiy = jazo). 0 = ballga ta'sir qilmaydi (default).
    /// "Ball sabablar" bo'limida belgilanadi.
    /// </summary>
    public int Points { get; set; }
}

/// <summary>
/// Intizomiy ball sababi — nomi va ball o'zgarishi. Musbat = rag'bat (qo'shiladi),
/// manfiy = jazo (ayriladi). Masalan "Darsga kech qoldi" = −5, "Faol ishtirok" = +3.
/// </summary>
public class DisciplineReason
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    /// <summary>Ball o'zgarishi (musbat yoki manfiy).</summary>
    public int Points { get; set; }

    // ----- §6.3, 4- va 5-qadam: ota-onaga xabar va sabab kartochkasi -----

    /// <summary>
    /// Shu sabab bo'yicha ball qo'yilganda OTA-ONAGA xabar bormi (§6.3, 4-qadam).
    /// Xabar mavjud kanal orqali ketadi — <b>Telegram</b> (TelegramService /
    /// NotificationsController). SMS ustuni ATAYLAB YO'Q: bu mahsulotda yagona
    /// kanal Telegram (CLAUDE.md), EduSchool'dagi `behaviorSmsTemplate` ning
    /// nusxasi bizga kerak emas.
    ///
    /// <para>
    /// <b>Sukut bo'yicha false, va mavjud HAR BIR sababda ham false.</b>
    /// §6.3 ning o'z ogohlantirishi: ota-onaga xabar yuboradigan intizomiy
    /// ball — butunlay boshqa ijtimoiy hodisa. Har kechikishda ota-onaga
    /// xabar yuboradigan tizim bir hafta ichida o'chiriladi va o'zi bilan
    /// birga butun modulni olib ketadi. Maktab uni har sabab uchun ALOHIDA,
    /// ataylab yoqishi kerak.
    /// </para>
    /// </summary>
    public bool NotifyParent { get; set; }

    /// <summary>Sabab izohi — qachon qo'yiladi, nimani anglatadi (§6.3, 5-qadam).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// false = yangi ball qo'yishda tanlanmaydi, lekin ESKI yozuvlar joyida
    /// qoladi (<see cref="DisciplinePoint"/> nom va ballni nusxalab oladi).
    /// Sukut bo'yicha true — mavjud sabablarning hammasi faol.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// O'quvchiga qo'yilgan bitta intizomiy ball yozuvi. Har o'quvchi 100 balldan boshlaydi;
/// qoldi = 100 + barcha yozuvlar ballari yig'indisi.
/// </summary>
public class DisciplinePoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    public string ReasonId { get; set; } = string.Empty;
    /// <summary>Sabab nomi (nusxa — sabab keyin o'zgarsa/o'chsa ham tarix saqlanadi).</summary>
    public string ReasonName { get; set; } = string.Empty;
    /// <summary>Yozuv paytidagi ball (sababdan nusxa — sabab keyin o'zgarsa tarix saqlanadi).</summary>
    public int Points { get; set; }
    public string Note { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt (ISO).</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Kim qo'ygani (admin F.I.SH yoki login).</summary>
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// O'quvchilarni baholash turi — admin xohlagancha qo'sha oladi (masalan "Og'zaki",
/// "Yozma", "Nazorat ishi", "Loyiha"). Hozircha faqat nom va izoh; baholash mantig'i keyin qo'shiladi.
/// </summary>
public class EvaluationType
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    /// <summary>Ixtiyoriy izoh.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt (ISO) — tartiblash uchun.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// O'quvchiga bitta baholash turi bo'yicha bir oyda qo'yilgan baho (1-5). Har
/// (StudentId, EvaluationTypeId, Month) uchun yagona — "har oy bir marta" baholash.
/// </summary>
public class EvaluationGrade
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    public string EvaluationTypeId { get; set; } = string.Empty;
    /// <summary>
    /// Qaysi FAN bo'yicha baholangani — shu fan o'qituvchisi qo'yadi. "" (bo'sh) = umumiy
    /// (eski/admin fan ko'rsatmasdan qo'ygan). Har (Student, Subject, Type, Month) uchun yagona.
    /// </summary>
    public string SubjectId { get; set; } = string.Empty;
    /// <summary>Baho tegishli oy ("YYYY-MM"). Har oy uchun alohida baho.</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary>Oy ichida qaysi haftada baholangani (1..5; 0 = butun oy tanlangan edi).</summary>
    public int Week { get; set; }
    /// <summary>Baho 1-5.</summary>
    public int Score { get; set; }
    /// <summary>Oxirgi yangilangan vaqt (ISO).</summary>
    public string UpdatedAt { get; set; } = string.Empty;
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

/// <summary>
/// Bayram / dam olish kuni — shu sanada hech bir sinfda dars bo'lmaydi. Dars jadvali
/// ko'rinishlarida "Bayram" deb belgilanadi va jurnal ustunlaridan chiqarib tashlanadi.
/// Butun maktab uchun (sinfga bog'liq emas).
/// </summary>
public class Holiday
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Sana "YYYY-MM-DD".</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Nomi (ixtiyoriy, masalan "Navro'z bayrami").</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>Dars vaqti (qo'ng'iroqlar jadvali).</summary>
public class LessonTime
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Period { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
}

// Eski moliya entity'lari (`MonthlyCharge`, `FinanceTransaction`) P1-21 da
// olib tashlandi. O'rniga SPEC §3.7 modeli — `SchoolLms.Domain/Billing.cs`:
// oylik hisob `invoices`, pul harakati `payments` + `ledger_entries`,
// chiqim `expenses`. Jadvallar `RetireLegacyFinance` migratsiyasida o'chdi.

/// <summary>Maktab umumiy holati va ma'lumotlari (bitta qator) — joriy o'quv yili + maktab profili.</summary>
/// <summary>O'qituvchining bir kunlik ish davomati.</summary>
public class TeacherAttendance
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Sana "yyyy-MM-dd".</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Holat: "present" (keldi) | "absent" (kelmadi) | "late" (kechikdi).</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Izoh / sabab (ixtiyoriy).</summary>
    public string Note { get; set; } = string.Empty;
    /// <summary>Kelgan vaqti "HH:mm" (turniketdan birinchi KIRISH). Bo'sh = noma'lum.</summary>
    public string CheckIn { get; set; } = string.Empty;
    /// <summary>Ketgan vaqti "HH:mm" (turniketdan oxirgi CHIQISH). Bo'sh = noma'lum.</summary>
    public string CheckOut { get; set; } = string.Empty;
    /// <summary>Manba: "manual" (admin qo'lda) | "turnstile" (qurilmadan avtomatik). Sinxronlash
    /// "manual" yozuvlarni o'zgartirmaydi (admin qo'lda tuzatgan bo'lsa saqlanadi).</summary>
    public string Source { get; set; } = "manual";
}

/// <summary>Maktab avtobusi (GPS bilan kuzatiladi).</summary>
public class Bus
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Avtobus nomi/raqami, masalan "1-avtobus".</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Davlat raqami ("01 A 123 BC").</summary>
    public string PlateNumber { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverPhone { get; set; } = string.Empty;
    /// <summary>GPS tracker qurilma ID'si (IMEI / id) — GPS hodisalari shu orqali avtobusga bog'lanadi.</summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>Marshrut nomi (ixtiyoriy).</summary>
    public string Route { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = string.Empty;
}

/// <summary>Avtobusning bitta GPS nuqtasi (iz / trail).</summary>
public class BusLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BusId { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>Tezlik (km/soat).</summary>
    public double Speed { get; set; }
    /// <summary>Qurilma vaqti (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string RecordedAt { get; set; } = string.Empty;
    /// <summary>Tizimga yozilgan vaqt (ISO).</summary>
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Maktab kamerasi (IP/RTSP). Media-shlyuz (MediaMTX) orqali brauzerda jonli + playback.</summary>
public class Camera
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    /// <summary>Joylashuvi ("1-qavat koridor", "Hovli" ...).</summary>
    public string Location { get; set; } = string.Empty;
    /// <summary>Asosiy RTSP oqimi (login/parol bilan): rtsp://user:pass@ip:554/...</summary>
    public string RtspUrl { get; set; } = string.Empty;
    /// <summary>Past sifatli (sub) RTSP — grid (ko'p kamera) uchun. Bo'sh bo'lsa asosiy ishlatiladi.</summary>
    public string RtspSubUrl { get; set; } = string.Empty;
    /// <summary>Yozuv necha KUN saqlansin — undan eski yozuvlar shlyuz tomonidan avtomatik o'chiriladi.
    /// 0 = cheksiz (o'chirilmaydi).</summary>
    public int RetentionDays { get; set; } = 7;
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = string.Empty;
}

/// <summary>Turniket/FaceID qurilmasidan kelgan bitta o'tish hodisasi (xom log).</summary>
public class TurnstileEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Bog'langan o'qituvchi (DeviceUserId orqali topilgan). Topilmasa bo'sh.</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Qurilmadagi xodim ID'si.</summary>
    public string DeviceUserId { get; set; } = string.Empty;
    /// <summary>Hodisa vaqti (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string EventAt { get; set; } = string.Empty;
    /// <summary>Yo'nalish: "in" (kirish) | "out" (chiqish).</summary>
    public string Direction { get; set; } = "in";
    /// <summary>Qurilma nomi/manzili (qaysi eshik).</summary>
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Tizimga yozilgan vaqt (ISO).</summary>
    public string CreatedAt { get; set; } = string.Empty;
}

public class SchoolMeta
{
    // Shared-DB: har maktab uchun bitta SchoolMeta qatori — Id unikal (Guid). Eski "current"
    // yagona maktab qatori — bitta yozuv saqlanadi.
    public string Id { get; set; } = Guid.NewGuid().ToString();
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
    /// <summary>Telegram bot foydalanuvchi nomi (@siz) — t.me havolasi va ro'yxat taklifi uchun.</summary>
    public string TelegramBotUsername { get; set; } = string.Empty;
    /// <summary>Telegram bot ko'rsatiladigan nomi (masalan "Maktab LMS Bot") — UI/ilovada ko'rsatish uchun.</summary>
    public string TelegramBotName { get; set; } = string.Empty;
    /// <summary>
    /// Firebase service account (JSON, to'liq) — ilovaga push (FCM) yuborish uchun. Bo'sh bo'lsa
    /// push yuborilmaydi. Admin "Sozlamalar → Push (Firebase)" bo'limidan kiritadi.
    /// </summary>
    public string FcmServiceAccountJson { get; set; } = string.Empty;
    /// <summary>
    /// Firebase WEB app konfiguratsiyasi (JSON: apiKey, authDomain, projectId, messagingSenderId,
    /// appId). Web (PWA) push uchun — brauzer FCM token olishi uchun zarur. Firebase Console →
    /// Project Settings → General → Your apps → Web app config. Ommaviy (maxfiy emas).
    /// </summary>
    public string FcmWebConfigJson { get; set; } = string.Empty;
    /// <summary>
    /// Web Push (VAPID) ochiq kaliti — Firebase Console → Cloud Messaging → Web configuration →
    /// "Web Push certificates" (Key pair). Web (PWA) push uchun zarur.
    /// </summary>
    public string FcmVapidKey { get; set; } = string.Empty;

    /// <summary>O'qituvchi maoshi hisoblashda toifa bo'yicha BIR SOAT dars narxi (so'm).
    /// Oylik maosh = haftalik darslar soni × 4 × shu narx. Admin "Dars jadvali → Oylik hisoblash"da kiritadi.</summary>
    public decimal SalaryRateOliy { get; set; }
    public decimal SalaryRate1 { get; set; }
    public decimal SalaryRate2 { get; set; }
    public decimal SalaryRateMutaxasis { get; set; }

    // ---------- Turniket / FaceID integratsiyasi (o'qituvchilar davomati avtomatik) ----------
    /// <summary>Integratsiya yoqilganmi.</summary>
    public bool TurnstileEnabled { get; set; }
    /// <summary>Qurilma turi/vendori: "hikvision" | "zkteco".</summary>
    public string TurnstileVendor { get; set; } = "hikvision";
    /// <summary>Qurilma manzili (IP yoki host), masalan "192.168.1.64".</summary>
    public string TurnstileHost { get; set; } = string.Empty;
    /// <summary>Qurilma porti (Hikvision ISAPI odatda 80).</summary>
    public int TurnstilePort { get; set; } = 80;
    public string TurnstileUsername { get; set; } = string.Empty;
    public string TurnstilePassword { get; set; } = string.Empty;
    /// <summary>Ish boshlanish vaqti "HH:mm" — kechikishni aniqlash uchun (dars jadvalidagi birinchi
    /// dars bilan birga, qaysi biri erta bo'lsa). Bo'sh bo'lsa faqat dars jadvali ishlatiladi.</summary>
    public string WorkStartTime { get; set; } = "08:30";
    /// <summary>Kechikishga yo'l qo'yiladigan daqiqalar (grace). Kelgan vaqt kutilgan + grace dan
    /// keyin bo'lsa — "kechikdi".</summary>
    public int LateGraceMinutes { get; set; } = 10;
    /// <summary>Oxirgi muvaffaqiyatli sinxronlash vaqti (ISO).</summary>
    public string TurnstileLastSync { get; set; } = string.Empty;

    // ---------- GPS (maktab avtobuslarini kuzatish) integratsiyasi ----------
    /// <summary>GPS kuzatuv yoqilganmi.</summary>
    public bool GpsEnabled { get; set; }
    /// <summary>Webhook kalit (ixtiyoriy) — tracker hodisa yuborganda shu token tekshiriladi (bo'sh = tekshirilmaydi).</summary>
    public string GpsIngestToken { get; set; } = string.Empty;
    /// <summary>"Onlayn" deb hisoblash uchun oxirgi signal necha daqiqa ichida bo'lishi kerak.</summary>
    public int GpsOnlineMinutes { get; set; } = 5;
    /// <summary>To'xtash deb hisoblanadigan radius (metr) — shu radiusda turib qolsa to'xtash.</summary>
    public int GpsStopRadiusM { get; set; } = 60;
    /// <summary>To'xtash deb hisoblanadigan eng kichik davomiylik (daqiqa).</summary>
    public int GpsStopMinMinutes { get; set; } = 3;

    // ---------- Kamera (videokuzatuv) integratsiyasi ----------
    /// <summary>Kamera kuzatuvi yoqilganmi.</summary>
    public bool CameraEnabled { get; set; }

    // =======================================================================
    //  Umumiy sozlama bayroqlari — §5.5. FAQAT TO'RTTASI.
    //
    //  EduSchool'ning `/settings` obyektida ~40 ta bayroq bor; §5.5 ulardan
    //  ATAYLAB to'rttasini oladi. Qolganlari rad etilgan, jumladan MODUL
    //  O'CHIRGICHLARI (`enableBehaviorSystem`, `gamification`, `warehouse`,
    //  `receptionAttendanceEnabled`): mijoz sotib olgan narsani yetkazamiz,
    //  o'chirilgan modul esa umuman build'da bo'lmasligi kerak.
    // =======================================================================

    /// <summary>
    /// Qarzi bor o'quvchini arxivlashni TAQIQLAYDI (§5.5, mijoz savoli Q4).
    ///
    /// <para>
    /// Sukut bo'yicha <b>true</b> — Q4 ning "javob bo'lmasa" qarori aynan shu:
    /// arxivlash qarzning yo'qolishining eng oson yo'li va u yopiq turishi
    /// kerak. Superadmin uchun chetlab o'tish imkoni ilova qatlamida beriladi.
    /// </para>
    /// </summary>
    public bool ArchiveOnlyNonDebtorStudents { get; set; } = true;

    /// <summary>
    /// Jurnalda "sababsiz" qoldirib bo'lmaydi — yo'qlik belgilanganda sabab
    /// MAJBURIY (§5.5). Ma'lumot sifatiga eng kuchli ta'sir qiladigan bayroq.
    ///
    /// <para>
    /// Sukut bo'yicha <b>false</b> — bugungi xatti-harakat shunday. Yoqilishi
    /// o'qituvchining kunlik ishini o'zgartiradi, ya'ni buni maktab o'zi,
    /// ataylab qilishi kerak; migratsiya emas.
    /// </para>
    /// </summary>
    public bool MakeAttendanceReasonRequired { get; set; }

    /// <summary>
    /// Baholar qo'yilmagan darsni yopib bo'lmaydi (§5.5). Sukut bo'yicha
    /// <b>false</b> — yuqoridagi bilan bir xil sabab.
    /// </summary>
    public bool IsStudentGradeRequired { get; set; }

    /// <summary>
    /// Ota-ona kabinetida o'zlashtirish (baholar/progress) ko'rinadimi (§5.5).
    ///
    /// <para>
    /// Sukut bo'yicha <b>true</b> — bugun ota-onalar buni ko'rishadi va
    /// migratsiya mavjud xatti-harakatni o'zgartirmasligi kerak. Bayroq yomon
    /// chorakda uni VAQTINCHA o'chirish uchun kerak — hozir buning uchun
    /// deploy talab qilinardi.
    /// </para>
    /// </summary>
    public bool ShowLearningProgressInParentDashboard { get; set; } = true;

    // =======================================================================
    //  O'quv guruhlari — students-parity.md §2.1.4, §4.3 (cut-over).
    // =======================================================================

    /// <summary>
    /// Guruh darslari yoqilganmi — cut-over o'chirgichi (§4.3).
    ///
    /// <para>
    /// Sukut bo'yicha <b>false</b>. O'chiq paytda guruhlar va ularning ro'yxati
    /// yaratiladi, lekin guruhga tegishli jadval haftaga biriktirilmaydi —
    /// ya'ni birorta dars, jurnal, hisobot, maosh yoki turniket raqami
    /// o'zgarmaydi. Uni superadmin cut-over tekshiruvidan keyin yoqadi
    /// (audit bilan); migratsiya emas.
    /// </para>
    /// </summary>
    public bool GroupLessonsEnabled { get; set; }

    // =======================================================================
    //  Shartnoma raqami — students-parity.md §2.10 (K-6), §3.3.
    // =======================================================================

    /// <summary>
    /// Shartnoma raqamlash qoidasi (K-6):
    /// <see cref="ContractNumberMode"/> — <c>auto</c> | <c>manual</c>.
    ///
    /// <para>
    /// Sukut <b>auto</b>: tizim ketma-ket raqam beradi. Bu bugungi K-1
    /// reyestrining eng kam ajablanadigan xatti-harakati va §3.3 ning
    /// talabi. <c>manual</c> da raqamni xodim o'zi yozadi (davlat blankasi,
    /// eski qog'oz arxivi bilan davom etish).
    /// </para>
    /// <para>
    /// Raqamning O'ZI shu yerda emas — u <see cref="StudentContract.Number"/>
    /// da va uning qisman unikal indeksi ostida. Bu ustun faqat "raqamni kim
    /// beradi" degan savolga javob beradi.
    /// </para>
    /// </summary>
    public string ContractNumberMode { get; set; } = SchoolLms.Domain.ContractNumberMode.Auto;

    // =======================================================================
    //  Admission — admission-and-testing.md §5.13, §7.7.
    // =======================================================================

    /// <summary>
    /// Whether the finished public entrance-test page shows the candidate the
    /// per-question review with the correct answers (§7.7, open question 1).
    ///
    /// <para>
    /// Default <b>false</b>, on purpose: successive candidates sit papers drawn
    /// from one bank, so handing the key to each of them leaks the bank. When
    /// false the server does not even project the questions into the result.
    /// Turning it on is the school's explicit choice, not a migration's.
    /// </para>
    /// </summary>
    public bool AdmissionShowAnswersToCandidate { get; set; }
}

/// <summary><see cref="SchoolMeta.ContractNumberMode"/> qiymatlari (K-6).</summary>
public static class ContractNumberMode
{
    /// <summary>Tizim ketma-ket raqam beradi.</summary>
    public const string Auto = "auto";

    /// <summary>Raqamni xodim qo'lda kiritadi.</summary>
    public const string Manual = "manual";

    /// <summary>Baza CHECK cheklovi bilan BIR XIL ro'yxat.</summary>
    public static readonly string[] All = [Auto, Manual];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
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
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>Sinf ota-onalariga Telegram bot orqali yuborilgan e'lon (bir tomonlama xabar).</summary>
public class Broadcast
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Yuborish vaqtida shu sinfda Telegramda ro'yxatdan o'tgan ota-onalar soni.</summary>
    public int RecipientCount { get; set; }
    /// <summary>Telegram orqali muvaffaqiyatli yetkazilganlar soni.</summary>
    public int SentCount { get; set; }
}

/// <summary>Ilovaga (FCM push) yuborilgan bildirishnoma — tarix uchun.</summary>
public class PushMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qabul qiluvchi toifa yorlig'i (masalan "Ota-onalar — 9-A", "O'qituvchilar").</summary>
    public string Audience { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Maqsadli qurilma tokenlari soni.</summary>
    public int RecipientCount { get; set; }
    /// <summary>Muvaffaqiyatli yuborilgan push soni.</summary>
    public int SentCount { get; set; }
}

/// <summary>
/// Farzandni olib ketish so'rovi. Ota-ona ilovada "Farzandimni olishga keldim" bossa yaratiladi;
/// sinf rahbari ilovada "Qabul qildim" bossa Accepted bo'ladi. Har ikki tomonga push boradi.
/// </summary>
public class PickupRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>O'quvchi F.I.SH (nusxa — ko'rsatish uchun).</summary>
    public string StudentName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    /// <summary>So'rovchi (ota-ona/oila akkaunti) UserId — javob push'i shu yerga boradi.</summary>
    public string RequestedByUserId { get; set; } = string.Empty;
    /// <summary>pending | accepted</summary>
    public string Status { get; set; } = "pending";
    public string CreatedAt { get; set; } = string.Empty;
    public string? AcceptedAt { get; set; }
    public string? AcceptedByTeacherId { get; set; }
    /// <summary>Qabul qilgan (sinf rahbari) F.I.SH.</summary>
    public string? AcceptedByName { get; set; }
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
    public DateTime CreatedAt { get; set; } = AppClock.Now;
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
    public DateTime CreatedAt { get; set; } = AppClock.Now;

    // Eski (oddiy topshiriq) ustunlari — orqaga moslik uchun saqlanadi, yangi forma ishlatmaydi.
    public string ClassId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public string? Date { get; set; }
    public int? Period { get; set; }
    public string? TypeId { get; set; }

    public List<AssignmentMaterial> Materials { get; set; } = new();
    public List<TestQuestion> Questions { get; set; } = new();

    // =======================================================================
    //  students-parity.md §3.3 (Batch C, G-20) — §3.1 ning 7-bandi bilan
    //  AYNAN bir xil shakl.
    // =======================================================================

    /// <summary>
    /// <see cref="LessonOwnerKind"/> — topshiriq SINFGA beriladimi yoki
    /// o'quv GURUHIGA (G-20).
    ///
    /// <para>
    /// <c>group</c> bo'lganda guruh id'si <see cref="ClassIds"/> (va eski
    /// <see cref="ClassId"/>) ichida saqlanadi — beshta dars jadvalidagi
    /// bilan bir xil qoida: alohida nullable `group_id` ustuni har bir
    /// so'rovni ikkita ustun bilan ishlashga majbur qilardi.
    /// </para>
    /// <para>
    /// Mavjud HAR BIR topshiriq va yangi yozilgan har bir qator
    /// <c>class</c> — ya'ni bugungi LMS xatti-harakati o'zgarmaydi.
    /// </para>
    /// </summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
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
    /// <summary>Admin paneldagi qo'ng'iroq ro'yxati oxirgi marta o'qilgan vaqt.
    /// Shundan keyingi voqealar "yangi" deb sanaladi (null = hech qachon o'qilmagan).</summary>
    public DateTime? NotificationsReadAt { get; set; }
    /// <summary>Oxirgi yangilanish vaqti (UTC, ISO).</summary>
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
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
    /// <summary>Qurilma nomi (masalan "Samsung A52", "iPhone 13") — ilova yuboradi.</summary>
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Push provayder ilova identifikatori (app_id) — ilova yuboradi.</summary>
    public string AppId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public DateTime LastSeenAt { get; set; } = AppClock.Now;
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
    public DateTime UploadedAt { get; set; } = AppClock.Now;
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
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Fan ichidagi modullar (Sinf → Fan → Modul → Mavzu).</summary>
    public ICollection<LmsModule> Modules { get; set; } = new List<LmsModule>();
}

/// <summary>LMS moduli — fan ichidagi mavzular guruhi (Sinf → Fan → Modul → Mavzu).</summary>
public class LmsModule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public LmsSubject Subject { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Tartib raqami (1 dan). Mavzular ochilishi modul→mavzu tartibida hisoblanadi.</summary>
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public ICollection<LmsTopic> Topics { get; set; } = new List<LmsTopic>();
}

/// <summary>LMS mavzusi (dars) — bitta bo'lim: video + materiallar + matn. Modulga tegishli.</summary>
public class LmsTopic
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ModuleId { get; set; } = string.Empty;
    public LmsModule Module { get; set; } = null!;
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
    public DateTime CompletedAt { get; set; } = AppClock.Now;
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
    public DateTime SentAt { get; set; } = AppClock.Now;
    /// <summary>Telegram orqali muvaffaqiyatli yetkazildimi.</summary>
    public bool Delivered { get; set; }
    /// <summary>sent</summary>
    public string Status { get; set; } = "sent";
}
