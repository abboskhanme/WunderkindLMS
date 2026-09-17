using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// A'ZOLIK TARIXI hisobotlarda — <see cref="ClassAttainment"/> 5-bandi
/// (docs/modules/students-parity.md §2.1.6, G-15).
///
/// <para>
/// <b>Nima o'zgardi.</b> Ilgari hisobot a'zolikni LAHZA sifatida o'qirdi:
/// </para>
/// <list type="bullet">
///   <item>guruhdan CHIQQAN bolaning o'sha guruhdagi baholari yo'qolardi
///     (<c>CountsFor</c> faqat FAOL a'zoni tan olardi);</item>
///   <item>sinf ALMASHTIRGAN bolaning eski sinfdagi baholari umuman hech
///     qayerda ko'rinmasdi — eski sinf ro'yxatida u yo'q, yangi sinf esa
///     qatorlarni faqat o'z id'si bo'yicha o'qirdi.</item>
/// </list>
/// <para>
/// Endi ikkalasi ham bolaning BUGUNGI sinfi qatorida sanaladi, lekin FAQAT
/// a'zolik oynasi ichidagi kunlar uchun. Har test kutilgan ESKI raqamni ham,
/// YANGI raqamni ham yozib qo'yadi.
/// </para>
/// <para>
/// <b>Bu tuzatish o'chirgichdan MUSTAQIL.</b> Sinf tarixi guruhlardan oldin
/// ham bor edi, shuning uchun sinf yarmi <c>group_lessons_enabled</c> O'CHIQ
/// turganda ham tekshiriladi; guruh yarmi esa faqat yoqilganda ishlaydi va
/// o'chiq holatda hech narsa qo'shmasligi ALOHIDA tekshiriladi.
/// </para>
/// <para>
/// <b>Nega o'z bazasi.</b> O'chirgich butun maktab uchun BITTA qator; umumiy
/// bazada uni yoqib-o'chirish qo'shni test klassining natijasini siljitardi.
/// Har test o'z bazasini oladi, hovuzlar <see cref="DisposeAsync"/> da
/// tozalanadi — aks holda konteynerdagi <c>max_connections</c> tugaydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class MembershipHistoryTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. Sinf almashtirish — o'chirgich O'CHIQ (tuzatish undan mustaqil)
    // =====================================================================

    /// <summary>
    /// 1-oktyabrda 5-B dan 5-A ga o'tgan bola. 5-A ning o'zlashtirish ekrani:
    ///
    /// <list type="bullet">
    ///   <item>o'rtacha baho <b>3 → 4</b>: 5-B dagi "5" endi sanaladi
    ///     ((5 + 3) / 2), ilgari u umuman ko'rinmasdi;</item>
    ///   <item>davomat <b>100 → 67</b>: maxrajga 5-B ning sentyabr darslari
    ///     qo'shildi va o'sha kunlardagi davomatsizlik ham ko'rindi.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Sinf_almashtirgan_bolaning_eski_baholari_yangi_sinf_qatorida_sanaladi()
    {
        await using var db = await NewDbAsync("move_off");
        var w = await SeedAsync(db);

        var data = await PerformanceAsync(db, w.ClassAId);
        var mover = Row(data, w.Mover);

        Assert.Equal(4, mover.Average);         // ilgari: 3 (faqat 5-A dagi "3")
        Assert.Equal(67, mover.Attendance);     // ilgari: 100 (maxraj faqat 5-A dan edi)
    }

    /// <summary>
    /// ENG MUHIM KAFOLAT: eski sinf so'rov qamroviga kirgani bilan uning
    /// darslari BOSHQA bolalarning maxrajiga QO'SHILMAYDI. Sinf almashtirmagan
    /// sinfdoshning raqami — o'zgarishsiz.
    /// </summary>
    [Fact]
    public async Task Eski_sinf_qamrovga_kirsa_ham_boshqa_bolalarning_raqami_ozgarmaydi()
    {
        await using var db = await NewDbAsync("move_other");
        var w = await SeedAsync(db);

        var stayer = Row(await PerformanceAsync(db, w.ClassAId), w.Stayer);

        Assert.Equal(5, stayer.Average);        // faqat 5-A dagi "5"
        Assert.Equal(100, stayer.Attendance);   // maxraj — faqat 5-A ning bitta darsi
    }

    /// <summary>
    /// Bola ketgan sinf ham o'z raqamini saqlaydi: 5-B hisobotida ketgan
    /// bolaning qatori YO'Q (ro'yxat <c>class_name</c> bo'yicha) va 5-B da
    /// qolgan bolaning raqami o'zgarmagan.
    /// </summary>
    [Fact]
    public async Task Eski_sinf_hisobotida_ketgan_bolaning_qatori_yoq()
    {
        await using var db = await NewDbAsync("move_old");
        var w = await SeedAsync(db);

        var inB = await PerformanceAsync(db, w.ClassBId);

        Assert.Equal([w.BStayer], inB.Rows.Select(r => r.Student.Id));
        var bStayer = Row(inB, w.BStayer);
        Assert.Equal(4, bStayer.Average);
        Assert.Equal(100, bStayer.Attendance);
    }

    /// <summary>
    /// Shaxsiy hisobot ham eski sinfni ko'radi: o'rtacha <b>3 → 4</b> va
    /// qoldirilgan dars <b>0 → 1</b> (5-B dagi sababli qoldirish).
    /// </summary>
    [Fact]
    public async Task Oquvchi_hisoboti_eski_sinfdagi_baho_va_davomatsizlikni_qoshadi()
    {
        await using var db = await NewDbAsync("move_report");
        var w = await SeedAsync(db);
        var mover = await db.Students.SingleAsync(s => s.Id == w.Mover);

        var report = await StudentReportBuilder.BuildAsync(db, mover);

        Assert.Equal(4, report.Grades[w.MathId][1]);                        // ilgari: 3
        Assert.Equal(1, report.Attendance.MissedLessons.GetValueOrDefault(1)); // ilgari: 0
    }

    // =====================================================================
    //  2. Guruhdan chiqish — o'chirgich bilan
    // =====================================================================

    /// <summary>
    /// Guruhdan 1-oktyabrda chiqqan bola. O'chirgich O'CHIQ ekan guruh umuman
    /// ko'rinmaydi — bu cut-over kafolati.
    /// </summary>
    [Fact]
    public async Task Ochirgich_ochiq_guruhdan_chiqqan_bolaning_guruh_bahosi_korinmaydi()
    {
        await using var db = await NewDbAsync("left_off");
        var w = await SeedAsync(db);

        var data = await PerformanceAsync(db, w.ClassAId);
        Assert.DoesNotContain(w.EnglishId, data.Subjects.Select(s => s.Id));
        Assert.Equal(5, Row(data, w.Leaver).Average);
    }

    /// <summary>
    /// O'chirgich YOQILGAN: guruhdan chiqqan bolaning o'sha guruhda olgan
    /// bahosi sinf ekranida QAYTADI — o'rtacha <b>5 → 4</b> ((5 + 3) / 2).
    /// Ilgari <c>CountsFor</c> faqat FAOL a'zoni tan olgani uchun bu baho
    /// yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Ochirgich_yoqilgan_guruhdan_chiqqan_bolaning_bahosi_saqlanadi()
    {
        await using var db = await NewDbAsync("left_on");
        var w = await SeedAsync(db);
        await ClassAttainmentTests.SetGroupLessonsAsync(db, true);

        var data = await PerformanceAsync(db, w.ClassAId);
        var leaver = Row(data, w.Leaver);

        Assert.Contains(w.EnglishId, data.Subjects.Select(s => s.Id));
        Assert.Equal(3, leaver.Grades[w.EnglishId]);
        Assert.Equal(4, leaver.Average);        // ilgari: 5 (guruh bahosi tushib qolardi)
    }

    /// <summary>
    /// Chiqqan kundan KEYINGI guruh darsi maxrajga kirmaydi: guruhda ikkita
    /// dars o'tilgan, biri chiqishdan oldin, biri keyin — bolaning maxrajida
    /// faqat SINF darsi + chiqishdan OLDINGI guruh darsi (2 ta), ya'ni
    /// davomat 100% bo'lib qoladi va "yo'q bo'lgan darsni qoldirdi" degan
    /// soxta davomatsizlik chiqmaydi.
    /// </summary>
    [Fact]
    public async Task Chiqqandan_keyingi_guruh_darsi_maxrajga_kirmaydi()
    {
        await using var db = await NewDbAsync("left_after");
        var w = await SeedAsync(db);
        await ClassAttainmentTests.SetGroupLessonsAsync(db, true);

        // Guruhdan chiqqandan KEYIN qo'yilgan davomatsizlik belgisi — bolaning
        // hisobotiga kirmasligi kerak (u o'sha kuni guruhda emas edi).
        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = w.GroupId, OwnerKind = LessonOwnerKind.Group, SubjectId = w.EnglishId,
            Quarter = 1, StudentId = w.Leaver, Date = w.LateDate, Period = 3, ReasonId = w.ReasonId,
        });
        await db.SaveChangesAsync();

        var leaver = Row(await PerformanceAsync(db, w.ClassAId), w.Leaver);
        Assert.Equal(100, leaver.Attendance);

        var report = await StudentReportBuilder.BuildAsync(
            db, await db.Students.SingleAsync(s => s.Id == w.Leaver));
        Assert.Equal(0, report.Attendance.MissedLessons.GetValueOrDefault(1));
    }

    /// <summary>
    /// Fanlar pivoti (maktab bo'yicha o'zlashtirish) ham xuddi shu qoidani
    /// beradi: guruhdan chiqqan bolaning bahosi 5-A qatoridagi ingliz tili
    /// katagida turadi, guruh esa ALOHIDA QATOR bo'lib chiqmaydi.
    /// </summary>
    [Fact]
    public async Task Pivotda_chiqqan_azoning_bahosi_oz_sinfi_katagida_turadi()
    {
        await using var db = await NewDbAsync("left_pivot");
        var w = await SeedAsync(db);
        await ClassAttainmentTests.SetGroupLessonsAsync(db, true);

        var report = await SubjectAttainmentReport.BuildAsync(db, [w.ClassAId, w.ClassBId], [1]);

        Assert.Equal(["class", "class"], report.Rows.Select(r => r.Kind));
        var english = report.Rows.Single(r => r.ClassId == w.ClassAId)
            .Cells.Single(c => c.SubjectId == w.EnglishId);
        Assert.Equal(1, english.Values);        // faqat bitta bola — chiqqan a'zo
        Assert.Equal(3, english.Average);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static ClassStudentRowDto Row(ClassPerformanceDataDto data, string studentId) =>
        data.Rows.Single(r => r.Student.Id == studentId);

    private static async Task<ClassPerformanceDataDto> PerformanceAsync(AppDbContext db, string classId)
    {
        var res = await new ClassAnalyticsController(db).Performance(classId);
        return Assert.IsType<ClassPerformanceDataDto>(res.Value);
    }

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("history_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>Seed natijasi — testlar shu id'lar bilan ishlaydi.</summary>
    private sealed record World(
        string ClassAId, string ClassBId, string GroupId,
        string MathId, string EnglishId, string ReasonId,
        string Mover, string Stayer, string Leaver, string BStayer,
        string EarlyDate, string MidDate, string LateDate);

    /// <summary>
    /// UCH o'quvchi, ikkita sinf va bitta guruh; hamma sana ANIQ yozilgan,
    /// chunki qoida aynan sanalar ustida ishlaydi.
    ///
    /// <code>
    ///   sanalar:   Erta = 2026-09-07 · O'rta = 2026-09-14 · Kech = 2026-11-02
    ///   o'tish/chiqish kuni: 2026-10-01
    ///
    ///   Mover  (bugun 5-A):  5-B [09-01 … 10-01] → 5-A [10-01 … ochiq]
    ///        · 5-B, Erta, 2-dars  — SABABLI QOLDIRDI
    ///        · 5-B, O'rta, 2-dars — baho 5
    ///        · 5-A, Kech, 1-dars  — baho 3
    ///   Stayer (5-A, doim):  5-A [09-01 … ochiq]
    ///        · 5-A, Kech, 1-dars  — baho 5
    ///   Leaver (5-A):        5-A [09-01 … ochiq], guruh [09-01 … 10-01]
    ///        · 5-A, Kech, 1-dars  — baho 5
    ///        · guruh, Erta, 3-dars — baho 3
    ///   BStayer (5-B, doim): 5-B [09-01 … ochiq]
    ///        · 5-B, Kech, 2-dars  — baho 4
    ///
    ///   o'tilgan darslar (lesson_notes, hammasi `conducted`):
    ///        5-A:   Kech 1-dars (matematika)
    ///        5-B:   Erta 2-dars, O'rta 2-dars, Kech 2-dars (matematika)
    ///        guruh: Erta 3-dars, Kech 3-dars (ingliz tili)
    /// </code>
    ///
    /// <para>
    /// 5-B ning KECHKI darsi va guruhning KECHKI darsi ataylab bor: ikkalasi
    /// ham a'zolik yopilganidan KEYIN, ya'ni ular hech kimning maxrajiga
    /// kirmasligi kerak.
    /// </para>
    /// <para>O'chirgich boshida O'CHIQ — kerak bo'lsa test uni o'zi yoqadi.</para>
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        const string early = "2026-09-07";
        const string mid = "2026-09-14";
        const string late = "2026-11-02";
        var joined = new DateOnly(2026, 9, 1);
        var switchDay = new DateOnly(2026, 10, 1);

        db.SchoolMeta.Add(new SchoolMeta { CurrentYear = "2026/2027", Name = "Test maktab" });
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" });

        var reason = new AbsenceReason { Name = "Sababli", Short = "S" };
        db.AbsenceReasons.Add(reason);

        var classA = new SchoolClass { Name = "5-A", Grade = 5 };
        var classB = new SchoolClass { Name = "5-B", Grade = 5 };
        db.Classes.AddRange(classA, classB);

        var math = new Subject { Name = "Matematika" };
        var english = new Subject { Name = "Ingliz tili", IsGroupable = true };
        db.Subjects.AddRange(math, english);

        var teacher = new Teacher { FullName = "Ustoz", Category = "oliy" };
        var groupTeacher = new Teacher { FullName = "Guruh ustozi", Category = "oliy" };
        db.Teachers.AddRange(teacher, groupTeacher);

        var mover = NewStudent("Mansur Mansurov", "5-A");
        var stayer = NewStudent("Sardor Sardorov", "5-A");
        var leaver = NewStudent("Laziz Lazizov", "5-A");
        var bStayer = NewStudent("Bekzod Bekzodov", "5-B");
        db.Students.AddRange(mover, stayer, leaver, bStayer);

        db.ScheduleTemplates.AddRange(
            Template(classA.Id, "5-A asosiy", math.Id, teacher.Id, period: 1),
            Template(classB.Id, "5-B asosiy", math.Id, teacher.Id, period: 2));

        var createdBy = await SystemUserIdAsync(db);
        var group = new StudyGroup
        {
            Name = "Kuchli ingliz",
            SubjectId = english.Id,
            CreatedBy = createdBy,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.Add(group);
        db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = group.Id, ClassId = classA.Id });
        db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = groupTeacher.Id });

        var groupId = group.Id.ToString();
        var groupTemplate = Template(groupId, "Guruh jadvali", english.Id, groupTeacher.Id, period: 3);
        groupTemplate.OwnerKind = LessonOwnerKind.Group;
        db.ScheduleTemplates.Add(groupTemplate);

        await db.SaveChangesAsync();

        // ---- A'zoliklar (TARIX) ----
        db.ClassMemberships.AddRange(
            Membership(mover.Id, classB.Id, joined, switchDay, createdBy),
            Membership(mover.Id, classA.Id, switchDay, null, createdBy),
            Membership(stayer.Id, classA.Id, joined, null, createdBy),
            Membership(leaver.Id, classA.Id, joined, null, createdBy),
            Membership(bStayer.Id, classB.Id, joined, null, createdBy));
        db.StudyGroupMembers.Add(new StudyGroupMember
        {
            GroupId = group.Id, SubjectId = english.Id, StudentId = leaver.Id,
            JoinedOn = joined, LeftOn = switchDay, LeaveReason = "Guruhdan chiqdi",
            CreatedBy = createdBy, CreatedAt = AppClock.NowInstant,
        });

        // ---- O'tilgan darslar ----
        db.LessonNotes.AddRange(
            Note(classA.Id, math.Id, late, 1),
            Note(classB.Id, math.Id, early, 2),
            Note(classB.Id, math.Id, mid, 2),
            Note(classB.Id, math.Id, late, 2),
            Note(groupId, english.Id, early, 3, LessonOwnerKind.Group),
            Note(groupId, english.Id, late, 3, LessonOwnerKind.Group));

        // ---- Jurnal ----
        db.JournalEntries.AddRange(
            new JournalEntry
            {
                ClassId = classB.Id, SubjectId = math.Id, Quarter = 1,
                StudentId = mover.Id, Date = early, Period = 2, ReasonId = reason.Id,
            },
            new JournalEntry
            {
                ClassId = classB.Id, SubjectId = math.Id, Quarter = 1,
                StudentId = mover.Id, Date = mid, Period = 2, Grade = 5,
            },
            new JournalEntry
            {
                ClassId = classA.Id, SubjectId = math.Id, Quarter = 1,
                StudentId = mover.Id, Date = late, Period = 1, Grade = 3,
            },
            new JournalEntry
            {
                ClassId = classA.Id, SubjectId = math.Id, Quarter = 1,
                StudentId = stayer.Id, Date = late, Period = 1, Grade = 5,
            },
            new JournalEntry
            {
                ClassId = classA.Id, SubjectId = math.Id, Quarter = 1,
                StudentId = leaver.Id, Date = late, Period = 1, Grade = 5,
            },
            new JournalEntry
            {
                ClassId = groupId, OwnerKind = LessonOwnerKind.Group, SubjectId = english.Id,
                Quarter = 1, StudentId = leaver.Id, Date = early, Period = 3, Grade = 3,
            },
            new JournalEntry
            {
                ClassId = classB.Id, SubjectId = math.Id, Quarter = 1,
                StudentId = bStayer.Id, Date = late, Period = 2, Grade = 4,
            });

        await db.SaveChangesAsync();

        return new World(
            classA.Id, classB.Id, groupId,
            math.Id, english.Id, reason.Id,
            mover.Id, stayer.Id, leaver.Id, bStayer.Id,
            early, mid, late);
    }

    private static ClassMembership Membership(
        string studentId, string classId, DateOnly joinedOn, DateOnly? leftOn, string userId) => new()
    {
        StudentId = studentId,
        ClassId = classId,
        JoinedOn = joinedOn,
        LeftOn = leftOn,
        LeaveReason = leftOn is null ? null : "Boshqa sinfga o'tkazildi",
        CreatedBy = userId,
        CreatedAt = AppClock.NowInstant,
    };

    private static ScheduleTemplate Template(
        string ownerId, string name, string subjectId, string teacherId, int period) => new()
    {
        ClassId = ownerId,
        Name = name,
        Lessons = [new ScheduleLesson { Day = 0, Period = period, SubjectId = subjectId, TeacherId = teacherId }],
    };

    private static LessonNote Note(
        string ownerId, string subjectId, string date, int period,
        string ownerKind = LessonOwnerKind.Class) => new()
    {
        ClassId = ownerId, OwnerKind = ownerKind, SubjectId = subjectId, Quarter = 1,
        Date = date, Period = period, SubGroup = 0, Topic = "Mavzu", Conducted = true,
    };

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2014-01-01",
        Gender = "male",
        ClassName = className,
        EnrollmentDate = "2026-09-01",
    };

    /// <summary><c>created_by</c> ustuni <c>users(id)</c> ga RESTRICT bilan bog'langan.</summary>
    private static async Task<string> SystemUserIdAsync(AppDbContext db)
    {
        var existing = await db.Users.FirstOrDefaultAsync();
        if (existing is not null) return existing.Id;
        var user = new AppUser
        {
            FullName = "Seed",
            Role = Roles.Admin,
            Email = $"membership-history.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
