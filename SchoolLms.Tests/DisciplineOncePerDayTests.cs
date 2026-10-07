using System.Net.Http.Json;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Client, 2026-10-07: "kun bo'yi kelmagan o'quvchidan har bir dars uchun ball ayirilmasin — kuniga
/// bir marta". Seven absent lessons on one day are ONE discipline event of that reason; a late mark
/// the same day is a second, separate event. Every discipline screen tells the same number.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DisciplineOncePerDayTests(ApiFixture fixture)
{
    private const string Base = "/api/admin/discipline";

    [Fact]
    public async Task Kun_boyi_kelmagan_oquvchidan_ball_bir_marta_ayiriladi()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"OD-{tag}", Grade = 5 };
        var className = cls.Id;
        var student = new Student { FullName = $"Bir Kunlik {tag}", ClassName = cls.Name, EnrollmentDate = "2026-01-01" };
        var unexcused = new AbsenceReason { Name = $"Sababsiz {tag}", Short = "S", IsLate = false, Points = -7 };
        var late = new AbsenceReason { Name = $"Kechikdi {tag}", Short = "K", IsLate = true, Points = -3 };
        var today = AppClock.Today;
        var day1 = today.AddDays(-2).ToString("yyyy-MM-dd");
        var day2 = today.AddDays(-1).ToString("yyyy-MM-dd");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.Add(student);
            db.AbsenceReasons.AddRange(unexcused, late);
            // Day 1: absent from all seven lessons, and a late mark in an eighth (two events).
            for (var p = 1; p <= 7; p++)
                db.JournalEntries.Add(Entry(className, student.Id, day1, p, unexcused.Id));
            db.JournalEntries.Add(Entry(className, student.Id, day1, 8, late.Id));
            // Day 2: one absent lesson (a third event).
            db.JournalEntries.Add(Entry(className, student.Id, day2, 1, unexcused.Id));
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Ballar nazorati: −7 (1-kun) −3 (1-kun, kechikish) −7 (2-kun) = −17, not −7×8−3 = −59.
        var scores = await admin.GetFromJsonAsync<List<DisciplineScoreRowDto>>($"{Base}/scores");
        var row = Assert.Single(scores!, r => r.StudentId == student.Id);
        Assert.Equal(17, row.Minus);
        Assert.Equal(100 - 17, row.Remaining);

        // Point history: one line per day and reason; the 7-lesson day says so.
        var points = await admin.GetFromJsonAsync<List<DisciplinePointDto>>($"{Base}/points?studentId={student.Id}");
        var attendance = points!.Where(p => p.Source == "attendance").ToList();
        Assert.Equal(3, attendance.Count);
        Assert.Contains(attendance, p => p.CreatedAt == day1 && p.Points == -7 && p.Note == "Jurnal davomati · 7 dars");
        Assert.Contains(attendance, p => p.CreatedAt == day1 && p.Points == -3 && p.Note == "Jurnal davomati");
        Assert.Contains(attendance, p => p.CreatedAt == day2 && p.Points == -7);

        // Harakatlar feed: the same three events, and its totals.
        var feed = await admin.GetFromJsonAsync<DisciplineFeedDto>(
            $"{Base}/feed?source=attendance&search={Uri.EscapeDataString(student.FullName)}&from={day1}&to={day2}");
        Assert.Equal(3, feed!.Total);
        Assert.Equal(-17, feed.PointsSum);

        // The attendance-discipline report and the student profile tell the same remaining score.
        await fixture.Api.WithDbAsync(async db =>
        {
            var report = await AttendanceDisciplineReport.BuildAsync(db, day1, day2);
            var line = Assert.Single(report.Students, s => s.StudentId == student.Id);
            Assert.Equal(-17, line.AttendancePoints);
            Assert.Equal(100 - 17, line.Remaining);
            Assert.Equal(2, Assert.Single(report.Reasons, r => r.ReasonId == unexcused.Id).Count);
        });
    }

    [Fact]
    public void Bir_kun_bir_sabab_bitta_hodisa_ikki_sabab_ikkita()
    {
        var days = AttendanceDisciplineDays.Collapse(
        [
            new("b", "s1", "2026-09-10", "abs"),
            new("a", "s1", "2026-09-10", "abs"),
            new("c", "s1", "2026-09-10", "late"),
            new("d", "s1", "2026-09-11", "abs"),
            new("e", "s2", "2026-09-10", "abs"),
        ]);

        Assert.Equal(4, days.Count);
        var first = Assert.Single(days, d => d.StudentId == "s1" && d.Date == "2026-09-10" && d.ReasonId == "abs");
        Assert.Equal(2, first.Lessons);
        Assert.Equal("a", first.Id);                       // the smallest id — stable
        Assert.Equal("Jurnal davomati · 2 dars", AttendanceDisciplineDays.Note(first.Lessons));
        Assert.Equal("Jurnal davomati", AttendanceDisciplineDays.Note(1));
    }

    private static JournalEntry Entry(string classId, string studentId, string date, int period, string reasonId) => new()
    {
        ClassId = classId, SubjectId = "matematika", Quarter = 1, StudentId = studentId,
        Date = date, Period = period, ReasonId = reasonId,
    };
}
