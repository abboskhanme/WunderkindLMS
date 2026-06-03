using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;
using SchoolLms.Domain.Platform;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Yagona (shared) baza: barcha maktablar ma'lumotlari + Control Plane (Owners/Tenants) shu yerda.
/// Har bir maktab qatori shadow <c>TenantId</c> ustuni bilan belgilanadi; global query filter
/// (<see cref="_tenant"/>'dagi joriy TenantId bo'yicha) avtomatik faqat o'sha maktab qatorlarini
/// ko'rsatadi. <c>SaveChanges</c> yangi qatorlarga TenantId qo'yadi va boshqa maktab qatorini
/// o'zgartirishni bloklaydi.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : DbContext(options), IAppDbContext
{
    private readonly ITenantContext _tenant = tenant;

    // Maktab ma'lumotlari (har biri TenantId bilan filtrlanadi)
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
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
    public DbSet<FinanceTransaction> FinanceTransactions => Set<FinanceTransaction>();
    public DbSet<MonthlyCharge> MonthlyCharges => Set<MonthlyCharge>();
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

    // LMS (Ta'lim)
    public DbSet<LmsSubject> LmsSubjects => Set<LmsSubject>();
    public DbSet<LmsTopic> LmsTopics => Set<LmsTopic>();
    public DbSet<LmsMaterial> LmsMaterials => Set<LmsMaterial>();
    public DbSet<LmsProgress> LmsProgresses => Set<LmsProgress>();

    // Control Plane (tenantga BOG'LIQ EMAS — filtrlanmaydi)
    public DbSet<PlatformOwner> Owners => Set<PlatformOwner>();
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Tenantga bog'liq bo'lmagan (filtrlanmaydigan) entity'lar.</summary>
    private static bool IsPlatformEntity(Type t) => t == typeof(PlatformOwner) || t == typeof(Tenant);

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Login (Email) BUTUN baza bo'ylab unikal (AccountFactory shu invariantni dastur darajasida
        // ta'minlaydi — maktab kodisiz login uchun). DB darajasidagi unique indeks TOCTOU poyga holatida
        // ham dublikatni bloklaydi (parallel ro'yxatdan o'tish login'ni buzmasin).
        b.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();

        // Pul maydonlari uchun aniqlik (SQL Server decimal(18,2))
        b.Entity<Student>().Property(s => s.Balance).HasPrecision(18, 2);
        b.Entity<Student>().Property(s => s.DiscountAmount).HasPrecision(18, 2);
        b.Entity<SchoolClass>().Property(c => c.MonthlyFee).HasPrecision(18, 2);
        b.Entity<FinanceTransaction>().Property(t => t.Amount).HasPrecision(18, 2);
        b.Entity<MonthlyCharge>().Property(c => c.Amount).HasPrecision(18, 2);
        b.Entity<MonthlyCharge>().Property(c => c.Discount).HasPrecision(18, 2);
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
        b.Entity<FinanceTransaction>().HasIndex(t => t.Date);
        b.Entity<MonthlyCharge>().HasIndex(c => new { c.StudentId, c.Month }).IsUnique();
        b.Entity<AuditLog>().HasIndex(a => new { a.EntityType, a.EntityId });
        b.Entity<AuditLog>().HasIndex(a => a.Timestamp);
        b.Entity<AuditLog>().HasIndex(a => a.StudentId);
        b.Entity<AuditLog>().HasIndex(a => a.TeacherId);

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
            .HasMany(s => s.Topics).WithOne(t => t.Subject)
            .HasForeignKey(t => t.SubjectId).OnDelete(DeleteBehavior.Cascade);
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
        b.Entity<LmsTopic>()
            .HasIndex(t => new { t.SubjectId, t.Order });

        // Control Plane jadvallari
        b.Entity<PlatformOwner>().HasIndex(o => o.Email).IsUnique();
        b.Entity<Tenant>().HasIndex(t => t.Slug).IsUnique();
        b.Entity<Tenant>().HasIndex(t => t.Status);
        b.Entity<Tenant>().Property(t => t.SubscriptionPrice).HasPrecision(18, 2);

        // ----- Multi-tenant: har maktab entity'siga shadow TenantId + global filter -----
        var apply = typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter),
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var et in b.Model.GetEntityTypes().ToList())
        {
            var clr = et.ClrType;
            if (IsPlatformEntity(clr) || et.IsOwned() || et.BaseType != null) continue;
            apply.MakeGenericMethod(clr).Invoke(this, [b]);
        }
    }

    /// <summary>Bir entity turiga shadow <c>TenantId</c> ustuni, indeks va global filter qo'shadi.</summary>
    private void ApplyTenantFilter<TEntity>(ModelBuilder b) where TEntity : class
    {
        b.Entity<TEntity>().Property<string>("TenantId").HasMaxLength(64).IsRequired().HasDefaultValue("");
        b.Entity<TEntity>().HasIndex("TenantId");
        // _tenant.TenantId query bajarilganda o'qiladi (parametr) — middleware o'rnatgan joriy maktab.
        b.Entity<TEntity>().HasQueryFilter(e => EF.Property<string>(e, "TenantId") == _tenant.TenantId);
    }

    public override int SaveChanges()
    {
        TagTenant();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TagTenant();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Yangi qatorlarga joriy TenantId qo'yadi; boshqa maktab qatorini o'zgartirishni bloklaydi.</summary>
    private void TagTenant()
    {
        var tid = _tenant.TenantId;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (IsPlatformEntity(entry.Entity.GetType())) continue;
            if (entry.Metadata.FindProperty("TenantId") is null) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    // Provisioning kabi hollarda chaqiruvchi TenantId'ni oldindan qo'ygan bo'lishi mumkin.
                    var cur = (string?)entry.Property("TenantId").CurrentValue;
                    if (string.IsNullOrEmpty(cur) && !string.IsNullOrEmpty(tid))
                        entry.Property("TenantId").CurrentValue = tid;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    var owner = (string?)entry.Property("TenantId").CurrentValue;
                    if (!string.IsNullOrEmpty(tid) && owner != tid)
                        throw new InvalidOperationException(
                            "Tenant chegarasi buzildi: boshqa maktab qatorini o'zgartirib bo'lmaydi.");
                    break;
            }
        }
    }
}
