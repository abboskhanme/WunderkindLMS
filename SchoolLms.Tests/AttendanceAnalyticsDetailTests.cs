using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "Davomat analitikasi" names the students (client, 2026-10-09: "qaysi o'quvchilar qaysi kuni qaysi
/// sababdan kelmagan haqidagi malumotlar bilish mumkin bo'lsin"): a row per student over the period
/// and a row per student-day that was not a plain "came" — with the reason and the missed lessons.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AttendanceAnalyticsDetailTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    /// <summary>2026-01-12 — Monday: 5-A has Kimyo in periods 1–3, marked; 6-A has it too, unmarked.</summary>
    private const string Monday = "2026-01-12";

    [Fact]
    public async Task Kim_qaysi_kuni_qaysi_sababdan_va_qaysi_darslarga_kelmagani_chiqadi()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("att_detail");
        _connectionStrings.Add(database.OwnerConnectionString);
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);

        var fiveA = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var sixA = new SchoolClass { Name = "6-A", Grade = 6, Language = "uz" };
        var chemistry = new Subject { Name = "Kimyo" };
        db.Classes.AddRange(fiveA, sixA);
        db.Subjects.Add(chemistry);
        var ali = Pupil("Aliyev Ali", "5-A");
        var bek = Pupil("Berdiyev Bek", "5-A");
        var can = Pupil("Comilov Can", "5-A");
        var dil = Pupil("Dilova Dil", "5-A");
        var fozil = Pupil("Fayzullayev Fozil", "6-A");
        db.Students.AddRange(ali, bek, can, dil, fozil);
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        var sick = new AbsenceReason { Name = "Kasal", Short = "K" };
        var late = new AbsenceReason { Name = "Kech qoldi", Short = "KQ", IsLate = true };
        db.AbsenceReasons.AddRange(unexcused, sick, late);
        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });
        var actor = new AppUser { FullName = "Mas'ul", Role = Roles.Staff, Email = $"d.{Guid.NewGuid():N}", PasswordHash = "x" };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var week = ScheduleMath.GetQuarterWeeks("2026-01-10", "2026-03-20")
            .First(w => string.CompareOrdinal(Monday, w.StartISO) >= 0 && string.CompareOrdinal(Monday, w.EndISO) <= 0);
        foreach (var cls in new[] { fiveA, sixA })
        {
            var tpl = new ScheduleTemplate
            {
                ClassId = cls.Id, Name = "Asosiy",
                Lessons = [.. new[] { 1, 2, 3 }.Select(p => new ScheduleLesson { Day = 0, Period = p, SubjectId = chemistry.Id })],
            };
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
            db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 3, Week = week.Week, TemplateId = tpl.Id });
        }

        void Row(Student s, int period, AbsenceReason r) => db.JournalEntries.Add(new JournalEntry
        {
            ClassId = fiveA.Id, SubjectId = chemistry.Id, StudentId = s.Id, Date = Monday, Period = period,
            Quarter = 3, ReasonId = r.Id,
        });
        foreach (var period in new[] { 1, 2, 3 })
        {
            db.DailyAttendanceMarks.Add(new DailyAttendanceMark
            {
                ClassId = fiveA.Id, SubjectId = chemistry.Id, Date = Monday, Period = period,
                MarkedBy = actor.Id, MarkedAt = AppClock.NowInstant,
            });
            Row(ali, period, unexcused);           // Ali: did not come at all
        }
        Row(bek, 2, sick);                          // Bek: missed lesson 2, ill
        Row(can, 1, late);                          // Can: late to lesson 1
        await db.SaveChangesAsync();                // Dil: came to everything

        var r = await AttendanceAnalytics.BuildAsync(db, null, Monday, Monday, Monday);

        var events = r.Events!.ToDictionary(e => e.FullName);
        Assert.Equal(4, events.Count);               // Ali, Bek, Can, Fozil — not Dil

        var a = events["Aliyev Ali"];
        Assert.Equal(("absent", Monday, "5-A", true, 3), (a.Status, a.Date, a.RowName, a.Unexcused, a.HeldLessons));
        Assert.Equal([1, 2, 3], a.Periods);
        Assert.Equal([unexcused.Id], a.ReasonIds);

        var b = events["Berdiyev Bek"];
        Assert.Equal(("partial", false), (b.Status, b.Unexcused));
        Assert.Equal([2], b.Periods);
        Assert.Equal([sick.Id], b.ReasonIds);

        var c = events["Comilov Can"];
        Assert.Equal("late", c.Status);
        Assert.Equal([1], c.Periods);
        Assert.Equal([late.Id], c.ReasonIds);

        Assert.Equal("unchecked", events["Fayzullayev Fozil"].Status);   // 6-A: nobody took attendance

        var students = r.Students!.ToDictionary(s => s.FullName);
        Assert.Equal(5, students.Count);
        Assert.Equal((1, 1, 1), (students["Aliyev Ali"].Tally.Opportunities, students["Aliyev Ali"].Tally.Absent,
            students["Aliyev Ali"].Tally.Unexcused));
        Assert.Equal((1, 1), (students["Berdiyev Bek"].Tally.Present, students["Berdiyev Bek"].Tally.Partial));
        Assert.Equal(100.0, students["Dilova Dil"].Tally.PresentPct);
        Assert.False(r.EventsTruncated);

        // One class: only its own pupils.
        var only5A = await AttendanceAnalytics.BuildAsync(db, fiveA.Id, Monday, Monday, Monday);
        Assert.Equal(4, only5A.Students!.Count);
        // One class: each student carries the days for the month grid.
        Assert.All(only5A.Students!, s => Assert.Equal([Monday], s.Dates!));
        Assert.Null(r.Students!.First().Dates);   // the whole school does not
        Assert.DoesNotContain(only5A.Events!, e => e.FullName == "Fayzullayev Fozil");
    }

    private static Student Pupil(string fullName, string className) => new()
    {
        FullName = fullName, LastName = fullName.Split(' ')[0], FirstName = fullName.Split(' ')[1],
        BirthDate = "2015-01-01", Gender = "male", ClassName = className, EnrollmentDate = "2025-09-01",
    };
}
