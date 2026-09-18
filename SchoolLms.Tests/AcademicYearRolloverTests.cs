using Microsoft.AspNetCore.Http;
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
/// O'QUV YILINI YAKUNLASH (rollover) — <c>POST /api/admin/academic-year/rollover</c>,
/// docs/modules/students-parity.md §2.1.4 ("Year rollover", Q8) va G-11.
///
/// <para>
/// Bu endpoint butun maktabni bir amalda o'zgartiradi: arxiv snapshot'ini oladi,
/// o'quvchilarni keyingi sinfga ko'taradi, bitiruvchilarni chiqaradi, tanlangan
/// toifalarni tozalaydi VA <b>har faol o'quv guruhini arxivlab, a'zoliklarini
/// sana bilan yopadi</b>. Shunga qaramay unga birorta test yo'q edi.
/// </para>
///
/// <para>
/// <b>Nega o'z bazasi va nega HTTP emas.</b> Amal butun maktabni supurib tashlaydi —
/// umumiy <see cref="ApiFixture.Database"/> da yurgizib bo'lmaydi, chunki u bilan
/// birga qo'shni test klasslarining ma'lumoti ham yo'q bo'lardi. Ikkinchi
/// <see cref="ApiFactory"/> ham ko'tarib bo'lmaydi: ulanish satri muhit
/// o'zgaruvchisi orqali uzatiladi va u JARAYON bo'yicha umumiy, shuning uchun
/// harness bir vaqtda faqat BITTA tirik fabrikaga ruxsat beradi. Yechim —
/// shablondan olingan alohida baza + controllerni to'g'ridan-to'g'ri chaqirish
/// (<see cref="ClassAttainmentTests"/> bilan bir xil naqsh). Endpointning RBAC
/// darvozasi (<c>[AdminPerm("academicYear")]</c>) o'z o'rnida —
/// <c>Security/RbacMatrixTests</c> uni atribut darajasida ushlaydi.
/// </para>
/// <para>
/// Hovuzlar <see cref="DisposeAsync"/> da tozalanadi — aks holda konteynerdagi
/// <c>max_connections</c> tugaydi va keyingi klasslar <c>53300</c> bilan yiqiladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AcademicYearRolloverTests(ApiFixture fixture) : IAsyncLifetime
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
    //  1. Guruhlar arxivlanadi, a'zoliklar yopiladi
    // =====================================================================

    /// <summary>
    /// Har FAOL guruh arxivlanadi va har FAOL a'zolik o'tish sanasi bilan
    /// yopiladi (o'chirilmaydi — tarix qoladi). Allaqachon arxivlangan guruh va
    /// allaqachon yopilgan a'zolikka TEGILMAYDI: ularning sanasi va sababi
    /// o'zgarmaydi, aks holda "qachon chiqdi" degan savol har yil qayta
    /// yozilardi.
    /// </summary>
    [Fact]
    public async Task Rollover_faol_guruhlarni_arxivlaydi_va_azoliklarni_yopadi()
    {
        await using var db = await NewDbAsync("groups");
        var w = await SeedAsync(db);

        await RolloverAsync(db, Request(w.NewYear));

        var groups = await db.StudyGroups.AsNoTracking().ToListAsync();
        Assert.All(groups, g => Assert.True(g.IsArchived, $"guruh arxivlanmadi: {g.Name}"));
        Assert.All(groups, g => Assert.NotNull(g.ArchivedAt));

        var today = AppClock.Today;
        var active = await db.StudyGroupMembers.AsNoTracking()
            .SingleAsync(m => m.GroupId == w.EnglishGroupId && m.StudentId == w.Fifth);
        Assert.Equal(today, active.LeftOn);
        Assert.Equal("O'quv yili yakunlandi", active.LeaveReason);

        // Oldin yopilgan a'zolik — o'sha-o'sha.
        var closed = await db.StudyGroupMembers.AsNoTracking()
            .SingleAsync(m => m.GroupId == w.ArchivedGroupId);
        Assert.Equal(new DateOnly(2026, 9, 15), closed.LeftOn);
        Assert.Equal("Guruh arxivlandi", closed.LeaveReason);

        // Faol a'zolik umuman qolmadi.
        Assert.Empty(await db.StudyGroupMembers.AsNoTracking().Where(m => m.LeftOn == null).ToListAsync());
    }

    /// <summary>
    /// Audit yozuvi raqamlarni ochiq aytadi: ikkita FAOL guruh arxivlandi va
    /// ikkita FAOL a'zolik yopildi (uchinchi guruh va uchinchi a'zolik allaqachon
    /// yopiq edi, shuning uchun sanalmaydi).
    /// </summary>
    [Fact]
    public async Task Rollover_audit_yozuvida_guruh_raqamlari_bor()
    {
        await using var db = await NewDbAsync("audit");
        var w = await SeedAsync(db);

        await RolloverAsync(db, Request(w.NewYear));

        var entry = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityType == "AcademicYear" && a.Action == "rollover");
        Assert.Contains("arxivlangan guruh: 2", entry.Summary);
        Assert.Contains("yopilgan guruh a'zoligi: 2", entry.Summary);
        Assert.Contains($"2026/2027 → {w.NewYear}", entry.Summary);
    }

    /// <summary>
    /// KELAJAKDA boshlanadigan a'zolik (bugun hali ochilmagan guruh) o'tish
    /// sanasi bilan emas, O'Z boshlanish sanasi bilan yopiladi — aks holda
    /// <c>left_on &lt; joined_on</c> bo'lib, baza check constraint'i
    /// (<c>ck_study_group_members_dates</c>) butun amalni qaytarib yuborardi.
    /// </summary>
    [Fact]
    public async Task Kelajakda_boshlanadigan_azolik_oz_sanasi_bilan_yopiladi()
    {
        await using var db = await NewDbAsync("future");
        var w = await SeedAsync(db);

        var future = AppClock.Today.AddDays(30);
        db.StudyGroupMembers.Add(new StudyGroupMember
        {
            GroupId = w.PhysicsGroupId, SubjectId = w.PhysicsId, StudentId = w.Fifth,
            JoinedOn = future, CreatedBy = w.UserId, CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();

        await RolloverAsync(db, Request(w.NewYear));

        var row = await db.StudyGroupMembers.AsNoTracking()
            .SingleAsync(m => m.GroupId == w.PhysicsGroupId && m.StudentId == w.Fifth);
        Assert.Equal(future, row.LeftOn);
    }

    // =====================================================================
    //  2. Bitiruvchi sinf guruhni boqayotgan bo'lsa
    // =====================================================================

    /// <summary>
    /// <b>Regressiya.</b> 11-sinf o'chiriladi, lekin <c>study_group_classes.class_id</c>
    /// FK'si RESTRICT: bitiruvchi sinf biror guruhni boqayotgan bo'lsa (11-sinflardan
    /// yig'ilgan fizika guruhi — odatiy holat) butun rollover 23503 bilan
    /// qaytarilardi va maktab o'quv yilini umuman yakunlay olmasdi.
    ///
    /// <para>
    /// Endi "boqadi" bog'lanishi sinf bilan birga uziladi, guruhning O'ZI esa
    /// arxivlanadi va a'zolik tarixi joyida qoladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bitiruvchi_sinf_guruhni_boqayotgan_bolsa_ham_rollover_otadi()
    {
        await using var db = await NewDbAsync("graduate");
        var w = await SeedAsync(db);

        var result = await RolloverAsync(db, Request(w.NewYear));
        Assert.Equal(1, result.Graduated);

        Assert.Empty(await db.StudyGroupClasses.AsNoTracking()
            .Where(g => g.ClassId == w.EleventhId).ToListAsync());
        // Guruhning o'zi qoldi (arxivda) va uni 5-sinf hali ham boqadi.
        var physics = await db.StudyGroups.AsNoTracking().SingleAsync(g => g.Id == w.PhysicsGroupId);
        Assert.True(physics.IsArchived);
        Assert.NotEmpty(await db.StudyGroupClasses.AsNoTracking()
            .Where(g => g.GroupId == w.PhysicsGroupId).ToListAsync());
    }

    // =====================================================================
    //  3. Ko'tarish, tozalash va arxiv
    // =====================================================================

    /// <summary>
    /// Ko'tarish: 5-A → 6-A. Sinf nomi, o'quvchining <c>class_name</c> ustuni va
    /// sinf rahbarining biriktirmasi BIRGA o'zgaradi; sinf a'zoligi yozuvi esa
    /// sinf ID'siga bog'langani uchun ochiq qoladi va yetim bo'lmaydi.
    /// </summary>
    [Fact]
    public async Task Rollover_sinfni_kotaradi_va_sinf_azoligi_yetim_qolmaydi()
    {
        await using var db = await NewDbAsync("promote");
        var w = await SeedAsync(db);

        var result = await RolloverAsync(db, Request(w.NewYear));

        Assert.Equal("2026/2027", result.OldYear);
        Assert.Equal(w.NewYear, result.NewYear);
        Assert.Equal(1, result.Promoted);

        var cls = await db.Classes.AsNoTracking().SingleAsync();
        Assert.Equal("6-A", cls.Name);
        Assert.Equal(6, cls.Grade);
        Assert.Equal("6-A", (await db.Students.AsNoTracking().SingleAsync(s => s.Id == w.Fifth)).ClassName);
        Assert.Equal("6-A", (await db.Teachers.AsNoTracking().SingleAsync(t => t.Id == w.TeacherId)).HomeroomClass);

        var membership = await db.ClassMemberships.AsNoTracking()
            .SingleAsync(m => m.StudentId == w.Fifth);
        Assert.Equal(cls.Id, membership.ClassId);
        Assert.Null(membership.LeftOn);

        Assert.Equal(w.NewYear, (await db.SchoolMeta.AsNoTracking().SingleAsync()).CurrentYear);
    }

    /// <summary>
    /// Arxiv snapshot'i guruhlarni ham saqlaydi — usiz ZIP eksportidagi jurnal
    /// qatorlarining egasi NOMSIZ chiqardi (<c>class_id</c> ustunida guruh id'si
    /// turadi va u sinflar ro'yxatida yo'q).
    /// </summary>
    [Fact]
    public async Task Arxiv_snapshotida_guruhlar_ham_saqlanadi()
    {
        await using var db = await NewDbAsync("archive");
        var w = await SeedAsync(db);

        await RolloverAsync(db, Request(w.NewYear));

        var archive = await db.SchoolYearArchives.AsNoTracking().SingleAsync();
        Assert.Equal("2026/2027", archive.Year);
        Assert.Contains("Kuchli ingliz", archive.Data);
        Assert.Contains("\"studyGroups\"", archive.Data);
    }

    /// <summary>
    /// Guruhlarni arxivlash BIRORTA bayroqqa bog'liq emas: <c>ClearGrades</c> va
    /// <c>PromoteStudents</c> o'chirilgan bo'lsa ham guruhlar arxivlanadi va
    /// a'zoliklar yopiladi (yangi yil guruhlari qaytadan tuziladi — Q8), jurnal
    /// esa joyida qoladi.
    /// </summary>
    [Fact]
    public async Task Guruhlarni_arxivlash_bayroqlarga_boglik_emas()
    {
        await using var db = await NewDbAsync("flags");
        var w = await SeedAsync(db);

        await RolloverAsync(db, new RolloverRequest(
            w.NewYear, PromoteStudents: false, ClearGrades: false,
            ClearSchedule: false, ClearQuarters: false, ClearFinance: false));

        Assert.All(await db.StudyGroups.AsNoTracking().ToListAsync(), g => Assert.True(g.IsArchived));
        Assert.Empty(await db.StudyGroupMembers.AsNoTracking().Where(m => m.LeftOn == null).ToListAsync());
        Assert.Equal("5-A", (await db.Classes.AsNoTracking().SingleAsync(c => c.Id == w.FifthId)).Name);
        Assert.NotEmpty(await db.JournalEntries.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// Yakunlash ARXIVLAGAN guruhning baholari hisobotdan YO'QOLMAYDI: a'zolik
    /// yopilgan sanaga qadar yozilgan qatorlar bolaning hisobotida qoladi
    /// (<see cref="ClassAttainment"/> 5-bandi). Aks holda o'quv yilini yakunlash
    /// tugmasi butun yilning guruh baholarini o'chirib yuborardi.
    /// </summary>
    [Fact]
    public async Task Arxivlangan_guruhning_baholari_hisobotda_qoladi()
    {
        await using var db = await NewDbAsync("after");
        var w = await SeedAsync(db);
        await ClassAttainmentTests.SetGroupLessonsAsync(db, true);

        await RolloverAsync(db, new RolloverRequest(
            w.NewYear, PromoteStudents: false, ClearGrades: false,
            ClearSchedule: false, ClearQuarters: false, ClearFinance: false));

        var pupil = await db.Students.SingleAsync(s => s.Id == w.Fifth);
        var report = await StudentReportBuilder.BuildAsync(db, pupil);
        Assert.Equal(4, report.Grades[w.EnglishId][1]);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static RolloverRequest Request(string newYear) => new(
        newYear, PromoteStudents: true, ClearGrades: true,
        ClearSchedule: true, ClearQuarters: true, ClearFinance: false);

    private static async Task<RolloverResultDto> RolloverAsync(AppDbContext db, RolloverRequest req)
    {
        var controller = new AcademicYearController(db, new AuditService(db, new HttpContextAccessor()));
        var res = await controller.Rollover(req);
        Assert.NotNull(res.Value);
        return res.Value!;
    }

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("rollover_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private sealed record World(
        string NewYear, string UserId, string TeacherId,
        string FifthId, string EleventhId,
        string EnglishId, string PhysicsId,
        Guid EnglishGroupId, Guid PhysicsGroupId, Guid ArchivedGroupId,
        string Fifth, string Eleventh);

    /// <summary>
    /// Ikkita sinf (5-A va 11-A), har birida bittadan o'quvchi, va UCHTA guruh:
    /// <list type="bullet">
    ///   <item>"Kuchli ingliz" — 5-A boqadi, a'zosi 5-sinf o'quvchisi (FAOL);</item>
    ///   <item>"11-sinf fizika" — 11-A VA 5-A boqadi, a'zosi bitiruvchi (FAOL) —
    ///     bitiruvchi sinf o'chirilganda FK tuzog'ini aynan shu ochadi;</item>
    ///   <item>"Eski guruh" — ALLAQACHON arxivlangan, a'zoligi 15-sentyabrda
    ///     yopilgan (rollover unga tegmasligi kerak).</item>
    /// </list>
    /// 5-sinf o'quvchisiga sinfda "5", guruhda "3" qo'yilgan — arxivdan keyin
    /// hisobot ikkalasini ham ko'rishi kerak.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        db.SchoolMeta.Add(new SchoolMeta { CurrentYear = "2026/2027", Name = "Test maktab" });
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" });

        var fifth = new SchoolClass { Name = "5-A", Grade = 5 };
        var eleventh = new SchoolClass { Name = "11-A", Grade = 11 };
        db.Classes.AddRange(fifth, eleventh);

        var math = new Subject { Name = "Matematika" };
        var english = new Subject { Name = "Ingliz tili", IsGroupable = true };
        var physics = new Subject { Name = "Fizika", IsGroupable = true };
        db.Subjects.AddRange(math, english, physics);

        var teacher = new Teacher { FullName = "Sinf rahbari", Category = "oliy", HomeroomClass = "5-A" };
        db.Teachers.Add(teacher);

        var pupil = NewStudent("Anvar Anvarov", "5-A");
        var graduate = NewStudent("Gulnora Gulnorova", "11-A");
        db.Students.AddRange(pupil, graduate);

        await db.SaveChangesAsync();
        var userId = await SystemUserIdAsync(db);

        var englishGroup = NewGroup("Kuchli ingliz", english.Id, userId);
        var physicsGroup = NewGroup("11-sinf fizika", physics.Id, userId);
        var oldGroup = NewGroup("Eski guruh", math.Id, userId);
        oldGroup.IsArchived = true;
        oldGroup.ArchivedAt = AppClock.NowInstant;
        db.StudyGroups.AddRange(englishGroup, physicsGroup, oldGroup);

        db.StudyGroupClasses.AddRange(
            new StudyGroupClass { GroupId = englishGroup.Id, ClassId = fifth.Id },
            new StudyGroupClass { GroupId = physicsGroup.Id, ClassId = eleventh.Id },
            new StudyGroupClass { GroupId = physicsGroup.Id, ClassId = fifth.Id },
            new StudyGroupClass { GroupId = oldGroup.Id, ClassId = fifth.Id });
        db.StudyGroupTeachers.AddRange(
            new StudyGroupTeacher { GroupId = englishGroup.Id, TeacherId = teacher.Id },
            new StudyGroupTeacher { GroupId = physicsGroup.Id, TeacherId = teacher.Id });

        var joined = new DateOnly(2026, 9, 1);
        db.StudyGroupMembers.AddRange(
            new StudyGroupMember
            {
                GroupId = englishGroup.Id, SubjectId = english.Id, StudentId = pupil.Id,
                JoinedOn = joined, CreatedBy = userId, CreatedAt = AppClock.NowInstant,
            },
            new StudyGroupMember
            {
                GroupId = physicsGroup.Id, SubjectId = physics.Id, StudentId = graduate.Id,
                JoinedOn = joined, CreatedBy = userId, CreatedAt = AppClock.NowInstant,
            },
            new StudyGroupMember
            {
                GroupId = oldGroup.Id, SubjectId = math.Id, StudentId = pupil.Id,
                JoinedOn = joined, LeftOn = new DateOnly(2026, 9, 15),
                LeaveReason = "Guruh arxivlandi",
                CreatedBy = userId, CreatedAt = AppClock.NowInstant,
            });

        db.ClassMemberships.AddRange(
            new ClassMembership
            {
                StudentId = pupil.Id, ClassId = fifth.Id, JoinedOn = joined,
                CreatedBy = userId, CreatedAt = AppClock.NowInstant,
            },
            new ClassMembership
            {
                StudentId = graduate.Id, ClassId = eleventh.Id, JoinedOn = joined,
                CreatedBy = userId, CreatedAt = AppClock.NowInstant,
            });

        const string date = "2026-09-07";
        db.LessonNotes.AddRange(
            new LessonNote
            {
                ClassId = fifth.Id, SubjectId = math.Id, Quarter = 1, Date = date,
                Period = 1, Topic = "Mavzu", Conducted = true,
            },
            new LessonNote
            {
                ClassId = englishGroup.Id.ToString(), OwnerKind = LessonOwnerKind.Group,
                SubjectId = english.Id, Quarter = 1, Date = date, Period = 2,
                Topic = "Guruh mavzusi", Conducted = true,
            });
        db.JournalEntries.AddRange(
            new JournalEntry
            {
                ClassId = fifth.Id, SubjectId = math.Id, Quarter = 1,
                StudentId = pupil.Id, Date = date, Period = 1, Grade = 5,
            },
            new JournalEntry
            {
                ClassId = englishGroup.Id.ToString(), OwnerKind = LessonOwnerKind.Group,
                SubjectId = english.Id, Quarter = 1, StudentId = pupil.Id,
                Date = date, Period = 2, Grade = 5,
            },
            new JournalEntry
            {
                ClassId = englishGroup.Id.ToString(), OwnerKind = LessonOwnerKind.Group,
                SubjectId = english.Id, Quarter = 1, StudentId = pupil.Id,
                Date = "2026-09-14", Period = 2, Grade = 3,
            });

        await db.SaveChangesAsync();

        return new World(
            "2027/2028", userId, teacher.Id, fifth.Id, eleventh.Id,
            english.Id, physics.Id,
            englishGroup.Id, physicsGroup.Id, oldGroup.Id,
            pupil.Id, graduate.Id);
    }

    private static StudyGroup NewGroup(string name, string subjectId, string userId) => new()
    {
        Name = name,
        SubjectId = subjectId,
        CreatedBy = userId,
        CreatedAt = AppClock.NowInstant,
    };

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2010-01-01",
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
            Email = $"rollover.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
