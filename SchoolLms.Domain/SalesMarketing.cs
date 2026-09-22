namespace SchoolLms.Domain;

// ===========================================================================
//  Savdo va marketing — ariza (survey) formasi va yangiliklar.
//  Spetsifikatsiya: docs/modules/sales-marketing.md §4. Vazifa: SM-1.
// ===========================================================================
//
//  NEGA ALOHIDA FAYL (Entities.cs ga qo'shilmagan)
//  ----------------------------------------------
//  `Billing.cs`, `Guardians.cs`, `TransactionTypes.cs` bilan bir xil sabab:
//  `Entities.cs` repozitoriyadagi eng ko'p konflikt beradigan fayl va bu
//  modulni o'nga yaqin agent parallel quradi (§7.2). `Entities.cs` shu
//  migratsiyadan FAQAT `Lead` ning uchta additive xossasini oladi (§4.4),
//  boshqa hech nimani.
//
//  TIPLAR — `Billing.cs` NAQSHI
//  ----------------------------
//      id            -> uuid            (Guid)
//      vaqt belgisi  -> timestamptz     (DateTimeOffset)
//      `users`/`leads`/`lead_stages` ga havola -> text (eski jadvallar PK'si)
//
//  DIQQAT — VAQT TIPI ARALASH, VA BU ATAYLAB (§4 ning ogohlantirishi)
//  -------------------------------------------------------------------
//  `AppDbContext.OnModelCreating` oxirida BARCHA `DateTime` ustunlari
//  `timestamp without time zone` ga majburlanadi (eski kod Toshkent "devor
//  soati"ni saqlaydi). Shu fayldagi UCHTA YANGI jadval esa `DateTimeOffset`
//  ishlatadi — ya'ni `timestamptz`, `default now()` — chunki ular yangi
//  modul va o'sha tsikl faqat `DateTime` ni qidiradi.
//  `Lead.CreatedAt` esa buning AKSI: u ESKI entity'dagi xossa,
//  `Broadcast.CreatedAt` bilan yonma-yon turadi va `DateTime?` bo'lib
//  qoladi. Buni "tuzatmang" — bitta jadvalda ikki xil vaqt semantikasi
//  bo'lib qolardi.
//
//  MOLIYAVIY EMAS — TO'LIQ CRUD
//  ----------------------------
//  Uchala jadvalda ham summa, jurnal (ledger) yozuvi yoki kvitansiya yo'q.
//  `docs/SPEC.md` §4.1 ning o'zgarmaslik qoidasi bu yerga TEGISHLI EMAS va
//  `deploy/init-roles.sql` §5 ro'yxatiga bu nomlar QO'SHILMAYDI —
//  `sales_marketing_guards.sql` da faqat GRANT bor, birorta REVOKE yo'q
//  (§4.5).

/// <summary>
/// Lid qayerdan kelgani (<see cref="Lead.Source"/>). Ro'yxat ikkita qiymatda
/// YOPIQ — baza darajasida ham (<c>ck_leads_source</c>). Uchinchi qiymat
/// (masalan <c>telegram</c>) qo'shilsa: shu yerga bitta qator, migratsiyaga
/// bitta CHECK va voronka yorliqlari jadvaliga bitta qator (§4.4).
/// </summary>
public static class LeadSource
{
    /// <summary>Xodim kanban doskasida o'z qo'li bilan kiritgan.</summary>
    public const string Manual = "manual";

    /// <summary>Ommaviy ariza formasi (<c>/ariza/:slug</c>) orqali kelgan.</summary>
    public const string Survey = "survey";

    public static readonly IReadOnlyList<string> All = [Manual, Survey];
}

/// <summary>
/// Topshirilgan arizaning holati (<see cref="SurveySubmission.Status"/>).
/// </summary>
public static class SurveySubmissionStatus
{
    /// <summary>Lid yaratildi — <see cref="SurveySubmission.LeadId"/> to'ldirilgan.</summary>
    public const string Lead = "lead";

    /// <summary>
    /// Takror: 24 soat ichida shu ariza bo'yicha aynan shu telefon va shu
    /// o'quvchi nomi bilan yozuv allaqachon bor (§2.6 4-qadam). Qator BARIBIR
    /// saqlanadi — ota-ona nima yozganining isboti yo'qolmasin — lekin
    /// ikkinchi lid YARATILMAYDI.
    /// </summary>
    public const string Duplicate = "duplicate";

    public static readonly IReadOnlyList<string> All = [Lead, Duplicate];
}

/// <summary>
/// O'quvchi jinsi — ommaviy ariza formasida va <see cref="Lead.Gender"/> da.
/// Ikkita qiymatda YOPIQ: dizayni muzlatilgan <c>LeadCard.tsx</c> uni
/// <c>Record&lt;Gender,string&gt;</c> orqali chizadi, uchinchi qiymat esa
/// o'sha kartochkada bo'sh satr bo'lib chiqardi (§2.4, "Rule T").
/// </summary>
public static class SurveyGender
{
    public const string Male = "male";
    public const string Female = "female";

    public static readonly IReadOnlyList<string> All = [Male, Female];
}

/// <summary>
/// Ommaviy ariza formasi (<c>/ariza/{slug}</c>) — maktabga qabul uchun.
/// Har bir topshiriq <see cref="SurveySubmission"/> qatorini va (takror
/// bo'lmasa) <see cref="Lead"/> ni yaratadi.
/// </summary>
public class Survey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Havolaning o'qiladigan qismi: <c>qabul-2027</c>. Kichik harf,
    /// <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>, 3–60 belgi (D1). Unikal —
    /// <c>ux_surveys_slug</c> <c>lower(slug)</c> ustida.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Ariza nomi — sahifaning sarlavhasi va Rule N dagi izoh matni.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Sarlavha ostidagi qo'shimcha satr.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Banner rasmi (<c>/uploads/...</c>) — ixtiyoriy.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Taklif hujjati (pdf/doc) — sahifada yuklab olish havolasi.</summary>
    public string? OfferUrl { get; set; }

    /// <summary>Yuborilgandan keyingi minnatdorchilik matni; <c>null</c> — sahifaning standart matni.</summary>
    public string? ThankYouText { get; set; }

    /// <summary>
    /// Lid qaysi kanban ustuniga tushadi (<see cref="LeadStage"/> id'si).
    /// <c>on delete set null</c>: ustunni o'chirish maktab uchun oddiy ish,
    /// keyin Rule S 2-qadami (eng kichik <c>Order</c>) ishlaydi (§4.1).
    /// </summary>
    public string? StageId { get; set; }

    /// <summary>O'quvchi ismi so'ralsinmi? O'chirilsa lid "{ota-ona} — farzandi" nomi bilan yaratiladi (§2.4).</summary>
    public bool ShowStudentFirstNameInput { get; set; } = true;

    /// <summary>O'quvchi familiyasi so'ralsinmi?</summary>
    public bool ShowStudentLastNameInput { get; set; } = true;

    /// <summary>O'quvchi telefoni so'ralsinmi? Bu qiymatning `Lead` da ustuni yo'q — izohga yoziladi (Rule N).</summary>
    public bool ShowStudentPhoneNumberInput { get; set; }

    /// <summary>
    /// Sinf so'ralsinmi? Rule T bo'yicha O'CHIRIB BO'LMAYDI:
    /// <c>leads.target_grade</c> — <c>not null</c> int, va <c>0</c> allaqachon
    /// "nol sinf" degani, ya'ni "noma'lum" o'rnida ishlatib bo'lmaydi.
    /// Baza darajasida: <c>ck_surveys_required_toggles</c>.
    /// </summary>
    public bool ShowStudentGradeInput { get; set; } = true;

    /// <summary>
    /// Jins so'ralsinmi? Rule T bo'yicha O'CHIRIB BO'LMAYDI:
    /// <c>leads.gender</c> — ikki qiymatli <c>not null</c> ustun va uni
    /// dizayni MUZLATILGAN <c>LeadCard.tsx</c> chizadi (CLAUDE.md).
    /// Baza darajasida: <c>ck_surveys_required_toggles</c>.
    /// </summary>
    public bool ShowStudentGenderInput { get; set; } = true;

    /// <summary>
    /// Yopilgan ariza ommaviy sahifada 404 beradi (D8) — o'chirilgan forma
    /// emas. Arizada topshiriq bo'lsa uni o'chirib bo'lmaydi, faqat shu
    /// bayroqni tushirish mumkin (D7).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary><see cref="AppUser.Id"/> — <c>on delete restrict</c>.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Ommaviy formaga topshirilgan bitta ariza — ota-ona AYNAN nima yozgani.
/// Lid keyin tahrirlansa ham (yoki o'chirilsa ham) bu qator o'zgarmaydi:
/// "Topshirilgan arizalar" registri va suiste'molni tekshirish uchun.
/// </summary>
public class SurveySubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="Survey.Id"/> — <c>on delete restrict</c> (D7 ning asosi).</summary>
    public Guid SurveyId { get; set; }

    /// <summary>
    /// Yaratilgan (yoki takror holatda — mavjud) <see cref="Lead.Id"/>.
    /// <c>on delete set null</c>: lidni doskadan o'chirish ota-ona yozgan
    /// ma'lumotning ISBOTINI o'chirmasin (§4.2).
    /// </summary>
    public string? LeadId { get; set; }

    /// <summary><see cref="SurveySubmissionStatus"/>.</summary>
    public string Status { get; set; } = SurveySubmissionStatus.Lead;

    public string ParentFirstName { get; set; } = string.Empty;
    public string? ParentLastName { get; set; }

    /// <summary>Telefon — ota-ona YOZGANICHA (formatlanmagan).</summary>
    public string ParentPhone { get; set; } = string.Empty;

    /// <summary>
    /// <c>PhoneUtil.Key</c> — oxirgi 9 raqam. Takrorni aniqlash shu ustun
    /// bo'yicha ketadi (§2.6 4-qadam), chunki ota-ona bir safar
    /// "+998 90 ..." deb, ikkinchi safar "90..." deb yozadi.
    /// </summary>
    public string ParentPhoneKey { get; set; } = string.Empty;

    public string? StudentFirstName { get; set; }
    public string? StudentLastName { get; set; }
    public string? StudentPhone { get; set; }

    /// <summary>0..11 — <c>0</c> HAQIQIY sinf (nol sinf), "noma'lum" emas (§2.1).</summary>
    public short? StudentGrade { get; set; }

    /// <summary><see cref="SurveyGender"/>.</summary>
    public string? StudentGender { get; set; }

    /// <summary>
    /// Yuboruvchining IP'si. Bu — tizimda ommaviy foydalanuvchining IP'si
    /// saqlanadigan YAGONA joy: faqat suiste'molni tekshirish uchun, faqat
    /// admin detal panelida ko'rinadi va Excel eksportiga TUSHMAYDI (§4.2).
    /// </summary>
    public string? Ip { get; set; }

    /// <summary>Brauzer satri — yuqoridagi bilan bir xil maqsad va bir xil cheklov.</summary>
    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Maktab e'lon qilgan yangilik — <b>tarixi bilan</b>: e'lon qilingandan
/// keyin ham lentada turadi va bir oydan keyin ham o'qiladi.
///
/// <para>
/// <b>Nega <see cref="Broadcast"/> ga qo'shilmadi (N1).</b> `Broadcast` —
/// bitta sinfning ota-onalariga mail-merge xabar (qarzdorlik summasi bilan).
/// Yangilik esa butun maktabga, auditoriya bo'yicha va doimiy. Ikkalasini
/// birlashtirish yo mail-merge'ni yo'qotardi, yo har bir qarz eslatmasini
/// doimiy ommaviy postga aylantirardi.
/// </para>
///
/// <para>
/// <b>Nega sinf nomi <c>NewsItem</c>, <c>News</c> emas (§4.6).</b>
/// <c>News</c> ko'plik kabi o'qiladi va <c>DbSet&lt;NewsItem&gt; News</c>
/// xossasi bilan to'qnashardi.
/// </para>
/// </summary>
public class NewsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// ODDIY MATN, HTML emas (N2). WYSIWYG tanasi serverda tozalanishi va
    /// uchta klientda HTML sifatida chizilishi kerak bo'lardi — maktab
    /// e'lonidagi qalin shrift uchun stored XSS'ning uchta imkoniyati.
    /// Hamma joyda <c>whitespace-pre-line</c> bilan chiziladi.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Ixtiyoriy (N7): rasm topmaguncha e'lon qila olmaydigan maktab e'lon qilmaydi.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Auditoriya: xodimlar. API'da uchovi <c>audience[]</c> massiviga aylanadi (N5).</summary>
    public bool ForEmployee { get; set; }

    /// <summary>Auditoriya: ota-onalar.</summary>
    public bool ForParent { get; set; }

    /// <summary>Auditoriya: o'quvchilar.</summary>
    public bool ForStudent { get; set; }

    /// <summary>
    /// <c>null</c> — qoralama. KELAJAKDAGI vaqt hech qachon yozilmaydi (N3):
    /// bu repozitoriyada planlovchi yo'q, ya'ni kelajakdagi belgi hech qachon
    /// ishga tushmasdi.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary><see cref="AppUser.Id"/> — <c>on delete restrict</c>.</summary>
    public string AuthorId { get; set; } = string.Empty;

    /// <summary>
    /// Muallif nomining NUSXASI — aynan <see cref="Broadcast.SenderName"/>
    /// kabi: xodim nomini o'zgartirsa yoki hisobi o'chsa, kim e'lon
    /// qilgani qayta yozilib ketmasin (§4.3).
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Telegram tarqatmasi yuborilgan vaqt; <c>null</c> — yuborilmagan.</summary>
    public DateTimeOffset? TelegramSentAt { get; set; }

    /// <summary>Yuborish vaqtida topilgan chat id'lar soni (dublikatsiz — N6).</summary>
    public int TelegramRecipientCount { get; set; }

    /// <summary>Telegram muvaffaqiyatli qabul qilgan xabarlar soni.</summary>
    public int TelegramSentCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Yumshoq o'chirish (N8): Telegram orqali ketgan, keyin admin
    /// ro'yxatidan G'OYIB bo'lgan va matnini hech kim ayta olmaydigan
    /// yangilik — bu qo'ng'iroq qilinadigan nosozlik.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
