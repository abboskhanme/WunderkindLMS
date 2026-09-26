using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "Davomat belgilash" ekranida qo'yilgan belgi davomatni o'qiydigan boshqa ekranlarda ham
/// TO'G'RI ko'rinishi (2026-09-26, uchidan-uchiga tekshiruvda topilgan xatolar):
/// <list type="bullet">
///   <item>Kunlik davomat hisoboti — fan kunda ikki marta bo'lsa, bir darsdagi yo'qlik ikkinchi
///     darsga ham tushardi; fan bo'yicha ro'yxatda kech kelgan "Kelmadi" chiqardi.</item>
///   <item>Bosh sahifa "Bugungi davomat" — belgilangan darsdagi kelganlar "tekshirilmagan" edi.</item>
///   <item>Bosh sahifa "Dars qoldirayotganlar" — sababli (kasal) yo'qlik ham sanalardi va
///     "Oxirgi kelgan" belgilangan darslarni ko'rmasdi.</item>
/// </list>
/// Har test O'Z bazasida — raqamlar butun maktab bo'yicha.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AttendanceConsumersTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    /// <summary>2026-01-12 — dushanba (jadvalda 0-kun).</summary>
    private const string Monday = "2026-01-12";

    [Fact]
    public async Task Kunlik_hisobot_bir_fan_ikki_darsda_yoqlikni_aralashtirmaydi()
    {
        await using var db = await NewDbAsync("daily_twice");
        var s = await SeedAsync(db);

        // Kimyo — 1- va 4-soat. A 1-soatda sababsiz, B 1-soatda kech keldi; 4-soat toza.
        var tpl = new ScheduleTemplate
        {
            ClassId = s.ClassId, Name = "Asosiy",
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 1, SubjectId = s.Chemistry },
                new ScheduleLesson { Day = 0, Period = 4, SubjectId = s.Chemistry },
            ],
        };
        db.ScheduleTemplates.Add(tpl);
        await db.SaveChangesAsync();
        var week = ScheduleMath.GetQuarterWeeks("2026-01-10", "2026-03-20")
            .First(w => string.CompareOrdinal(Monday, w.StartISO) >= 0 && string.CompareOrdinal(Monday, w.EndISO) <= 0);
        db.WeekAssignments.Add(new WeekAssignment { ClassId = s.ClassId, Quarter = 3, Week = week.Week, TemplateId = tpl.Id });
        db.JournalEntries.AddRange(
            Entry(s.ClassId, s.Chemistry, s.A, Monday, 1, s.Unexcused),
            Entry(s.ClassId, s.Chemistry, s.B, Monday, 1, s.Late));
        await db.SaveChangesAsync();

        var controller = new AttendanceController(db);
        var daily = (await controller.GetDaily(s.ClassId, Monday)).Value!;
        var first = daily.Subjects.Single(x => x.Period == 1);
        var fourth = daily.Subjects.Single(x => x.Period == 4);
        Assert.Equal(1, first.Absent);                 // kech keldi — yo'qlik emas
        Assert.Equal(2, first.Reasons.Sum(r => r.Count));
        Assert.Equal(0, fourth.Absent);                // ilgari 1 bo'lardi
        Assert.Empty(fourth.Reasons);
        Assert.Equal(fourth.Total, fourth.Present);

        // Fan bo'yicha ro'yxat: 1-soat — A kelmadi, B KELDI (kech qoldi); 4-soat — hamma keldi.
        var detail1 = (await controller.GetSubjectDetail(s.ClassId, s.Chemistry, Monday, 1)).Value!.ToList();
        var a1 = detail1.Single(x => x.Student.Id == s.A);
        var b1 = detail1.Single(x => x.Student.Id == s.B);
        Assert.True(a1.Absent);
        Assert.Equal("Sababsiz", a1.ReasonName);
        Assert.False(b1.Absent);
        Assert.Equal("Kech keldi", b1.ReasonName);
        var detail4 = (await controller.GetSubjectDetail(s.ClassId, s.Chemistry, Monday, 4)).Value!;
        Assert.All(detail4, x => { Assert.False(x.Absent); Assert.Null(x.ReasonName); });
    }

    [Fact]
    public async Task Bosh_sahifa_bugungi_soat_belgilangan_darsda_kelganlar_tekshirilgan()
    {
        await using var db = await NewDbAsync("dash_today");
        var s = await SeedAsync(db);
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        // 5-A (3 o'quvchi) 2-soat belgilangan: A sababsiz, B kech keldi, C — yozuvsiz (keldi).
        db.DailyAttendanceMarks.Add(Mark(s.ClassId, s.Chemistry, today, 2, s.ActorId));
        db.JournalEntries.AddRange(
            Entry(s.ClassId, s.Chemistry, s.A, today, 2, s.Unexcused),
            Entry(s.ClassId, s.Chemistry, s.B, today, 2, s.Late));
        // Yo'nalish guruhi (2 a'zo) 3-soat belgilangan, hech kim yo'q — hammasi keldi.
        var track = await AddTrackAsync(db, s, [s.D, s.E]);
        db.DailyAttendanceMarks.Add(Mark(track, s.Chemistry, today, 3, s.ActorId));
        await db.SaveChangesAsync();

        var dashboard = (await new DashboardController(db).Get()).Value!;

        var p2 = dashboard.AttendanceByPeriod.Single(r => r.Period == 2);
        Assert.Equal(3, p2.Expected);
        Assert.Equal(2, p2.Present);     // B (kech) + C (yozuvsiz)
        Assert.Equal(1, p2.Absent);
        Assert.Equal(0, p2.Unchecked);   // ilgari 1 (C) edi

        var p3 = dashboard.AttendanceByPeriod.Single(r => r.Period == 3);
        Assert.Equal(2, p3.Expected);
        Assert.Equal(2, p3.Present);
        Assert.Equal(0, p3.Unchecked);
    }

    [Fact]
    public async Task Bosh_sahifa_dars_qoldiruvchilar_faqat_sababsiz_va_oxirgi_kelgan_belgidan()
    {
        await using var db = await NewDbAsync("dash_absent");
        var s = await SeedAsync(db);
        var day1 = AppClock.Today.AddDays(-3).ToString("yyyy-MM-dd");
        var day2 = AppClock.Today.AddDays(-2).ToString("yyyy-MM-dd");

        // 1-kun: A sababsiz, B kasal. 2-kun: belgilangan dars, hech kim yo'q — ikkalasi keldi.
        db.DailyAttendanceMarks.AddRange(
            Mark(s.ClassId, s.Chemistry, day1, 1, s.ActorId),
            Mark(s.ClassId, s.Chemistry, day2, 1, s.ActorId));
        db.JournalEntries.AddRange(
            Entry(s.ClassId, s.Chemistry, s.A, day1, 1, s.Unexcused),
            Entry(s.ClassId, s.Chemistry, s.B, day1, 1, s.Illness));
        await db.SaveChangesAsync();

        var dashboard = (await new DashboardController(db).Get()).Value!;

        var row = Assert.Single(dashboard.AbsentStudents);   // kasal B — ro'yxatda YO'Q
        Assert.Equal(s.A, row.StudentId);
        Assert.Equal(1, row.MissedDays);
        Assert.Equal(day2, row.LastSeen);                      // ilgari null edi
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record Seed(
        string ClassId, string Chemistry, string A, string B, string C, string D, string E,
        string Illness, string Unexcused, string Late, string ActorId);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("att_consumers_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>5-A (A, B, C) + 9-A (D, E — yo'nalish uchun), Kimyo, uch sabab, 3-chorak.</summary>
    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        var cls = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var c9 = new SchoolClass { Name = "9-A", Grade = 9, Language = "uz" };
        var chemistry = new Subject { Name = "Kimyo" };
        db.Classes.AddRange(cls, c9);
        db.Subjects.Add(chemistry);

        var a = NewStudent("Aliyev Ali", "5-A");
        var b = NewStudent("Berdiyev Bek", "5-A");
        var c = NewStudent("Comilov Can", "5-A");
        var d = NewStudent("Dilova Dilnoza", "9-A");
        var e = NewStudent("Ergashev Erkin", "9-A");
        db.Students.AddRange(a, b, c, d, e);

        var illness = new AbsenceReason { Name = "Kasal", Short = "K" };
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "SS" };
        var late = new AbsenceReason { Name = "Kech keldi", Short = "Kch", IsLate = true };
        db.AbsenceReasons.AddRange(illness, unexcused, late);

        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });

        var actor = new AppUser
        {
            FullName = "Mas'ul xodim", Role = Roles.Staff, Email = "davomat@example.test", PasswordHash = "x",
        };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        return new Seed(cls.Id, chemistry.Id, a.Id, b.Id, c.Id, d.Id, e.Id,
            illness.Id, unexcused.Id, late.Id, actor.Id);
    }

    private static async Task<string> AddTrackAsync(AppDbContext db, Seed s, string[] members)
    {
        var track = new StudyGroup
        {
            Name = "Aniq fanlar", SubjectId = null, IsTrack = true, CreatedBy = s.ActorId, CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.Add(track);
        foreach (var id in members)
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = track.Id, StudentId = id, JoinedOn = new DateOnly(2025, 9, 1),
                CreatedBy = s.ActorId, CreatedAt = AppClock.NowInstant,
            });
        await db.SaveChangesAsync();
        return track.Id.ToString();
    }

    private static DailyAttendanceMark Mark(string ownerId, string subjectId, string date, int period, string by) => new()
    {
        ClassId = ownerId, SubjectId = subjectId, Date = date, Period = period, MarkedBy = by, MarkedAt = AppClock.NowInstant,
    };

    private static JournalEntry Entry(string classId, string subjectId, string studentId, string date, int period, string reasonId) => new()
    {
        ClassId = classId, SubjectId = subjectId, StudentId = studentId, Date = date, Period = period,
        Quarter = 3, ReasonId = reasonId,
    };

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = className,
        EnrollmentDate = "2025-09-01",
    };
}
