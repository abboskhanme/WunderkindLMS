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
    DbSet<FinanceTransaction> FinanceTransactions { get; }
    DbSet<MonthlyCharge> MonthlyCharges { get; }
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

    // LMS (Ta'lim)
    DbSet<LmsSubject> LmsSubjects { get; }
    DbSet<LmsTopic> LmsTopics { get; }
    DbSet<LmsMaterial> LmsMaterials { get; }
    DbSet<LmsProgress> LmsProgresses { get; }

    int SaveChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
