using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Kechki dars va yotoqxona davomati (mijoz, 2026-09-23):
/// kimdan olinadi (faqat faol yotoqxona abonementi borlar), ro'yxat yo'nalish guruhlari
/// bo'yicha, abonementsizni belgilab bo'lmaydi, ruxsat sessiya bo'yicha, ota-ona xabari.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BoardingAttendanceTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/boarding-attendance";
    private static readonly Guid Dormitory = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid Tuition = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private sealed record World(string Tag, Guid TrackId, string Boarder, string DayOnly, string Expired, string Loose, string Date);

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var today = AppClock.Today;
        var (admin, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        Student Pupil(string n, string cls) => new()
        {
            FullName = $"{n} {tag}", LastName = n, FirstName = tag, BirthDate = "2010-01-01",
            Gender = "male", ClassName = cls, EnrollmentDate = "2025-09-01",
        };
        var boarder = Pupil("Yotoqli", $"10-{tag}");
        var dayOnly = Pupil("Kunduzgi", $"10-{tag}");
        var expired = Pupil("Tugagan", $"11-{tag}");
        var loose = Pupil("Guruhsiz", $"9-{tag}");
        var subject = new Subject { Name = $"Yo'nalish {tag}" };
        var track = new StudyGroup
        {
            Name = $"Aniq fanlar {tag}", SubjectId = subject.Id, IsTrack = true,
            CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
        };
        StudyGroupMember Member(Student s) => new()
        {
            GroupId = track.Id, SubjectId = subject.Id, StudentId = s.Id, JoinedOn = today.AddDays(-30),
            CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
        };
        StudentSubscription Sub(Student s, Guid cat, DateOnly from, DateOnly? to) => new()
        {
            StudentId = s.Id, CategoryId = cat, MonthlyAmount = 1_000_000m, StartsOn = from, EndsOn = to,
            CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Subjects.Add(subject);
            db.Students.AddRange(boarder, dayOnly, expired, loose);
            db.StudyGroups.Add(track);
            db.StudyGroupMembers.AddRange(Member(boarder), Member(dayOnly), Member(expired));
            db.StudentSubscriptions.AddRange(
                Sub(boarder, Dormitory, today.AddDays(-10), null),
                Sub(dayOnly, Tuition, today.AddDays(-10), null),           // o'qish — yotoqxona emas
                Sub(expired, Dormitory, today.AddDays(-60), today.AddDays(-1)), // tugagan
                Sub(loose, Dormitory, today.AddDays(-5), null));           // guruhsiz yotoqxonachi
            await db.SaveChangesAsync();
        });
        return new World(tag, track.Id, boarder.Id, dayOnly.Id, expired.Id, loose.Id,
            today.ToString("yyyy-MM-dd"));
    }

    private static Task<BoardingDayDto?> DayAsync(HttpClient c, string date, string session) =>
        c.GetFromJsonAsync<BoardingDayDto>($"{Url}?date={date}&session={session}");

    [Fact]
    public async Task Faqat_faol_yotoqxona_abonementi_borlar_belgilanadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var day = (await DayAsync(admin, w.Date, BoardingSession.Evening))!;
        var track = Assert.Single(day.Sections, s => s.Key == w.TrackId.ToString());
        Assert.Equal("group", track.Kind);
        Assert.True(Assert.Single(track.Students, s => s.StudentId == w.Boarder).Eligible);
        Assert.False(Assert.Single(track.Students, s => s.StudentId == w.DayOnly).Eligible);   // kulrang
        Assert.False(Assert.Single(track.Students, s => s.StudentId == w.Expired).Eligible);   // muddati o'tgan
        Assert.Equal(1, track.Eligible);

        // Guruhsiz yotoqxonachi — o'z sinfi bo'limida.
        var loose = Assert.Single(day.Sections, s => s.Students.Any(x => x.StudentId == w.Loose));
        Assert.Equal("class", loose.Kind);

        var ok = await admin.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "evening",
            marks = new[] { new { studentId = w.Boarder, status = "absent" } },
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var after = (await DayAsync(admin, w.Date, BoardingSession.Evening))!;
        Assert.Equal("absent", after.Sections.SelectMany(s => s.Students).Single(s => s.StudentId == w.Boarder).Status);
        // Yotoqxona sessiyasi alohida — u yerda hali belgilanmagan.
        var dorm = (await DayAsync(admin, w.Date, BoardingSession.Dorm))!;
        Assert.Null(dorm.Sections.SelectMany(s => s.Students).Single(s => s.StudentId == w.Boarder).Status);

        var bad = await admin.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "evening",
            marks = new[] { new { studentId = w.DayOnly, status = "present" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Qayta_saqlash_qatorni_yangilaydi_takrorlamaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        foreach (var st in new[] { "present", "excused", "absent" })
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(Url, new
            {
                date = w.Date, session = "dorm",
                marks = new[] { new { studentId = w.Boarder, status = st } },
            })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.BoardingAttendance.Where(a => a.StudentId == w.Boarder && a.Session == "dorm").ToListAsync();
            Assert.Equal("absent", Assert.Single(rows).Status);
        });
    }

    [Fact]
    public async Task Ruxsat_sessiya_boyicha_oqish_ham()
    {
        var w = await SeedAsync();
        using var evening = await fixture.Api.ClientAsAsync(Roles.Staff, "attendanceEvening");
        Assert.Equal(HttpStatusCode.OK, (await evening.GetAsync($"{Url}?date={w.Date}&session=evening")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evening.GetAsync($"{Url}?date={w.Date}&session=dorm")).StatusCode);

        using var none = await fixture.Api.ClientAsAsync(Roles.Staff, "attendance");
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync($"{Url}?date={w.Date}&session=evening")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await none.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "evening", marks = new[] { new { studentId = w.Boarder, status = "present" } },
        })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Boshqa_rollar_403(string role)
    {
        using var c = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Url}?date=2026-01-01&session=evening")).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401_va_notogri_sorovlar_400()
    {
        using var anon = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Url}?date=2026-01-01&session=evening")).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Url}?date=2026-01-01&session=night")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Url}?date=01.01.2026&session=evening")).StatusCode);
        var future = AppClock.Today.AddDays(2).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url, new
        {
            date = future, session = "evening", marks = new[] { new { studentId = "x", status = "present" } },
        })).StatusCode);
    }

    [Fact]
    public void Ota_ona_xabari_sessiyaga_mos()
    {
        var d = new DateOnly(2026, 9, 23);
        Assert.Contains("kechki darsga kelmadi", BoardingAttendanceService.BuildMessage("Ali", d, BoardingSession.Evening));
        Assert.Contains("yotoqxonada yo'q", BoardingAttendanceService.BuildMessage("Ali", d, BoardingSession.Dorm));
        Assert.Contains("23.09.2026", BoardingAttendanceService.BuildMessage("Ali", d, BoardingSession.Dorm));
    }

    /// <summary>Guruh formasi "yo'nalish guruhi" belgisini saqlaydi va qaytaradi.</summary>
    [Fact]
    public async Task Guruh_yonalish_belgisi_saqlanadi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var subject = new Subject { Name = $"Yo'n {tag}" };
        var cls = new SchoolClass { Name = $"10-{tag}", Grade = 10 };
        var teacher = new Teacher { FullName = $"Ustoz {tag}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Subjects.Add(subject); db.Classes.Add(cls); db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
        });
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync("/api/admin/study-groups", new
        {
            name = $"Tibbiyot {tag}", subjectId = subject.Id, classIds = new[] { cls.Id },
            teacherIds = new[] { teacher.Id }, isTrack = true,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = (await res.Content.ReadFromJsonAsync<StudyGroupDetailDto>())!;
        Assert.True(dto.IsTrack);
    }
}
