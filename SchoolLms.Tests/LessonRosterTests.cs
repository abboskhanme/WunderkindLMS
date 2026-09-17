using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// G-5 — uchta umumiy resolver (docs/modules/students-parity.md §2.1.6).
///
/// <para>
/// <b>Nima isbotlanadi va nega aynan shu.</b> Cut-over'ning butun xavfsizligi
/// bitta da'voga suyanadi: <i>guruh darsi mavjud bo'lmaguncha yangi
/// resolverlar bugungi so'rovlar bilan BIR XIL qatorlarni qaytaradi.</i>
/// Agar shu da'vo yolg'on bo'lsa, guruhlarga umuman tegmagan maktabda ham
/// jurnal, davomat, hisobot, maosh va turniket raqamlari jimgina siljib
/// ketardi.
/// </para>
/// <para>
/// Shuning uchun quyidagi testlar ESKI so'rovni (nusxa ko'chirilgan
/// <c>class_name == cls.Name</c> mantig'i) test ichida QAYTA yozadi va
/// resolver natijasi bilan yonma-yon solishtiradi — resolverning o'z
/// mantig'iga emas, BUGUNGI xatti-harakatga qarshi.
/// </para>
/// <para>
/// <b>Nega o'z bazasi.</b> <c>school_meta.group_lessons_enabled</c> — butun
/// maktab uchun BITTA qator. Umumiy bazada uni yoqib-o'chirish qo'shni test
/// klassining natijasini o'zgartirardi (masalan
/// <c>StudyGroupTests.Guruh_yaratish_darslarga_tegmaydi</c> uni o'chiq deb
/// talab qiladi). Har test o'z bazasini oladi — hovuzlar
/// <see cref="DisposeAsync"/> da tozalanadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class LessonRosterTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Har test O'Z bazasini, ya'ni o'z ulanish hovuzini oladi. Tozalanmasa
    /// konteynerdagi <c>max_connections</c> tugaydi va keyingi klasslar
    /// <c>53300</c> bilan yiqiladi.
    /// </summary>
    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  1. LessonRoster — dars ro'yxati
     * ================================================================== */

    /// <summary>
    /// Sinf ro'yxati BUGUNGI so'rov bilan bir xil: aynan
    /// <c>students.class_name == cls.Name</c>, arxivlanganlarsiz, F.I.SH
    /// bo'yicha tartiblangan. Bu — cut-over'ning asosiy kafolati.
    /// </summary>
    [Fact]
    public async Task Sinf_royxati_bugungi_sorov_bilan_bir_xil()
    {
        await using var db = await NewDbAsync("roster");
        var w = await SeedAsync(db);

        foreach (var cls in await db.Classes.ToListAsync())
        {
            // ESKI mantiq, ataylab qo'lda qayta yozilgan.
            var legacy = await db.Students
                .Where(s => s.ClassName == cls.Name && !s.IsArchived)
                .OrderBy(s => s.FullName)
                .Select(s => s.Id)
                .ToListAsync();

            var owner = await LessonRoster.OwnerAsync(db, cls.Id);
            Assert.NotNull(owner);
            Assert.Equal(LessonOwnerKind.Class, owner!.Kind);

            var viaResolver = (await LessonRoster.ForLessonAsync(db, owner))
                .Select(s => s.Id).ToList();

            Assert.Equal(legacy, viaResolver);
        }

        // Arxivlangan bola ro'yxatda yo'q (bugungi kod ham shunday).
        Assert.DoesNotContain(w.ArchivedInA, (await LessonRoster.ForLessonAsync(db, w.ClassAOwner))
            .Select(s => s.Id));
    }

    /// <summary>
    /// Bo'linish (SubGroup) filtri — bugungi qoidaning aynan o'zi:
    /// <c>SubGroup == 0</c> bo'lsa butun sinf, aks holda faqat o'sha guruh.
    /// </summary>
    [Fact]
    public async Task Bolinish_filtri_bugungi_qoidani_takrorlaydi()
    {
        await using var db = await NewDbAsync("subgroup");
        var w = await SeedAsync(db);

        var whole = (await LessonRoster.ForLessonAsync(db, w.ClassAOwner, subGroup: 0))
            .Select(s => s.Id).ToList();
        Assert.Contains(w.A1, whole);
        Assert.Contains(w.A2, whole);

        var first = (await LessonRoster.ForLessonAsync(db, w.ClassAOwner, subGroup: 1))
            .Select(s => s.Id).ToList();
        Assert.Equal(new[] { w.A1 }, first);   // A1 — 1-guruh, A2 — 2-guruh

        var second = (await LessonRoster.ForLessonAsync(db, w.ClassAOwner, subGroup: 2))
            .Select(s => s.Id).ToList();
        Assert.Equal(new[] { w.A2 }, second);
    }

    /// <summary>
    /// Guruh ro'yxati — FAOL a'zolar. Guruhda sinf ichidagi bo'linish yo'q,
    /// shuning uchun <c>subGroup</c> berilsa ham ro'yxat o'zgarmaydi.
    /// </summary>
    [Fact]
    public async Task Guruh_royxati_faol_azolardan_iborat()
    {
        await using var db = await NewDbAsync("grouproster");
        var w = await SeedAsync(db);
        await SetGroupLessonsAsync(db, true);

        var members = (await LessonRoster.ForLessonAsync(db, w.GroupOwner))
            .Select(s => s.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { w.A1, w.B1 }.OrderBy(x => x), members);

        // Bo'linish guruhga ta'sir qilmaydi.
        var withSubGroup = (await LessonRoster.ForLessonAsync(db, w.GroupOwner, subGroup: 1))
            .Select(s => s.Id).OrderBy(x => x).ToList();
        Assert.Equal(members, withSubGroup);

        // Guruhdan chiqqan bola FAOL ro'yxatda yo'q.
        Assert.DoesNotContain(w.A2, members);
    }

    /* =====================================================================
     *  2. O'chirgich — o'chiq bo'lsa guruh UMUMAN ko'rinmaydi
     * ================================================================== */

    /// <summary>
    /// O'chirgich o'chiq: tirik egalar faqat sinflar, o'quvchining egalari
    /// faqat sinfi. Guruh ham, uning a'zoligi ham bazada TURIBDI — lekin
    /// hech bir hisob-kitob ularni ko'rmaydi.
    /// </summary>
    [Fact]
    public async Task Ochirgich_ochiq_bolsa_guruh_egalari_korinmaydi()
    {
        await using var db = await NewDbAsync("switchoff");
        var w = await SeedAsync(db);

        Assert.False(await LessonRoster.GroupLessonsEnabledAsync(db));

        var owners = await LessonRoster.LiveOwnersAsync(db);
        Assert.Contains(w.ClassAId, owners.Keys);
        Assert.DoesNotContain(w.GroupId, owners.Keys);

        var a1 = await db.Students.SingleAsync(s => s.Id == w.A1);
        var a1Owners = await LessonRoster.OwnersOfAsync(db, a1);
        Assert.Single(a1Owners);
        Assert.Equal(LessonOwnerKind.Class, a1Owners[0].Kind);

        Assert.Empty(await LessonRoster.GroupOwnersByStudentAsync(db));
    }

    /// <summary>O'chirgich yoqilganda o'quvchi sinfi BILAN BIRGA guruhini ham oladi.</summary>
    [Fact]
    public async Task Ochirgich_yoqilsa_oquvchi_sinf_va_guruhni_oladi()
    {
        await using var db = await NewDbAsync("switchon");
        var w = await SeedAsync(db);
        await SetGroupLessonsAsync(db, true);

        var a1 = await db.Students.SingleAsync(s => s.Id == w.A1);
        var owners = await LessonRoster.OwnersOfAsync(db, a1);
        Assert.Equal(2, owners.Count);
        Assert.Contains(owners, o => o.IsClass && o.Id == w.ClassAId);
        Assert.Contains(owners, o => o.IsGroup && o.Id == w.GroupId);

        // Guruhga kirmagan bola — faqat sinfi.
        var a2 = await db.Students.SingleAsync(s => s.Id == w.A2);
        Assert.Single(await LessonRoster.OwnersOfAsync(db, a2));
    }

    /* =====================================================================
     *  3. PupilTimetable — o'quvchining haftasi
     * ================================================================== */

    /// <summary>
    /// O'chirgich o'chiq: o'quvchining haftasi BUGUNGI ikki qadamning aynan
    /// natijasi — sinfning haftaga biriktirilgan shabloni + bo'linish filtri.
    /// </summary>
    [Fact]
    public async Task Oquvchi_haftasi_ochirgich_ochiq_bolsa_bugungidek()
    {
        await using var db = await NewDbAsync("pupilweek_off");
        var w = await SeedAsync(db);

        var a1 = await db.Students.SingleAsync(s => s.Id == w.A1);

        // ESKI yo'l: PortalSchedule ikki qadami.
        var legacy = PortalSchedule.ForStudent(
                await PortalSchedule.LessonsForWeekAsync(db, w.ClassAId, 1, 1), a1.SubGroup)
            .OrderBy(l => l.Day).ThenBy(l => l.Period)
            .Select(l => (l.Day, l.Period, l.SubjectId)).ToList();

        var viaResolver = (await PupilTimetable.ForWeekAsync(db, a1, 1, 1))
            .Select(l => (l.Day, l.Period, l.SubjectId)).ToList();

        Assert.Equal(legacy, viaResolver);
        // Guruh darsi (4-dars) ro'yxatda YO'Q.
        Assert.DoesNotContain(viaResolver, l => l.Period == GroupPeriod);
    }

    /// <summary>O'chirgich yoqilganda guruh darsi o'quvchining haftasiga QO'SHILADI.</summary>
    [Fact]
    public async Task Oquvchi_haftasiga_guruh_darsi_qoshiladi()
    {
        await using var db = await NewDbAsync("pupilweek_on");
        var w = await SeedAsync(db);
        await SetGroupLessonsAsync(db, true);

        var a1 = await db.Students.SingleAsync(s => s.Id == w.A1);
        var lessons = await PupilTimetable.ForWeekAsync(db, a1, 1, 1);

        var group = Assert.Single(lessons, l => l.Owner.IsGroup);
        Assert.Equal(GroupDay, group.Day);
        Assert.Equal(GroupPeriod, group.Period);
        Assert.Equal(w.GroupSubjectId, group.SubjectId);

        // Sinf darslari joyida — guruh ularni SIQIB CHIQARMAYDI.
        Assert.Equal(3, lessons.Count(l => l.Owner.IsClass));

        // Guruhda bo'lmagan bola avvalgidek faqat sinf darslarini ko'radi.
        var a2 = await db.Students.SingleAsync(s => s.Id == w.A2);
        Assert.DoesNotContain(await PupilTimetable.ForWeekAsync(db, a2, 1, 1), l => l.Owner.IsGroup);
    }

    /* =====================================================================
     *  4. TeacherLessons — o'qituvchi darslari
     * ================================================================== */

    /// <summary>
    /// O'chirgich o'chiq: hafta kuni kesimidagi darslar soni ESKI filtr
    /// ("mavjud, arxivlanmagan sinflarning eng ko'p darsli shabloni") bilan
    /// bir xil. Guruhda dars beradigan o'qituvchi hali NOLGA ega.
    /// </summary>
    [Fact]
    public async Task Oqituvchi_darslari_ochirgich_ochiq_bolsa_bugungidek()
    {
        await using var db = await NewDbAsync("teacher_off");
        var w = await SeedAsync(db);

        var legacy = await LegacyLessonsByWeekdayAsync(db);
        var viaResolver = await TeacherLessons.ByWeekdayAsync(db);

        Assert.Equal(legacy.Keys.OrderBy(k => k), viaResolver.Keys.OrderBy(k => k));
        foreach (var (teacherId, arr) in legacy)
            Assert.Equal(arr, viaResolver[teacherId]);

        // Guruh o'qituvchisi — umuman ro'yxatda yo'q (aynan G-16 dagi jim tuzoq).
        Assert.False(viaResolver.ContainsKey(w.GroupTeacherId));
    }

    /// <summary>
    /// O'chirgich yoqilganda guruh darsi BIR MARTA sanaladi — guruhni IKKI
    /// sinf boqayotgan bo'lsa ham. Bu G-16 ning yuragi: nusxa yondashuvida
    /// bu raqam 2 bo'lib, maosh ikki barobar oshib ketardi.
    /// </summary>
    [Fact]
    public async Task Guruh_darsi_ikki_sinf_boqsa_ham_bir_marta_sanaladi()
    {
        await using var db = await NewDbAsync("teacher_on");
        var w = await SeedAsync(db);
        await SetGroupLessonsAsync(db, true);

        var byWeekday = await TeacherLessons.ByWeekdayAsync(db);

        Assert.True(byWeekday.ContainsKey(w.GroupTeacherId));
        Assert.Equal(1, byWeekday[w.GroupTeacherId].Sum());
        Assert.Equal(1, byWeekday[w.GroupTeacherId][GroupDay]);

        // Guruhni ikkita sinf boqadi — buni tasdiqlaymiz, aks holda test
        // "bir marta" ni bekorga da'vo qilardi.
        Assert.Equal(2, await db.StudyGroupClasses.CountAsync(x => x.GroupId == Guid.Parse(w.GroupId)));
    }

    /// <summary>
    /// Haftaga biriktirilgan darslar (portal manbai): o'chirgich o'chiq bo'lsa
    /// guruh biriktirishi KO'RINMAYDI, yoqilganda esa ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Haftalik_darslarda_guruh_ochirgichga_boysunadi()
    {
        await using var db = await NewDbAsync("teacherweek");
        var w = await SeedAsync(db);

        Assert.Empty(await TeacherLessons.ForWeekAsync(db, w.GroupTeacherId, 1, 1));

        await SetGroupLessonsAsync(db, true);
        var lessons = await TeacherLessons.ForWeekAsync(db, w.GroupTeacherId, 1, 1);
        var only = Assert.Single(lessons);
        Assert.True(only.Owner.IsGroup);
        Assert.Equal(GroupPeriod, only.Period);
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    /// <summary>Guruh darsi qaysi kun/soatda — sinf darslariga TEGMAYDIGAN soat.</summary>
    private const int GroupDay = 0;      // Dushanba
    private const int GroupPeriod = 4;   // sinfda 1-3 darslar bor, 4-dars bo'sh

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("lessons_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// <c>TeacherSalaryCalc.LessonsByWeekdayAsync</c> ning CUT-OVER'DAN OLDINGI
    /// tanasi, so'zma-so'z. Resolver shu bilan solishtiriladi — o'zi bilan emas.
    /// </summary>
    private static async Task<Dictionary<string, int[]>> LegacyLessonsByWeekdayAsync(AppDbContext db)
    {
        var classSet = (await db.Classes.Where(c => !c.IsArchived).Select(c => c.Id).ToListAsync())
            .ToHashSet();
        var templates = (await db.ScheduleTemplates.Include(t => t.Lessons).ToListAsync())
            .Where(t => classSet.Contains(t.ClassId)).ToList();
        var mainPerClass = templates
            .GroupBy(t => t.ClassId)
            .Select(g => g.OrderByDescending(t => t.Lessons.Count).ThenBy(t => t.Id).First());

        var result = new Dictionary<string, int[]>();
        foreach (var tpl in mainPerClass)
            foreach (var l in tpl.Lessons.Where(l => !string.IsNullOrEmpty(l.TeacherId) && l.Day is >= 0 and < 6))
            {
                if (!result.TryGetValue(l.TeacherId, out var arr)) result[l.TeacherId] = arr = new int[6];
                arr[l.Day]++;
            }
        return result;
    }

    internal static async Task SetGroupLessonsAsync(AppDbContext db, bool enabled)
    {
        var meta = await db.SchoolMeta.FirstAsync();
        meta.GroupLessonsEnabled = enabled;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ikki sinf (5-A, 5-B), har birida jadval; bitta guruh IKKALA sinfdan
    /// yig'ilgan (A1 va B1), guruhning o'z jadvali dushanba 4-darsda.
    /// Guruh a'zoligi va jadvali BOR, lekin o'chirgich O'CHIQ.
    /// </summary>
    internal static async Task<World> SeedAsync(AppDbContext db)
    {
        db.SchoolMeta.Add(new SchoolMeta { CurrentYear = "2026/2027", Name = "Test maktab" });
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" });

        var classA = new SchoolClass { Name = "5-A", Grade = 5 };
        var classB = new SchoolClass { Name = "5-B", Grade = 5 };
        db.Classes.AddRange(classA, classB);

        var math = new Subject { Name = "Matematika" };
        var english = new Subject { Name = "Ingliz tili", IsGroupable = true };
        db.Subjects.AddRange(math, english);

        var classTeacher = new Teacher { FullName = "Sinf ustozi", Category = "oliy" };
        var groupTeacher = new Teacher { FullName = "Guruh ustozi", Category = "oliy" };
        db.Teachers.AddRange(classTeacher, groupTeacher);

        var a1 = NewStudent("Anvar Anvarov", "5-A", subGroup: 1);
        var a2 = NewStudent("Aziza Azizova", "5-A", subGroup: 2);
        var b1 = NewStudent("Bobur Boburov", "5-B", subGroup: 0);
        var archived = NewStudent("Arxiv Arxivov", "5-A", subGroup: 0);
        archived.IsArchived = true;
        archived.ArchivedAt = "2026-09-05";
        db.Students.AddRange(a1, a2, b1, archived);

        // Sinf jadvallari: 5-A da 3 ta dars (dushanba 1-3), 5-B da 2 ta.
        var tplA = new ScheduleTemplate
        {
            ClassId = classA.Id,
            Name = "5-A asosiy",
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 1, SubjectId = math.Id, TeacherId = classTeacher.Id },
                new ScheduleLesson { Day = 0, Period = 2, SubjectId = math.Id, TeacherId = classTeacher.Id },
                new ScheduleLesson { Day = 0, Period = 3, SubjectId = math.Id, TeacherId = classTeacher.Id },
            ],
        };
        var tplB = new ScheduleTemplate
        {
            ClassId = classB.Id,
            Name = "5-B asosiy",
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 1, SubjectId = math.Id, TeacherId = classTeacher.Id },
                new ScheduleLesson { Day = 0, Period = 2, SubjectId = math.Id, TeacherId = classTeacher.Id },
            ],
        };
        db.ScheduleTemplates.AddRange(tplA, tplB);

        var group = new StudyGroup
        {
            Name = "Kuchli ingliz",
            SubjectId = english.Id,
            CreatedBy = await SystemUserIdAsync(db),
            CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.Add(group);
        db.StudyGroupClasses.AddRange(
            new StudyGroupClass { GroupId = group.Id, ClassId = classA.Id },
            new StudyGroupClass { GroupId = group.Id, ClassId = classB.Id });
        db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = groupTeacher.Id });

        var joined = new DateOnly(2026, 9, 1);
        db.StudyGroupMembers.AddRange(
            NewMember(group, english.Id, a1.Id, joined, await SystemUserIdAsync(db)),
            NewMember(group, english.Id, b1.Id, joined, await SystemUserIdAsync(db)),
            // A2 guruhda EDI, chiqib ketgan — FAOL ro'yxatda bo'lmasligi kerak.
            NewMember(group, english.Id, a2.Id, joined, await SystemUserIdAsync(db),
                leftOn: new DateOnly(2026, 9, 10)));

        var groupTpl = new ScheduleTemplate
        {
            ClassId = group.Id.ToString(),
            Name = "Guruh jadvali",
            OwnerKind = LessonOwnerKind.Group,
            Lessons =
            [
                new ScheduleLesson
                {
                    Day = GroupDay, Period = GroupPeriod,
                    SubjectId = english.Id, TeacherId = groupTeacher.Id,
                },
            ],
        };
        db.ScheduleTemplates.Add(groupTpl);

        // Haftaga biriktirish: 1-chorak 1-hafta — ikkala sinf va guruh.
        db.WeekAssignments.AddRange(
            new WeekAssignment { ClassId = classA.Id, Quarter = 1, Week = 1, TemplateId = tplA.Id },
            new WeekAssignment { ClassId = classB.Id, Quarter = 1, Week = 1, TemplateId = tplB.Id },
            new WeekAssignment
            {
                ClassId = group.Id.ToString(), Quarter = 1, Week = 1,
                TemplateId = groupTpl.Id, OwnerKind = LessonOwnerKind.Group,
            });

        await db.SaveChangesAsync();

        return new World(
            ClassAId: classA.Id, ClassBId: classB.Id,
            ClassAOwner: new LessonOwner(LessonOwnerKind.Class, classA.Id, classA.Name),
            GroupId: group.Id.ToString(),
            GroupOwner: new LessonOwner(LessonOwnerKind.Group, group.Id.ToString(), group.Name, english.Id),
            GroupSubjectId: english.Id,
            GroupTeacherId: groupTeacher.Id,
            ClassTeacherId: classTeacher.Id,
            A1: a1.Id, A2: a2.Id, B1: b1.Id, ArchivedInA: archived.Id,
            ClassATemplateId: tplA.Id, GroupTemplateId: groupTpl.Id);
    }

    /// <summary>
    /// <c>created_by</c> ustuni <c>users(id)</c> ga RESTRICT bilan bog'langan —
    /// test uchun bitta haqiqiy foydalanuvchi kerak.
    /// </summary>
    private static async Task<string> SystemUserIdAsync(AppDbContext db)
    {
        var existing = await db.Users.FirstOrDefaultAsync();
        if (existing is not null) return existing.Id;
        var user = new AppUser
        {
            FullName = "Seed",
            Role = Roles.Admin,
            Email = $"lesson-roster.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static StudyGroupMember NewMember(
        StudyGroup group, string subjectId, string studentId, DateOnly joined, string createdBy,
        DateOnly? leftOn = null) => new()
    {
        GroupId = group.Id,
        SubjectId = subjectId,
        StudentId = studentId,
        JoinedOn = joined,
        LeftOn = leftOn,
        CreatedBy = createdBy,
        CreatedAt = AppClock.NowInstant,
    };

    private static Student NewStudent(string fullName, string className, int subGroup) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2014-01-01",
        Gender = "male",
        ClassName = className,
        SubGroup = subGroup,
        EnrollmentDate = "2026-09-01",
    };

    /// <summary>Seed natijasi — testlar shu id'lar bilan ishlaydi.</summary>
    internal sealed record World(
        string ClassAId,
        string ClassBId,
        LessonOwner ClassAOwner,
        string GroupId,
        LessonOwner GroupOwner,
        string GroupSubjectId,
        string GroupTeacherId,
        string ClassTeacherId,
        string A1,
        string A2,
        string B1,
        string ArchivedInA,
        string ClassATemplateId,
        string GroupTemplateId);
}
