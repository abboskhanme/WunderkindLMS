using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "Sinfim · Kelmaganlar" (2026-10-26): the homeroom teacher sees their own pupils' missed days, changes
/// the reason (written into the journal, late marks untouched) and writes why the pupil was away. The
/// comment reaches the admin attendance report. Each test has its own database — the list reads the
/// school's whole day.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class HomeroomAbsencesTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    private static readonly DateOnly Day = AppClock.Today.AddDays(-1);
    private static readonly string DayIso = Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Royxatda_faqat_mening_kelmagan_oquvchilarim_kech_kelgan_va_kelgan_yoq()
    {
        var (db, s) = await SetupAsync("homeroom_abs_list");
        var list = await new HomeroomAbsenceService(db).ListAsync(s.Homeroom.Id, Day);

        Assert.Equal(["5-A"], list.Groups);
        Assert.Equal(new[] { "Ali Test", "Bek Test" }, list.Rows.Select(r => r.FullName));
        var ali = list.Rows[0];
        Assert.Equal((StudentDay.Absent, s.Unexcused, true), (ali.Status, ali.ReasonId, ali.Unexcused));
        Assert.Equal([1, 2], ali.Periods);
        var bek = list.Rows[1];
        Assert.Equal(StudentDay.Partial, bek.Status);
        Assert.Equal([1], bek.Periods);
        // Late reasons are not offered: the pupil was in class.
        Assert.DoesNotContain(list.Reasons, r => r.Id == s.Late);
        Assert.Equal(2, list.Reasons.Count);

        // 5-B's homeroom teacher sees only their own Eli — none of 5-A.
        var other = await new HomeroomAbsenceService(db).ListAsync(s.Stranger.Id, Day);
        Assert.Equal(["Eli Test"], other.Rows.Select(r => r.FullName));
    }

    [Fact]
    public async Task Sabab_jurnalga_yoziladi_kech_belgisi_qoladi_izoh_saqlanadi_va_hisobotda_korinadi()
    {
        var (db, s) = await SetupAsync("homeroom_abs_save");
        var service = new HomeroomAbsenceService(db);

        var row = await service.SaveAsync(s.Homeroom, s.Pupils["Ali"], Day,
            new SaveHomeroomAbsenceRequest(s.Excused, "  Isitmasi bor, onasi qo'ng'iroq qildi  "));
        Assert.Equal((s.Excused, false), (row.ReasonId, row.Unexcused));
        Assert.Equal("Isitmasi bor, onasi qo'ng'iroq qildi", row.Comment);
        Assert.Equal("Rahbar Ustoz", row.CommentBy);

        var aliEntries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.StudentId == s.Pupils["Ali"] && e.Date == DayIso).ToListAsync();
        Assert.All(aliEntries, e => Assert.Equal(s.Excused, e.ReasonId));

        // Bek: the missed lesson gets the new reason, the late mark on the second stays late.
        await service.SaveAsync(s.Homeroom, s.Pupils["Bek"], Day, new SaveHomeroomAbsenceRequest(s.Excused, null));
        var bekEntries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.StudentId == s.Pupils["Bek"] && e.Date == DayIso).OrderBy(e => e.Period).ToListAsync();
        Assert.Equal([s.Excused, s.Late], bekEntries.Select(e => e.ReasonId));

        var audits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == AuditService.EntityAttendanceDay).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, a => a.Summary.Contains("izoh yozildi") && a.Summary.Contains("«Sababsiz» → «Kasal»"));

        // The admin report shows the comment next to the day.
        var report = await AttendanceAnalytics.BuildAsync(db, null, DayIso, DayIso, DayIso);
        var ev = report.Events!.Single(e => e.StudentId == s.Pupils["Ali"]);
        Assert.Equal("Isitmasi bor, onasi qo'ng'iroq qildi", ev.Comment);

        // Empty comment removes it; saving the same thing again writes nothing.
        await service.SaveAsync(s.Homeroom, s.Pupils["Ali"], Day, new SaveHomeroomAbsenceRequest(null, ""));
        Assert.False(await db.AbsenceComments.AnyAsync());
        await service.SaveAsync(s.Homeroom, s.Pupils["Ali"], Day, new SaveHomeroomAbsenceRequest(s.Excused, ""));
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.EntityType == AuditService.EntityAttendanceDay));
    }

    [Fact]
    public async Task Begona_oquvchi_kelgan_kun_kech_sabab_va_kelajak_rad_etiladi()
    {
        var (db, s) = await SetupAsync("homeroom_abs_rules");
        var service = new HomeroomAbsenceService(db);
        var req = new SaveHomeroomAbsenceRequest(s.Excused, "Izoh");

        async Task<int> Status(Func<Task> act) =>
            (await Assert.ThrowsAsync<AttendanceCaseException>(act)).Status;

        Assert.Equal(404, await Status(() => service.SaveAsync(s.Stranger, s.Pupils["Ali"], Day, req)));
        Assert.Equal(409, await Status(() => service.SaveAsync(s.Homeroom, s.Pupils["Dil"], Day, req)));
        Assert.Equal(400, await Status(() =>
            service.SaveAsync(s.Homeroom, s.Pupils["Ali"], Day, new SaveHomeroomAbsenceRequest(s.Late, null))));
        Assert.Equal(400, await Status(() => service.SaveAsync(s.Homeroom, s.Pupils["Ali"], AppClock.Today.AddDays(1), req)));
        Assert.Equal(400, await Status(() => service.SaveAsync(s.Homeroom, s.Pupils["Ali"], Day,
            new SaveHomeroomAbsenceRequest(null, new string('x', HomeroomAbsenceService.MaxCommentLength + 1)))));

        Assert.False(await db.AbsenceComments.AnyAsync());
        Assert.All(await db.JournalEntries.AsNoTracking().Where(e => e.StudentId == s.Pupils["Ali"]).ToListAsync(),
            e => Assert.Equal(s.Unexcused, e.ReasonId));
    }

    // =====================================================================

    private sealed record School(
        Teacher Homeroom, Teacher Stranger, string Unexcused, string Excused, string Late,
        Dictionary<string, string> Pupils);

    /// <summary>
    /// 5-A with two lessons yesterday: Ali away all day (unexcused), Bek away from the first and late
    /// to the second, Dil present. 5-B's Eli away all day — not this teacher's pupil.
    /// </summary>
    private async Task<(AppDbContext, School)> SetupAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        var db = PostgresFixture.NewContext(database.OwnerConnectionString);

        var a = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var b = new SchoolClass { Name = "5-B", Grade = 5, Language = "uz" };
        var math = new Subject { Name = "Matematika" };
        var physics = new Subject { Name = "Fizika" };
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        var excused = new AbsenceReason { Name = "Kasal", Short = "K" };
        var late = new AbsenceReason { Name = "Kech keldi", Short = "Kch", IsLate = true };
        var user = new AppUser { FullName = "Rahbar Ustoz", Role = Roles.Teacher, Email = $"rahbar.{name}" };
        var homeroom = new Teacher { FullName = "Rahbar Ustoz", UserId = user.Id };
        var stranger = new Teacher { FullName = "Begona Ustoz", UserId = Guid.NewGuid().ToString() };
        db.Users.Add(user);
        db.Classes.AddRange(a, b);
        db.Subjects.AddRange(math, physics);
        db.AbsenceReasons.AddRange(unexcused, excused, late);
        db.Teachers.AddRange(homeroom, stranger);
        var pupils = new Dictionary<string, Student>
        {
            ["Ali"] = Pupil("Ali Test", "5-A"),
            ["Bek"] = Pupil("Bek Test", "5-A"),
            ["Dil"] = Pupil("Dil Test", "5-A"),
            ["Eli"] = Pupil("Eli Test", "5-B"),
        };
        db.Students.AddRange(pupils.Values);
        await db.SaveChangesAsync();
        db.ClassHomeroomTeachers.AddRange(
            new ClassHomeroomTeacher { ClassId = a.Id, TeacherId = homeroom.Id, CreatedAt = AppClock.NowInstant },
            new ClassHomeroomTeacher { ClassId = b.Id, TeacherId = stranger.Id, CreatedAt = AppClock.NowInstant });

        foreach (var cls in new[] { a, b })
            foreach (var (subject, period) in new[] { (math.Id, 1), (physics.Id, 2) })
            {
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = cls.Id, SubjectId = subject, Quarter = 1, Date = DayIso, Period = period,
                    Topic = "Mavzu", Conducted = true,
                });
                db.DailyAttendanceMarks.Add(new DailyAttendanceMark
                {
                    ClassId = cls.Id, SubjectId = subject, Date = DayIso, Period = period,
                    MarkedBy = "nurse", MarkedAt = AppClock.NowInstant,
                });
            }

        void Mark(SchoolClass cls, string pupil, string subject, int period, string reason) =>
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = cls.Id, SubjectId = subject, Quarter = 1, StudentId = pupils[pupil].Id,
                Date = DayIso, Period = period, ReasonId = reason,
            });
        Mark(a, "Ali", math.Id, 1, unexcused.Id);
        Mark(a, "Ali", physics.Id, 2, unexcused.Id);
        Mark(a, "Bek", math.Id, 1, unexcused.Id);
        Mark(a, "Bek", physics.Id, 2, late.Id);
        Mark(b, "Eli", math.Id, 1, unexcused.Id);
        Mark(b, "Eli", physics.Id, 2, unexcused.Id);
        await db.SaveChangesAsync();

        return (db, new School(homeroom, stranger, unexcused.Id, excused.Id, late.Id,
            pupils.ToDictionary(kv => kv.Key, kv => kv.Value.Id)));
    }

    private static Student Pupil(string fullName, string className)
    {
        var p = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998901112233");
        p.Id = Guid.NewGuid().ToString();
        return p;
    }
}
