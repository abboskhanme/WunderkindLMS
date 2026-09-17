using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// G-16 (maosh) va G-14 (turniket kutilgan vaqti) —
/// docs/modules/students-parity.md §2.1.6.
///
/// <para>
/// <b>Bu faylda PUL bor.</b> Darslarni sanash qoidasi o'zgarsa, o'qituvchining
/// oyligi o'zgaradi. Shuning uchun har maosh testi ESKI raqamni ham, YANGI
/// raqamni ham ochiq yozadi — "hisoblash to'g'ri" degan mavhum da'vo emas.
/// </para>
/// <para>
/// <b>Nega o'z bazasi.</b> Maosh butun maktabni yig'adi va o'chirgich
/// (<c>school_meta.group_lessons_enabled</c>) bitta qatorda turadi: umumiy
/// bazada bu testlar qo'shnilarining raqamini o'zgartirardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class GroupSalaryTurnstileTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Bir soat dars narxi (so'm) — "oliy" toifa.</summary>
    private const decimal HourRate = 50_000m;

    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Har test o'z bazasini oladi — hovuzlar shu yerda bo'shatiladi.</summary>
    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  1. G-16 — maosh
     * ================================================================== */

    /// <summary>
    /// <b>ESKI RAQAM: 0 so'm. YANGI RAQAM: 200 000 so'm.</b>
    ///
    /// <para>
    /// Faqat guruhda dars beradigan o'qituvchi bugun maoshsiz qoladi:
    /// <c>TeacherSalaryCalc</c> egasi sinf bo'lmagan shablonni jimgina tashlab
    /// yuboradi (§2.1.5 — "the silent pay trap"). O'chirgich yoqilgach uning
    /// haftalik 1 ta darsi sanaladi: 1 × 4 hafta × 50 000 = 200 000 so'm.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Guruh_oqituvchisi_ochirgichgacha_0_som_keyin_200000_som()
    {
        await using var db = await NewDbAsync("salary");
        var w = await LessonRosterTests.SeedAsync(db);
        await SetRateAsync(db);

        var teacher = await db.Teachers.SingleAsync(t => t.Id == w.GroupTeacherId);
        var meta = await db.SchoolMeta.SingleAsync();

        var weeklyBefore = (await TeacherSalaryCalc.WeeklyLessonsAsync(db)).GetValueOrDefault(teacher.Id);
        Assert.Equal(0, weeklyBefore);
        Assert.Equal(0m, TeacherSalaryCalc.Monthly(weeklyBefore, teacher.Category, meta));

        await LessonRosterTests.SetGroupLessonsAsync(db, true);

        var weeklyAfter = (await TeacherSalaryCalc.WeeklyLessonsAsync(db)).GetValueOrDefault(teacher.Id);
        Assert.Equal(1, weeklyAfter);
        Assert.Equal(200_000m, TeacherSalaryCalc.Monthly(weeklyAfter, teacher.Category, meta));
    }

    /// <summary>
    /// <b>Guruhni IKKI sinf boqadi, lekin dars BIR MARTA to'lanadi.</b>
    ///
    /// <para>
    /// Agar guruh darsi har boquvchi sinf jadvaliga nusxalansa, raqam
    /// 400 000 so'm bo'lib ketardi — ya'ni maktab bir darsni ikki marta
    /// to'lardi. Bu test aynan o'sha ikki barobarlikni qo'riqlaydi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Guruh_darsi_bir_marta_tolanadi_400000_emas_200000()
    {
        await using var db = await NewDbAsync("salary_once");
        var w = await LessonRosterTests.SeedAsync(db);
        await SetRateAsync(db);
        await LessonRosterTests.SetGroupLessonsAsync(db, true);

        Assert.Equal(2, await db.StudyGroupClasses.CountAsync(x => x.GroupId == Guid.Parse(w.GroupId)));

        var teacher = await db.Teachers.SingleAsync(t => t.Id == w.GroupTeacherId);
        var meta = await db.SchoolMeta.SingleAsync();
        var weekly = (await TeacherSalaryCalc.WeeklyLessonsAsync(db)).GetValueOrDefault(teacher.Id);

        Assert.Equal(1, weekly);
        Assert.Equal(200_000m, TeacherSalaryCalc.Monthly(weekly, teacher.Category, meta));
        Assert.NotEqual(400_000m, TeacherSalaryCalc.Monthly(weekly, teacher.Category, meta));
    }

    /// <summary>
    /// <b>SINF o'qituvchisining oyligi O'ZGARMAYDI: 1 000 000 so'm, ikkala holatda ham.</b>
    ///
    /// <para>
    /// Cut-over'ning butun va'dasi shu: guruhga aloqasi yo'q odamning puli
    /// bir tiyin ham siljimaydi. 5-A da 3 ta + 5-B da 2 ta = haftasiga 5 dars,
    /// 5 × 4 × 50 000 = 1 000 000 so'm.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Sinf_oqituvchisining_oyligi_ochirgichdan_qatiy_nazar_bir_xil()
    {
        await using var db = await NewDbAsync("salary_class");
        var w = await LessonRosterTests.SeedAsync(db);
        await SetRateAsync(db);

        var teacher = await db.Teachers.SingleAsync(t => t.Id == w.ClassTeacherId);
        var meta = await db.SchoolMeta.SingleAsync();

        var before = (await TeacherSalaryCalc.WeeklyLessonsAsync(db)).GetValueOrDefault(teacher.Id);
        Assert.Equal(5, before);
        Assert.Equal(1_000_000m, TeacherSalaryCalc.Monthly(before, teacher.Category, meta));

        await LessonRosterTests.SetGroupLessonsAsync(db, true);

        var after = (await TeacherSalaryCalc.WeeklyLessonsAsync(db)).GetValueOrDefault(teacher.Id);
        Assert.Equal(before, after);
        Assert.Equal(1_000_000m, TeacherSalaryCalc.Monthly(after, teacher.Category, meta));
    }

    /* =====================================================================
     *  2. G-14 — turniketda kutilgan kelish vaqti
     * ================================================================== */

    /// <summary>
    /// <b>Kutilgan kelish guruh darsiga ergashadi.</b>
    ///
    /// <para>
    /// Sinfning dushanbadagi birinchi darsi — 2-dars (09:25). Bolaning guruh
    /// darsi esa 1-darsda (08:30). U 09:05 da kelgan:
    /// </para>
    /// <list type="bullet">
    ///   <item>o'chirgich O'CHIQ — kutilgan 09:25, ya'ni u KECHIKMAGAN
    ///     (bugungi xatti-harakat, o'zgarmaydi);</item>
    ///   <item>o'chirgich YOQILGAN — kutilgan 08:30, grace 10 daqiqa, ya'ni
    ///     35 daqiqa KECHIKKAN.</item>
    /// </list>
    /// <para>
    /// Guruhda bo'lmagan sinfdoshi ikkala holatda ham kechikmagan bo'lib
    /// qoladi — o'zgarish faqat guruhdagi bolaga tegadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Turniket_kutilgan_kelish_guruh_darsiga_ergashadi()
    {
        await using var db = await NewDbAsync("turnstile");
        var seed = await SeedTurnstileAsync(db);
        var day = DateOnly.Parse(Monday);

        var before = await new TurnstileAnalyticsQueries(db)
            .AttendanceAsync(day, day, TurnstileClass, null);
        Assert.Equal(0, RowOf(before, seed.InGroup).LateDays);
        Assert.Equal(0, RowOf(before, seed.NotInGroup).LateDays);

        await LessonRosterTests.SetGroupLessonsAsync(db, true);

        var after = await new TurnstileAnalyticsQueries(db)
            .AttendanceAsync(day, day, TurnstileClass, null);
        Assert.Equal(1, RowOf(after, seed.InGroup).LateDays);
        Assert.Equal(35, RowOf(after, seed.InGroup).LateMinutes);
        // Guruhda bo'lmagan bolaga hech narsa bo'lmadi.
        Assert.Equal(0, RowOf(after, seed.NotInGroup).LateDays);
    }

    /// <summary>
    /// Faqat guruhda dars beradigan o'qituvchidan ilgari maktabning umumiy
    /// ish vaqti kutilardi; endi uning guruh darsi ham hisobga olinadi.
    /// </summary>
    [Fact]
    public async Task Oqituvchining_birinchi_darsi_guruhni_ham_koradi()
    {
        await using var db = await NewDbAsync("teacherfirst");
        var w = await LessonRosterTests.SeedAsync(db);

        var before = await TeacherLessons.FirstPeriodByWeekdayAsync(db);
        Assert.False(before.ContainsKey(w.GroupTeacherId));

        await LessonRosterTests.SetGroupLessonsAsync(db, true);

        var after = await TeacherLessons.FirstPeriodByWeekdayAsync(db);
        Assert.True(after.ContainsKey(w.GroupTeacherId));
        Assert.Equal(4, after[w.GroupTeacherId][0]); // dushanba, 4-dars

        // Sinf o'qituvchisining birinchi darsi o'zgarmadi.
        Assert.Equal(before[w.ClassTeacherId], after[w.ClassTeacherId]);
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private const string Monday = "2026-09-07";
    private const string TurnstileClass = "9-TURNIKET";

    private static SchoolLms.Application.Dtos.TurnstileStudentRowDto RowOf(
        SchoolLms.Application.Dtos.TurnstileAttendanceReportDto report, string studentId) =>
        report.Rows.Single(r => r.StudentId == studentId);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("gsal_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task SetRateAsync(AppDbContext db)
    {
        var meta = await db.SchoolMeta.FirstAsync();
        meta.SalaryRateOliy = HourRate;
        await db.SaveChangesAsync();
    }

    private sealed record TurnstileSeed(string InGroup, string NotInGroup);

    /// <summary>
    /// Bitta dushanba: sinfning birinchi darsi 2-darsda (09:25), guruhning
    /// darsi 1-darsda (08:30). Ikkala bola ham 09:05 da kelgan — biri guruhda,
    /// ikkinchisi yo'q.
    /// </summary>
    private static async Task<TurnstileSeed> SeedTurnstileAsync(AppDbContext db)
    {
        db.SchoolMeta.Add(new SchoolMeta
        {
            CurrentYear = "2026/2027",
            Name = "Test maktab",
            TurnstileEnabled = true,
            WorkStartTime = "08:30",
            LateGraceMinutes = 10,
        });
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" });
        db.LessonTimes.AddRange(
            new LessonTime { Period = 1, StartTime = "08:30", EndTime = "09:15" },
            new LessonTime { Period = 2, StartTime = "09:25", EndTime = "10:10" },
            new LessonTime { Period = 3, StartTime = "10:20", EndTime = "11:05" });

        var cls = new SchoolClass { Name = TurnstileClass, Grade = 9 };
        db.Classes.Add(cls);

        var math = new Subject { Name = "Matematika" };
        var english = new Subject { Name = "Ingliz tili", IsGroupable = true };
        db.Subjects.AddRange(math, english);

        var teacher = new Teacher { FullName = "Ustoz" };
        db.Teachers.Add(teacher);

        // Sinf dushanba kuni FAQAT 2- va 3-darsda o'qiydi → kutilgan kelish 09:25.
        db.ScheduleTemplates.Add(new ScheduleTemplate
        {
            ClassId = cls.Id,
            Name = "Asosiy",
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 2, SubjectId = math.Id, TeacherId = teacher.Id },
                new ScheduleLesson { Day = 0, Period = 3, SubjectId = math.Id, TeacherId = teacher.Id },
            ],
        });

        var inGroup = NewStudent("Guruhli Gulnoza", "2001");
        var notInGroup = NewStudent("Guruhsiz Gulzoda", "2002");
        db.Students.AddRange(inGroup, notInGroup);

        var user = new AppUser
        {
            FullName = "Seed",
            Role = Roles.Admin,
            Email = $"turnstile-seed.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var group = new StudyGroup
        {
            Name = "Erta ingliz",
            SubjectId = english.Id,
            CreatedBy = user.Id,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.Add(group);
        db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = group.Id, ClassId = cls.Id });
        db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = teacher.Id });
        db.StudyGroupMembers.Add(new StudyGroupMember
        {
            GroupId = group.Id,
            SubjectId = english.Id,
            StudentId = inGroup.Id,
            JoinedOn = new DateOnly(2026, 9, 1),
            CreatedBy = user.Id,
            CreatedAt = AppClock.NowInstant,
        });

        // Guruh dushanba 1-darsda (08:30) — sinfnikidan ERTA.
        db.ScheduleTemplates.Add(new ScheduleTemplate
        {
            ClassId = group.Id.ToString(),
            Name = "Guruh jadvali",
            OwnerKind = LessonOwnerKind.Group,
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 1, SubjectId = english.Id, TeacherId = teacher.Id },
            ],
        });

        // Ikkalasi ham 09:05 da kirdi, 11:10 da chiqdi.
        db.TurnstileEvents.AddRange(
            Pass("2001", "09:05"), Pass("2001", "11:10"),
            Pass("2002", "09:05"), Pass("2002", "11:10"));

        await db.SaveChangesAsync();
        return new TurnstileSeed(inGroup.Id, notInGroup.Id);
    }

    private static Student NewStudent(string fullName, string deviceUserId) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2012-01-01",
        Gender = "female",
        ClassName = TurnstileClass,
        DeviceUserId = deviceUserId,
        EnrollmentDate = "2026-09-01",
    };

    private static TurnstileEvent Pass(string deviceUserId, string hhmm) => new()
    {
        DeviceUserId = deviceUserId,
        EventAt = $"{Monday}T{hhmm}:00",
        Direction = "in",
        DeviceName = "Test turniket",
        CreatedAt = $"{Monday}T{hhmm}:05",
    };
}
