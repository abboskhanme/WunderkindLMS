using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.Abstractions;

/// <summary>
/// Ma'lumotlar bazasi konteksti abstraksiyasi. Application qatlamidagi xizmatlar
/// (Services) konkret <c>AppDbContext</c> (Infrastructure) o'rniga shu interfeysga
/// bog'lanadi — bu bog'liqlik yo'nalishini ichkariga (Domain/Application tomon)
/// saqlaydi. Infrastructure'dagi <c>AppDbContext</c> shu interfeysni implement qiladi,
/// DI esa <c>IAppDbContext</c> ni o'sha scoped <c>AppDbContext</c> ga ulaydi.
/// </summary>
public interface IAppDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<Student> Students { get; }
    DbSet<Teacher> Teachers { get; }
    DbSet<TeacherAttendance> TeacherAttendances { get; }
    DbSet<TurnstileEvent> TurnstileEvents { get; }
    DbSet<Bus> Buses { get; }
    DbSet<BusLocation> BusLocations { get; }
    DbSet<Camera> Cameras { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<SchoolClass> Classes { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadStage> LeadStages { get; }
    DbSet<Dish> Dishes { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<QuarterGrade> QuarterGrades { get; }
    DbSet<LessonNote> LessonNotes { get; }
    DbSet<ScheduleTemplate> ScheduleTemplates { get; }
    DbSet<WeekAssignment> WeekAssignments { get; }
    DbSet<AbsenceReason> AbsenceReasons { get; }
    DbSet<QuarterPeriod> Quarters { get; }
    DbSet<LessonTime> LessonTimes { get; }
    DbSet<Holiday> Holidays { get; }
    DbSet<DisciplineReason> DisciplineReasons { get; }
    DbSet<DisciplinePoint> DisciplinePoints { get; }
    DbSet<EvaluationType> EvaluationTypes { get; }
    DbSet<EvaluationGrade> EvaluationGrades { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SchoolMeta> SchoolMeta { get; }
    DbSet<SchoolYearArchive> SchoolYearArchives { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<Broadcast> Broadcasts { get; }
    DbSet<PushMessage> PushMessages { get; }
    DbSet<PickupRequest> PickupRequests { get; }
    DbSet<TelegramRegistration> TelegramRegistrations { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<AssignmentType> AssignmentTypes { get; }
    DbSet<AssignmentMaterial> AssignmentMaterials { get; }
    DbSet<TestQuestion> TestQuestions { get; }
    DbSet<AssignmentSubmission> AssignmentSubmissions { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<ContractTemplate> ContractTemplates { get; }
    DbSet<Contract> Contracts { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Feedback> Feedbacks { get; }

    // ---------- Vasiylar (SPEC §3.2) va Telegram Mini App (SPEC §6 Faza 3) ----------
    // `students.parent_phone` HALI HAM bor va uni o'qiydigan kod o'zgarmadi;
    // bu to'plam uning ustiga ko'p-ko'pga bog'lanishni qo'shadi, o'rniga emas
    // (docs/PENDING_WIRING.md — `parent_phone` ni yopish alohida vazifa).
    DbSet<Guardian> Guardians { get; }
    DbSet<StudentGuardian> StudentGuardians { get; }
    DbSet<TelegramAccount> TelegramAccounts { get; }
    DbSet<TelegramLinkCode> TelegramLinkCodes { get; }
    DbSet<ChatRead> ChatReads { get; }

    // LMS (Ta'lim)
    DbSet<LmsSubject> LmsSubjects { get; }
    DbSet<LmsTopic> LmsTopics { get; }
    DbSet<LmsMaterial> LmsMaterials { get; }
    DbSet<LmsProgress> LmsProgresses { get; }

    // ---------- Moliya (billing) — SPEC §3.7, P1-04 ----------
    // Eski yassi moliya jadvallari (`finance_transactions`, `monthly_charges`) va
    // o'quvchi qatoridagi saqlangan qoldiq P1-21 da olib tashlandi — pul haqidagi
    // YAGONA manba shu to'plam.
    //
    // DIQQAT: `Payments`, `PaymentAllocations`, `LedgerEntries` — FAQAT INSERT.
    // `app_rw` rolida ularga UPDATE/DELETE huquqi yo'q (SPEC §4.1), shuning uchun
    // bu DbSet'lardan olingan entity'ni o'zgartirib `SaveChanges` qilish ishlab
    // turgan ilovada 42501 bilan YIQILADI — tuzatish `reversal_of` orqali bo'ladi.
    DbSet<FeeCategory> FeeCategories { get; }
    DbSet<StudentSubscription> StudentSubscriptions { get; }
    DbSet<Discount> Discounts { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<CashShift> CashShifts { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<BillingSettings> BillingSettings { get; }

    /// <summary>
    /// Tungi tekshiruv bayroqlari (SPEC §4.6, P1-14). O'CHIRIB BO'LMAYDI:
    /// <c>app_rw</c> da DELETE yo'q, UPDATE esa faqat uchta "yopish" ustuniga
    /// (<c>Migrations/Sql/anomaly_guards.sql</c>).
    /// </summary>
    DbSet<FinanceAnomalyFlag> FinanceAnomalyFlags { get; }

    // ---------- Kassa stoli (docs/modules/finance-parity.md §3.1, A to'plami) ----------
    // Sxema oldin keladi, ekranlar keyin: bu uchtasini hozircha HECH BIR xizmat
    // o'qimaydi (S2 — kassa, S3 — qaytarim).
    //
    // DIQQAT: uchalasi ham FAQAT INSERT — `app_rw` da UPDATE/DELETE yo'q
    // (`Migrations/Sql/finance_parity_guards.sql`), ya'ni bu DbSet'lardan olingan
    // entity'ni o'zgartirib `SaveChanges` qilish 42501 bilan YIQILADI.

    /// <summary>
    /// Kassadan pul chiqishi (F1.04). Tuzatish faqat <c>ReversalOf</c> bilan.
    /// </summary>
    DbSet<CashHandover> CashHandovers { get; }

    /// <summary>
    /// O'quvchiga qaytarim (F1.05). Yagona ruxsat etilgan UPDATE — to'rtta
    /// "qaror" ustuni (<c>approved_by</c>, <c>approved_at</c>,
    /// <c>cash_shift_id</c>, <c>rejected_reason</c>), va u ham FAQAT BIR MARTA:
    /// qaror qo'yilgach <c>student_refunds_locked</c> trigger'i har qanday
    /// keyingi UPDATE ni 23514 bilan rad etadi.
    /// </summary>
    DbSet<StudentRefund> StudentRefunds { get; }

    /// <summary>
    /// Chiqim hujjatlari (F1.08) — pul yozuvining dalili. SELECT va INSERT,
    /// boshqa hech narsa: dalilni almashtirish summani o'zgartirish bilan bir
    /// og'irlikda.
    /// </summary>
    DbSet<ExpenseAttachment> ExpenseAttachments { get; }

    // ---------- Bonus / jarima (finance-parity.md §3.2, Batch B, F11.01/F11.02) ----------
    // `hr_employees` hali yo'q (HR-01/02/03 qurilmagan) — xodim identifikatsiyasi
    // `TeacherId`/`UserId` orqali, `hr_employees` o'zi ishlatadigan naqsh bilan
    // bir xil (`PayrollAdjustments.cs` boshidagi izoh).

    /// <summary>Sabab katalogi (F11.02) — moliyaviy emas, to'liq CRUD.</summary>
    DbSet<AdjustmentReason> AdjustmentReasons { get; }

    /// <summary>
    /// Bonus/jarima registri (F11.01). FAQAT INSERT — `app_rw` da UPDATE/DELETE
    /// yo'q (<c>payroll_adjustments_guards.sql</c>). Tuzatish faqat <c>ReversalOf</c> bilan.
    /// </summary>
    DbSet<PayrollAdjustment> PayrollAdjustments { get; }

    // ---------- Ikkinchi to'lqin (docs/modules/existing-module-gaps.md) ----------
    // Sxema oldin keladi, ekranlar keyin: bu to'plamni hozircha HECH BIR xizmat
    // o'qimaydi. Ular shu yerda ro'yxatda turibdi, chunki ketma-ket keladigan
    // uchta modul (qarzdorlar §3.5, sertifikatlar §2.3, arxiv sabablari §2.2)
    // aks holda shu bitta faylga uch marta tegishga majbur bo'lardi.
    DbSet<DebtorStatus> DebtorStatuses { get; }
    DbSet<DebtorAction> DebtorActions { get; }
    DbSet<CertificateType> CertificateTypes { get; }
    DbSet<Certificate> Certificates { get; }
    DbSet<StudentArchiveReason> StudentArchiveReasons { get; }

    // ---------- O'quv guruhlari va sinf a'zoligi (students-parity.md §3.1) ----------
    // Sxema oldin keladi (M-slice), xizmatlar keyin (1-slice). `class_memberships`
    // migratsiya lahzasida `students.class_name` dan to'ldirilgan NUSXA: a'zolik
    // xizmati ikkalasini birga yuritmaguncha haqiqat manbai `class_name`.
    DbSet<StudyGroup> StudyGroups { get; }
    DbSet<StudyGroupClass> StudyGroupClasses { get; }
    DbSet<StudyGroupTeacher> StudyGroupTeachers { get; }
    DbSet<StudyGroupMember> StudyGroupMembers { get; }
    DbSet<ClassMembership> ClassMemberships { get; }

    // ---------- O'quvchi kartochkasi (students-parity.md §3.2) ----------
    DbSet<StudentStatus> StudentStatuses { get; }
    DbSet<StudentComment> StudentComments { get; }
    DbSet<StudentContract> StudentContracts { get; }
    DbSet<Room> Rooms { get; }

    // ---------- O'quv bo'limi pariteti, P2 (students-parity.md §3.3) ----------
    // Sxema oldin keladi, ekranlar keyin: bu uchtasini hozircha HECH BIR
    // xizmat o'qimaydi. Hammasi oddiy CRUD — moliyaviy jadval emas, ya'ni
    // `app_rw` da to'liq huquq bor (`students_parity_p2_guards.sql`).

    /// <summary>
    /// Sertifikatning QO'SHIMCHA fanlari (Z-3). `certificates.subject_id`
    /// ("asosiy fan") JOYIDA qoladi va migratsiya shu jadvalni undan BIR
    /// MARTA to'ldirgan — ikkalasini birga yuritish sertifikat xizmatining ishi.
    /// </summary>
    DbSet<CertificateSubject> CertificateSubjects { get; }

    /// <summary>
    /// O'quvchining turlangan joylashuvlari (L-2) — har turdan bittadan.
    /// Eski `students.latitude/longitude/location_address` ustunlari
    /// TEGILMAGAN va hali ham o'qiladi.
    /// </summary>
    DbSet<StudentLocation> StudentLocations { get; }

    /// <summary>
    /// Foydalanuvchi × ekran jadval ko'rinishi (X-1). Mavjud
    /// <see cref="UserSettings"/> (foydalanuvchiga bitta qator) bilan
    /// ALMASHTIRILMAYDI — u til/tema uchun, bu ustunlar uchun.
    /// </summary>
    DbSet<UserTableSetting> UserTableSettings { get; }

    int SaveChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Aniq tranzaksiya ochadi — bir nechta <c>SaveChanges</c> ni BITTA atomar
    /// amalga birlashtirish uchun.
    ///
    /// <para>
    /// P1-07 da qo'shildi va moliya uchun MAJBURIY. To'lov qabul qilish
    /// (P1-11) to'rtta narsani yozadi: <c>payments</c>, <c>payment_allocations</c>,
    /// hisob-faktura statusi va ledger yozuvlari. <c>LedgerService.PostAsync</c>
    /// o'z <c>SaveChanges</c> ini chaqiradi, ya'ni tranzaksiyasiz bu ikki
    /// alohida commit bo'lardi — orada jarayon o'lsa, bazada LEDGERSIZ TO'LOV
    /// qolardi. Pulda bunday holat tuzatib bo'lmaydigan farq beradi.
    /// </para>
    /// </summary>
    Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default);
}
