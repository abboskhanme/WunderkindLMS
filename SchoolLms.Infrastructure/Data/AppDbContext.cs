using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Maktab ma'lumotlar bazasi (bitta maktab — multi-tenant emas).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    // Maktab ma'lumotlari
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherAttendance> TeacherAttendances => Set<TeacherAttendance>();
    public DbSet<TurnstileEvent> TurnstileEvents => Set<TurnstileEvent>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<BusLocation> BusLocations => Set<BusLocation>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadStage> LeadStages => Set<LeadStage>();
    public DbSet<Dish> Dishes => Set<Dish>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<QuarterGrade> QuarterGrades => Set<QuarterGrade>();
    public DbSet<LessonNote> LessonNotes => Set<LessonNote>();
    public DbSet<ScheduleTemplate> ScheduleTemplates => Set<ScheduleTemplate>();
    public DbSet<WeekAssignment> WeekAssignments => Set<WeekAssignment>();
    public DbSet<AbsenceReason> AbsenceReasons => Set<AbsenceReason>();
    public DbSet<QuarterPeriod> Quarters => Set<QuarterPeriod>();
    public DbSet<LessonTime> LessonTimes => Set<LessonTime>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<DisciplineReason> DisciplineReasons => Set<DisciplineReason>();
    public DbSet<DisciplinePoint> DisciplinePoints => Set<DisciplinePoint>();
    public DbSet<EvaluationType> EvaluationTypes => Set<EvaluationType>();
    public DbSet<EvaluationGrade> EvaluationGrades => Set<EvaluationGrade>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SchoolMeta> SchoolMeta => Set<SchoolMeta>();
    public DbSet<SchoolYearArchive> SchoolYearArchives => Set<SchoolYearArchive>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();
    public DbSet<PushMessage> PushMessages => Set<PushMessage>();
    public DbSet<PickupRequest> PickupRequests => Set<PickupRequest>();
    public DbSet<TelegramRegistration> TelegramRegistrations => Set<TelegramRegistration>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentType> AssignmentTypes => Set<AssignmentType>();
    public DbSet<AssignmentMaterial> AssignmentMaterials => Set<AssignmentMaterial>();
    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();

    // Vasiylar (SPEC §3.2) va Telegram Mini App bog'lanishi (SPEC §6 Faza 3).
    // Konfiguratsiya: GuardianModel.cs.
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<TelegramAccount> TelegramAccounts => Set<TelegramAccount>();
    public DbSet<TelegramLinkCode> TelegramLinkCodes => Set<TelegramLinkCode>();
    public DbSet<ChatRead> ChatReads => Set<ChatRead>();

    // LMS (Ta'lim)
    public DbSet<LmsSubject> LmsSubjects => Set<LmsSubject>();
    public DbSet<LmsModule> LmsModules => Set<LmsModule>();
    public DbSet<LmsTopic> LmsTopics => Set<LmsTopic>();
    public DbSet<LmsMaterial> LmsMaterials => Set<LmsMaterial>();
    public DbSet<LmsProgress> LmsProgresses => Set<LmsProgress>();

    // Moliya (billing) — SPEC §3.7, P1-04. Eski yassi moliya jadvallari P1-21 da
    // olib tashlandi; bu to'plam YAGONA pul manbai. Konfiguratsiya BillingModel.cs da.
    public DbSet<FeeCategory> FeeCategories => Set<FeeCategory>();
    public DbSet<StudentSubscription> StudentSubscriptions => Set<StudentSubscription>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<BillingSettings> BillingSettings => Set<BillingSettings>();

    // Tungi tekshiruv bayroqlari — SPEC §4.6, P1-14. Konfiguratsiya: AnomalyModel.cs.
    public DbSet<FinanceAnomalyFlag> FinanceAnomalyFlags => Set<FinanceAnomalyFlag>();

    // Ikkinchi to'lqin (docs/modules/existing-module-gaps.md): qarzdorlar ish oqimi
    // (§3.5), sertifikatlar (§2.3) va arxivlash sabablari katalogi (§2.2).
    // Konfiguratsiya: ParityModel.cs. Hozircha ilovadan HECH KIM o'qimaydi —
    // sxema oldin keladi, ekranlar keyin.
    public DbSet<DebtorStatus> DebtorStatuses => Set<DebtorStatus>();
    public DbSet<DebtorAction> DebtorActions => Set<DebtorAction>();
    public DbSet<CertificateType> CertificateTypes => Set<CertificateType>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<StudentArchiveReason> StudentArchiveReasons => Set<StudentArchiveReason>();

    // O'quv guruhlari va sinf a'zoligi (docs/modules/students-parity.md §3.1).
    // Konfiguratsiya: StudyGroupModel.cs. Hozircha ilovadan HECH KIM o'qimaydi —
    // `students.class_name` haqiqat manbai bo'lib qoladi (1-slice xizmati chiqmaguncha).
    public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
    public DbSet<StudyGroupClass> StudyGroupClasses => Set<StudyGroupClass>();
    public DbSet<StudyGroupTeacher> StudyGroupTeachers => Set<StudyGroupTeacher>();
    public DbSet<StudyGroupMember> StudyGroupMembers => Set<StudyGroupMember>();
    public DbSet<ClassMembership> ClassMemberships => Set<ClassMembership>();

    /// <inheritdoc />
    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Login (Email) unikal — DB darajasidagi unique indeks TOCTOU poyga holatida ham dublikatni
        // bloklaydi (parallel ro'yxatdan o'tish login'ni buzmasin).
        b.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();

        // Eski (Faza 0) pul maydonlari uchun aniqlik. Yangi moliya jadvallari
        // numeric(14,2) da — BillingModel.cs ga qarang.
        b.Entity<SchoolClass>().Property(c => c.MonthlyFee).HasPrecision(18, 2);
        b.Entity<Teacher>().Property(t => t.Salary).HasPrecision(18, 2);

        // ScheduleTemplate -> Lessons (egasiz/owned emas, oddiy bog'liqlik)
        b.Entity<ScheduleTemplate>()
            .HasMany(t => t.Lessons)
            .WithOne()
            .HasForeignKey(l => l.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tez-tez ishlatiladigan filtrlar uchun indekslar
        b.Entity<JournalEntry>().HasIndex(e => new { e.ClassId, e.SubjectId, e.Quarter });
        b.Entity<QuarterGrade>().HasIndex(g => new { g.ClassId, g.SubjectId, g.Quarter, g.StudentId }).IsUnique();
        b.Entity<LessonNote>().HasIndex(e => new { e.ClassId, e.SubjectId, e.Quarter });
        b.Entity<WeekAssignment>().HasIndex(e => new { e.ClassId, e.Quarter });
        b.Entity<Dish>().HasIndex(d => d.Date);
        b.Entity<ScheduleTemplate>().HasIndex(t => t.ClassId);
        b.Entity<AuditLog>().HasIndex(a => new { a.EntityType, a.EntityId });
        b.Entity<AuditLog>().HasIndex(a => a.Timestamp);
        b.Entity<AuditLog>().HasIndex(a => a.StudentId);
        b.Entity<AuditLog>().HasIndex(a => a.TeacherId);

        // Turniket hodisalari — (qurilma ID, hodisa vaqti).
        //
        // NEGA KERAK. Uchala turniket hisoboti ham (`TurnstileAnalyticsQueries`)
        // `event_at` oralig'i bo'yicha filtrlaydi, `TurnstileService.IngestAsync`
        // esa dublikatni aniqlash uchun butun jadvaldan `device_user_id|event_at`
        // juftliklarini o'qiydi. Indekssiz ikkalasi ham TO'LIQ SKAN edi, shuning
        // uchun hisobotlardagi oraliq `MaxRangeDays = 92` kun bilan cheklab
        // qo'yilgan. Ustun tartibi ataylab shunday: (1) dedup so'rovi aynan shu
        // ikki ustunni o'qiydi va endi u index-only scan bo'ladi, (2) bitta
        // xodim/o'quvchining o'tish tarixi — `device_user_id` bo'yicha aniq
        // qidiruv.
        //
        // `event_at` — `text` (ISO "yyyy-MM-ddTHH:mm:ss"), ya'ni leksikografik
        // tartib xronologik tartib bilan mos tushadi va oraliq so'rovi btree'da
        // to'g'ri ishlaydi.
        b.Entity<TurnstileEvent>().HasIndex(e => new { e.DeviceUserId, e.EventAt });

        // Xabarlar (chat/e'lon/telegram)
        b.Entity<ChatMessage>().HasIndex(m => new { m.ClassName, m.CreatedAt });
        b.Entity<Broadcast>().HasIndex(x => new { x.ClassName, x.CreatedAt });
        b.Entity<TelegramRegistration>().HasIndex(r => new { r.StudentId, r.ChatId }).IsUnique();
        b.Entity<TelegramRegistration>().HasIndex(r => r.ChatId);

        // Qo'shimcha topshiriqlar
        b.Entity<Assignment>().HasIndex(a => new { a.ClassId, a.SubjectId, a.Quarter });
        b.Entity<Assignment>().HasIndex(a => a.CreatedByUserId);
        b.Entity<Assignment>()
            .HasMany(a => a.Materials).WithOne().HasForeignKey(m => m.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<Assignment>()
            .HasMany(a => a.Questions).WithOne().HasForeignKey(q => q.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<AssignmentSubmission>().HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();
        b.Entity<AssignmentSubmission>().HasIndex(x => x.StudentId);

        // Foydalanuvchi sozlamalari va qurilma tokenlari
        b.Entity<UserSettings>().HasKey(s => s.UserId);
        b.Entity<DeviceToken>().HasIndex(d => d.Token).IsUnique();
        b.Entity<DeviceToken>().HasIndex(d => d.UserId);

        // Shartnomalar
        b.Entity<ContractTemplate>().HasIndex(t => t.Target);
        b.Entity<Contract>().HasIndex(c => new { c.Target, c.RecipientKey });

        // Boshqaruv: filiallar va taklif/shikoyatlar
        b.Entity<Feedback>().HasIndex(f => new { f.Status, f.CreatedAt });

        // LMS (Ta'lim)
        b.Entity<LmsSubject>().HasIndex(s => s.ClassId);
        b.Entity<LmsSubject>()
            .HasMany(s => s.Modules).WithOne(m => m.Subject)
            .HasForeignKey(m => m.SubjectId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LmsModule>()
            .HasMany(m => m.Topics).WithOne(t => t.Module)
            .HasForeignKey(t => t.ModuleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LmsTopic>()
            .HasMany(t => t.Materials).WithOne(m => m.Topic)
            .HasForeignKey(m => m.TopicId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LmsTopic>()
            .HasMany(t => t.Progresses).WithOne(p => p.Topic)
            .HasForeignKey(p => p.TopicId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LmsProgress>()
            .HasIndex(p => new { p.StudentId, p.TopicId }).IsUnique();
        b.Entity<LmsProgress>()
            .HasIndex(p => p.StudentId);
        b.Entity<LmsModule>()
            .HasIndex(m => new { m.SubjectId, m.Order });
        b.Entity<LmsTopic>()
            .HasIndex(t => new { t.ModuleId, t.Order });

        // ----- Moliya (billing) — SPEC §3.7, §4 -----
        // Aniqlik, unikal indekslar, FK'lar va check constraint'lar alohida faylda
        // (BillingModel.cs): Faza 1.C da beshta agent moliya kodini parallel yozadi,
        // shu fayl ularning umumiy konflikt maydoniga aylanmasin.
        BillingModel.Apply(b);

        // ----- Tungi tekshiruv (SPEC §4.6) va audit_log ning jsonb ustunlari -----
        // Yana bitta alohida fayl, yuqoridagi bilan bir xil sabab: P1-14 moliya
        // kodini boshqa agentlar bilan parallel yozadi.
        AnomalyModel.Apply(b);

        // ----- Vasiylar (SPEC §3.2) va Telegram Mini App (SPEC §6 Faza 3) -----
        // Uchinchi alohida fayl, yuqoridagilar bilan bir xil sabab.
        GuardianModel.Apply(b);

        // ----- Ikkinchi to'lqin (docs/modules/existing-module-gaps.md) -----
        // Qarzdorlar ish oqimi (§3.5), sertifikatlar (§2.3), arxivlash sabablari
        // (§2.2), `discipline_reasons` ning yangi ustunlari (§6.3) va
        // `school_meta` ning to'rtta bayrog'i (§5.5). To'rtinchi alohida fayl.
        ParityModel.Apply(b);

        // ----- O'quv guruhlari va sinf a'zoligi (students-parity.md §3.1) -----
        // Beshinchi alohida fayl. `owner_kind` (beshta dars jadvali),
        // `subjects.is_groupable` va `school_meta.group_lessons_enabled` ham shu yerda.
        StudyGroupModel.Apply(b);

        // ----- PostgreSQL: vaqt turi -----
        // Tizim sanalarni Toshkent "devor soati" sifatida saqlaydi (AppClock.Now — Kind=Unspecified),
        // UTC sifatida emas. Npgsql sukut bo'yicha DateTime'ni `timestamptz` ga moslaydi va
        // Kind=Unspecified qiymatni yozishdan bosh tortadi. Shuning uchun BARCHA DateTime ustunlarini
        // `timestamp without time zone` ga o'tkazamiz — bu mavjud semantikaga aynan mos keladi.
        //
        // MOLIYA ISTISNOSI: yangi billing entity'lari `DateTimeOffset` ishlatadi (SPEC §7 —
        // `timestamptz`). Quyidagi tsikl faqat `DateTime` ni qidiradi, shuning uchun ularga
        // TEGMAYDI. Moliyada mintaqasiz vaqt yaramaydi: smena chegarasi va `received_at`
        // ofsetsiz bo'lsa Z-hisobotni qayta hisoblab bo'lmaydi.
        foreach (var entityType in b.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetColumnType("timestamp without time zone");
    }
}
