using Microsoft.AspNetCore.Mvc;
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
/// The home page's period filter (client, 2026-10-08: "Bugun / Bu hafta / Bu oy" and a custom range,
/// for the whole page). Every dated figure follows <c>from..to</c>: average grade, attendance rate,
/// "Davomat analitikasi" (summed per day), absentees and first payments. Without a period the page
/// keeps its original windows.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DashboardPeriodTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // 2026-01-12 and 2026-01-19 are Mondays, 2026-01-13 (a holiday) and 2026-01-20 Tuesdays.
    private const string Mon12 = "2026-01-12";
    private const string Tue13 = "2026-01-13";
    private const string Mon19 = "2026-01-19";
    private const string Tue20 = "2026-01-20";

    [Fact]
    public async Task Bir_kun_tanlansa_hamma_raqam_shu_kundan()
    {
        await using var db = await NewDbAsync("day");
        await SeedAsync(db);

        var dto = await GetAsync(db, Mon12, Mon12);

        // Only B's Monday grade (5); C's 2 on the 20th is outside.
        Assert.Equal(5.0, dto.Stats.AverageGrade);
        // 5-A: A missed the whole Monday, B and C came — 2 of 3. 6-A had nothing held.
        Assert.Equal(67.0, dto.Stats.AttendanceRate);
        Assert.Equal(67.0, dto.ClassPerformance.Single(c => c.ClassName == "5-A").AttendanceRate);
        Assert.Null(dto.ClassPerformance.Single(c => c.ClassName == "6-A").AttendanceRate);

        // Period 1: 5-A marked (3 checked: A absent, B and C came) + 6-A scheduled, nobody marked (2).
        var p1 = dto.AttendanceByPeriod.Single(r => r.Period == 1);
        Assert.Equal((5, 2, 1, 2), (p1.Expected, p1.Present, p1.Absent, p1.Unchecked));
        Assert.Equal(0, dto.AttendanceByPeriod.Single(r => r.Period == 3).Expected);

        var a = Assert.Single(dto.AbsentStudents);
        Assert.Equal("Aliyev Ali", a.FullName);
        Assert.Equal(1, a.MissedDays);

        Assert.Equal(1, dto.Stats.FirstPaymentInPeriodCount); // B paid first at 00:30 that day
    }

    [Fact]
    public async Task Bir_necha_kun_davomat_jadvali_kunlar_yigindisi()
    {
        await using var db = await NewDbAsync("week");
        await SeedAsync(db);

        var dto = await GetAsync(db, Mon12, Mon19);

        // Mon 12 (5 expected, 2 came, 1 absent, 2 unchecked) + Tue 13 holiday + Mon 19 (nobody marked: 5 unchecked).
        var p1 = dto.AttendanceByPeriod.Single(r => r.Period == 1);
        Assert.Equal((10, 2, 1, 7), (p1.Expected, p1.Present, p1.Absent, p1.Unchecked));
        Assert.Equal(1, dto.Stats.FirstPaymentInPeriodCount);
    }

    [Fact]
    public async Task Qoldirgan_kunlar_davr_boshidan_sanaladi()
    {
        await using var db = await NewDbAsync("absent");
        await SeedAsync(db);

        var dto = await GetAsync(db, Mon19, Tue20);

        // A was also absent on the 12th — read for "Oxirgi kelgan", but not counted.
        var a = Assert.Single(dto.AbsentStudents);
        Assert.Equal(1, a.MissedDays);
        Assert.Equal(2.0, dto.Stats.AverageGrade);            // C's grade on the 20th
        Assert.Equal(1, dto.Stats.FirstPaymentInPeriodCount); // C, on the 20th
    }

    [Fact]
    public async Task Bosh_davrda_raqamlar_bosh()
    {
        await using var db = await NewDbAsync("empty");
        await SeedAsync(db);

        var dto = await GetAsync(db, "2026-01-14", "2026-01-17"); // Wed–Sat: no lessons in the timetable

        Assert.Equal(0, dto.Stats.AverageGrade);
        Assert.Null(dto.Stats.AttendanceRate);
        Assert.Empty(dto.AbsentStudents);
        Assert.All(dto.AttendanceByPeriod, r => Assert.Equal(0, r.Expected));
        Assert.Equal(0, dto.Stats.FirstPaymentInPeriodCount);
        // Headcounts are the school's state now, not the period's.
        Assert.Equal(5, dto.Stats.StudentsCount);
    }

    [Fact]
    public async Task Davrsiz_eski_oynalar_saqlanadi()
    {
        await using var db = await NewDbAsync("none");
        await SeedAsync(db);

        var dto = (await new DashboardController(db).Get()).Value!;

        Assert.Equal(3.5, dto.Stats.AverageGrade); // all-time: 5 and 2
        Assert.Null(dto.Stats.FirstPaymentInPeriodCount);
    }

    [Theory]
    [InlineData(Mon19, Mon12)]          // from after to
    [InlineData(Mon12, null)]           // half a period
    [InlineData("12.01.2026", Mon12)]   // not yyyy-MM-dd
    public async Task Notogri_davr_400(string? from, string? to)
    {
        // Rejected before the database is touched.
        var result = await new DashboardController(null!).Get(from, to);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static async Task<AdminDashboardDto> GetAsync(AppDbContext db, string from, string to)
    {
        var result = await new DashboardController(db).Get(from, to);
        return Assert.IsType<AdminDashboardDto>(result.Value);
    }

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("dash_period_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// 5-A (A, B, C) and 6-A (F, G); both have Kimyo in periods 1–2 every Monday and Tuesday.
    /// Mon 12: 5-A marked, A absent both lessons, B got a 5. Tue 13: holiday.
    /// Tue 20: A absent (unexcused), C got a 2. B paid first on the 12th at 00:30, C on the 20th.
    /// </summary>
    private static async Task SeedAsync(AppDbContext db)
    {
        var fiveA = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var sixA = new SchoolClass { Name = "6-A", Grade = 6, Language = "uz" };
        var chemistry = new Subject { Name = "Kimyo" };
        db.Classes.AddRange(fiveA, sixA);
        db.Subjects.Add(chemistry);

        var a = NewStudent("Aliyev Ali", "5-A");
        var b = NewStudent("Berdiyev Bek", "5-A");
        var c = NewStudent("Comilov Can", "5-A");
        db.Students.AddRange(a, b, c, NewStudent("Fayzullayev Fozil", "6-A"), NewStudent("G'aniyev G'ani", "6-A"));

        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "SS" };
        db.AbsenceReasons.Add(unexcused);
        const string quarterStart = "2026-01-10", quarterEnd = "2026-03-20";
        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = quarterStart, EndDate = quarterEnd });
        db.Holidays.Add(new Holiday { Date = Tue13, Name = "Bayram" });

        var actor = new AppUser
        {
            FullName = "Mas'ul xodim", Role = Roles.Admin, Email = $"period.{Guid.NewGuid():N}@example.test", PasswordHash = "x",
        };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        foreach (var cls in new[] { fiveA, sixA })
        {
            var tpl = new ScheduleTemplate
            {
                ClassId = cls.Id, Name = "Asosiy",
                Lessons = [.. new[] { 0, 1 }.SelectMany(d => new[]
                {
                    new ScheduleLesson { Day = d, Period = 1, SubjectId = chemistry.Id },
                    new ScheduleLesson { Day = d, Period = 2, SubjectId = chemistry.Id },
                })],
            };
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
            foreach (var w in ScheduleMath.GetQuarterWeeks(quarterStart, quarterEnd))
                db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 3, Week = w.Week, TemplateId = tpl.Id });
        }

        JournalEntry Entry(Student s, string date, int period) => new()
        {
            ClassId = fiveA.Id, SubjectId = chemistry.Id, StudentId = s.Id, Date = date, Period = period, Quarter = 3,
        };
        foreach (var period in new[] { 1, 2 })
        {
            db.DailyAttendanceMarks.Add(new DailyAttendanceMark
            {
                ClassId = fiveA.Id, SubjectId = chemistry.Id, Date = Mon12, Period = period,
                MarkedBy = actor.Id, MarkedAt = AppClock.NowInstant,
            });
            var absent = Entry(a, Mon12, period);
            absent.ReasonId = unexcused.Id;
            db.JournalEntries.Add(absent);
        }
        var five = Entry(b, Mon12, 1);
        five.Grade = 5;
        var absentLater = Entry(a, Tue20, 1);
        absentLater.ReasonId = unexcused.Id;
        var two = Entry(c, Tue20, 1);
        two.Grade = 2;
        db.JournalEntries.AddRange(five, absentLater, two);

        long receipt = 1;
        Payment Pay(Student s, DateTimeOffset at) => new()
        {
            ReceiptNo = receipt++, StudentId = s.Id, Amount = 100_000m, Method = PaymentMethod.Cash,
            CashierId = actor.Id, ReceivedAt = at,
        };
        db.Payments.AddRange(
            Pay(b, new DateTimeOffset(2026, 1, 11, 19, 30, 0, TimeSpan.Zero)),   // 12.01 00:30 Tashkent
            Pay(c, new DateTimeOffset(2026, 1, 20, 5, 0, 0, TimeSpan.Zero)));    // 20.01 10:00 Tashkent
        await db.SaveChangesAsync();
    }

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
