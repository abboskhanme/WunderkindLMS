using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// A class where nobody took attendance shows as "tekshirilmagan" (client, 2026-10-07: "bitta sinf
/// davomat qilinmagan lekin bu haqda malumot hisobi chiqmagan"). The reports used to count only
/// lessons that were conducted or marked, so such a class dropped out of the totals altogether:
/// prod 07.10 showed keldi 433 + kelmadi 24, unchecked 0, with 7-A's 26 pupils missing.
/// Timetable lessons up to today, minus Sundays and holidays, now join the reports as unchecked.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class UncheckedClassAttendanceTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    /// <summary>2026-01-12 — Monday (timetable day 0); 2026-01-13 — Tuesday, a holiday below.</summary>
    private const string Monday = "2026-01-12";
    private const string Tuesday = "2026-01-13";

    [Fact]
    public async Task Analitika_davomati_olinmagan_sinfni_tekshirilmagan_deb_sanaydi()
    {
        await using var db = await NewDbAsync("analytics");
        var s = await SeedAsync(db);

        var r = await AttendanceAnalytics.BuildAsync(db, null, Monday, Tuesday, Monday);

        // 5-A: A absent all day, B and C came. 6-A (F, G): nobody checked — unchecked, not missing.
        Assert.Equal(5, r.Total.Opportunities);
        Assert.Equal(2, r.Total.Present);
        Assert.Equal(1, r.Total.Absent);
        Assert.Equal(2, r.Total.Unchecked);
        Assert.Equal(2, r.Total.Lessons);                 // only 5-A's lessons were held

        var sixA = r.Classes.Single(c => c.ClassId == s.SixA);
        Assert.Equal(2, sixA.Tally.Unchecked);
        Assert.Equal(0, sixA.Tally.Lessons);

        // The holiday adds nothing; the trend has the Monday only.
        var point = Assert.Single(r.Trend);
        Assert.Equal(Monday, point.Date);
        Assert.Equal(2, point.Tally.Unchecked);

        // Period cut: each period — 3 checked pupils of 5-A, 2 unchecked of 6-A.
        Assert.All(r.Periods, p => Assert.Equal(2, p.Tally.Unchecked));
    }

    [Fact]
    public async Task Intizom_hisoboti_tekshirilmagan_kunni_foizga_keldi_deb_qoshmaydi()
    {
        await using var db = await NewDbAsync("discipline");
        var s = await SeedAsync(db);

        var r = await AttendanceDisciplineReport.BuildAsync(db, Monday, Tuesday);

        var sixA = r.Classes.Single(c => c.ClassId == s.SixA);
        Assert.Equal(2, sixA.Unchecked);
        Assert.Null(sixA.AttendancePercent);              // nobody checked — not 100%

        var fiveA = r.Classes.Single(c => c.ClassId == s.FiveA);
        Assert.Equal(0, fiveA.Unchecked);
        Assert.Equal(66.7, fiveA.AttendancePercent);      // 2 of 3 came

        Assert.Equal(2, r.Totals.Unchecked);
        Assert.Equal(66.7, r.Totals.AttendancePercent);   // over the checked days only
    }

    [Fact]
    public async Task Jadval_darslari_bugundan_keyin_bayramda_va_yakshanbada_yoq()
    {
        await using var db = await NewDbAsync("scheduled");
        var s = await SeedAsync(db);
        var today = AppClock.Today;
        var start = today.AddDays(-14);
        var end = today.AddDays(14);
        await AddLiveQuarterAsync(db, s.SixA, start, end);
        var holiday = today.AddDays(-2);
        db.Holidays.Add(new Holiday { Date = holiday.ToString("yyyy-MM-dd"), Name = "Bayram" });
        await db.SaveChangesAsync();

        var lessons = await HeldLessons.ScheduledAsync(
            db, [s.SixA], start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));

        Assert.NotEmpty(lessons);
        Assert.All(lessons, l =>
        {
            Assert.True(l.Scheduled);
            Assert.True(string.CompareOrdinal(l.Date, today.ToString("yyyy-MM-dd")) <= 0);
            Assert.NotEqual(holiday.ToString("yyyy-MM-dd"), l.Date);
            Assert.NotEqual(DayOfWeek.Sunday, DateOnly.Parse(l.Date).DayOfWeek);
        });
    }

    [Fact]
    public async Task Bosh_sahifa_boshlangan_belgilanmagan_darsni_tekshirilmagan_korsatadi()
    {
        if (AppClock.Today.DayOfWeek == DayOfWeek.Sunday) return; // no lessons on a Sunday

        await using var db = await NewDbAsync("dashboard");
        var s = await SeedAsync(db);
        await AddLiveQuarterAsync(db, s.SixA, AppClock.Today.AddDays(-7), AppClock.Today.AddDays(7));
        // Period 1 has started; period 2 starts at 23:59 — not yet.
        db.LessonTimes.AddRange(
            new LessonTime { Period = 1, StartTime = "00:00", EndTime = "00:45" },
            new LessonTime { Period = 2, StartTime = "23:59", EndTime = "23:59" });
        await db.SaveChangesAsync();

        var dashboard = (await new DashboardController(db).Get()).Value!;

        var p1 = dashboard.AttendanceByPeriod.Single(r => r.Period == 1);
        Assert.Equal(2, p1.Expected);    // 6-A, nobody marked it
        Assert.Equal(2, p1.Unchecked);
        var p2 = dashboard.AttendanceByPeriod.Single(r => r.Period == 2);
        Assert.Equal(0, p2.Expected);    // not started yet
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record Seed(string FiveA, string SixA, string Chemistry, string A, string Unexcused, string ActorId);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("unchecked_class_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// 5-A (A, B, C) and 6-A (F, G); both have Kimyo in periods 1–2 on Monday and Tuesday.
    /// Monday: 5-A marked, A absent both lessons; 6-A untouched. Tuesday is a holiday.
    /// </summary>
    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        var fiveA = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var sixA = new SchoolClass { Name = "6-A", Grade = 6, Language = "uz" };
        var chemistry = new Subject { Name = "Kimyo" };
        db.Classes.AddRange(fiveA, sixA);
        db.Subjects.Add(chemistry);

        var a = NewStudent("Aliyev Ali", "5-A");
        db.Students.AddRange(
            a, NewStudent("Berdiyev Bek", "5-A"), NewStudent("Comilov Can", "5-A"),
            NewStudent("Fayzullayev Fozil", "6-A"), NewStudent("G'aniyev G'ani", "6-A"));

        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "SS" };
        db.AbsenceReasons.Add(unexcused);
        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });
        db.Holidays.Add(new Holiday { Date = Tuesday, Name = "Bayram" });

        var actor = new AppUser
        {
            FullName = "Mas'ul xodim", Role = Roles.Staff, Email = "davomat@example.test", PasswordHash = "x",
        };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var week = ScheduleMath.GetQuarterWeeks("2026-01-10", "2026-03-20")
            .First(w => string.CompareOrdinal(Monday, w.StartISO) >= 0 && string.CompareOrdinal(Monday, w.EndISO) <= 0);
        foreach (var cls in new[] { fiveA, sixA })
        {
            var tpl = Template(cls.Id, chemistry.Id, days: [0, 1]);
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
            db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 3, Week = week.Week, TemplateId = tpl.Id });
        }

        foreach (var period in new[] { 1, 2 })
        {
            db.DailyAttendanceMarks.Add(new DailyAttendanceMark
            {
                ClassId = fiveA.Id, SubjectId = chemistry.Id, Date = Monday, Period = period,
                MarkedBy = actor.Id, MarkedAt = AppClock.NowInstant,
            });
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = fiveA.Id, SubjectId = chemistry.Id, StudentId = a.Id, Date = Monday, Period = period,
                Quarter = 3, ReasonId = unexcused.Id,
            });
        }
        await db.SaveChangesAsync();

        return new Seed(fiveA.Id, sixA.Id, chemistry.Id, a.Id, unexcused.Id, actor.Id);
    }

    /// <summary>A quarter around today with a Mon–Sat timetable (periods 1–2) on every week for the class.</summary>
    private static async Task AddLiveQuarterAsync(AppDbContext db, string classId, DateOnly start, DateOnly end)
    {
        var from = start.ToString("yyyy-MM-dd");
        var to = end.ToString("yyyy-MM-dd");
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = from, EndDate = to });
        var subject = new Subject { Name = "Fizika" };
        db.Subjects.Add(subject);
        var tpl = Template(classId, subject.Id, days: [0, 1, 2, 3, 4, 5]);
        db.ScheduleTemplates.Add(tpl);
        await db.SaveChangesAsync();
        foreach (var w in ScheduleMath.GetQuarterWeeks(from, to))
            db.WeekAssignments.Add(new WeekAssignment { ClassId = classId, Quarter = 1, Week = w.Week, TemplateId = tpl.Id });
        await db.SaveChangesAsync();
    }

    private static ScheduleTemplate Template(string classId, string subjectId, int[] days) => new()
    {
        ClassId = classId, Name = "Asosiy",
        Lessons = [.. days.SelectMany(d => new[]
        {
            new ScheduleLesson { Day = d, Period = 1, SubjectId = subjectId },
            new ScheduleLesson { Day = d, Period = 2, SubjectId = subjectId },
        })],
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
