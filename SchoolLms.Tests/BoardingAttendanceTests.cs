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
/// Kechki dars va yotoqxona davomati (mijoz, 2026-09-23): kimdan olinadi — 2026-10-11 dan qo'lda
/// yuritiladigan sessiya RO'YXATIDAN ("abonomentga qaralmasin"), bo'limlar yo'nalish guruhlari
/// bo'yicha, ro'yxatda yo'qni belgilab bo'lmaydi, ruxsat sessiya bo'yicha, ota-ona xabari.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BoardingAttendanceTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/boarding-attendance";

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
            // Yo'nalish guruhi fansiz (ck_study_groups_subject, TrackGroupsAsClasses).
            Name = $"Aniq fanlar {tag}", SubjectId = null, IsTrack = true,
            CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
        };
        StudyGroupMember Member(Student s) => new()
        {
            GroupId = track.Id, SubjectId = null, StudentId = s.Id, JoinedOn = today.AddDays(-30),
            CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
        };
        IEnumerable<BoardingRosterEntry> Roster(Student s, DateOnly from, DateOnly? to = null) =>
            BoardingSession.All.Select(session => new BoardingRosterEntry
            {
                Session = session, StudentId = s.Id, AddedOn = from, RemovedOn = to,
                AddedBy = admin.Id, CreatedAt = AppClock.NowInstant,
            });
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Subjects.Add(subject);
            db.Students.AddRange(boarder, dayOnly, expired, loose);
            db.StudyGroups.Add(track);
            db.StudyGroupMembers.AddRange(Member(boarder), Member(dayOnly), Member(expired));
            db.BoardingRosters.AddRange(Roster(boarder, today.AddDays(-10)));
            // dayOnly — ro'yxatda yo'q (guruhda bo'lsa ham)
            db.BoardingRosters.AddRange(Roster(expired, today.AddDays(-60), today));  // bugundan chiqarilgan
            db.BoardingRosters.AddRange(Roster(loose, today.AddDays(-5)));            // guruhsiz
            await db.SaveChangesAsync();
        });
        return new World(tag, track.Id, boarder.Id, dayOnly.Id, expired.Id, loose.Id,
            today.ToString("yyyy-MM-dd"));
    }

    private static Task<BoardingDayDto?> DayAsync(HttpClient c, string date, string session) =>
        c.GetFromJsonAsync<BoardingDayDto>($"{Url}?date={date}&session={session}");

    [Fact]
    public async Task Faqat_royxatdagilar_belgilanadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var day = (await DayAsync(admin, w.Date, BoardingSession.Evening))!;
        var track = Assert.Single(day.Sections, s => s.Key == w.TrackId.ToString());
        Assert.Equal("group", track.Kind);
        // Only the roster: the group's other pupil and the one taken off today are not listed.
        Assert.Equal([w.Boarder], track.Students.Select(s => s.StudentId));
        Assert.True(track.Students[0].Eligible);
        Assert.Equal(1, track.Eligible);
        // Yesterday the removed pupil was still on it.
        var yesterday = AppClock.Today.AddDays(-1).ToString("yyyy-MM-dd");
        Assert.Contains((await DayAsync(admin, yesterday, BoardingSession.Evening))!.Sections.SelectMany(s => s.Students),
            s => s.StudentId == w.Expired);

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

    /// <summary>
    /// 2026-09-26: kechki dars va yotoqxona ham K / Y + sabab. Sabab saqlanadi va qaytadi;
    /// holatga mos kelmagan sabab (keldi + yo'qlik sababi) rad etiladi.
    /// </summary>
    [Fact]
    public async Task Sabab_saqlanadi_va_holatga_mos_bolishi_shart()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Umumiy katalogdan foydalanamiz — yangi sabab qo'shish boshqa testlarga ta'sir qilardi.
        string? lateId = null, absentId = null;
        await fixture.Api.WithDbAsync(async db =>
        {
            lateId = await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).FirstOrDefaultAsync();
            absentId = await db.AbsenceReasons.Where(r => !r.IsLate).Select(r => r.Id).FirstOrDefaultAsync();
            if (lateId is null)
            {
                var r = new AbsenceReason { Name = $"Kech {w.Tag}", Short = "K9", IsLate = true };
                db.AbsenceReasons.Add(r); lateId = r.Id;
            }
            if (absentId is null)
            {
                var r = new AbsenceReason { Name = $"Kasal {w.Tag}", Short = "Q9", IsLate = false };
                db.AbsenceReasons.Add(r); absentId = r.Id;
            }
            await db.SaveChangesAsync();
        });

        var late = await admin.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "evening",
            marks = new[] { new { studentId = w.Boarder, status = "present", reasonId = lateId } },
        });
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        var day = (await DayAsync(admin, w.Date, BoardingSession.Evening))!;
        var row = day.Sections.SelectMany(s => s.Students).Single(s => s.StudentId == w.Boarder);
        Assert.Equal(("present", lateId), (row.Status, row.ReasonId));

        var absent = await admin.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "dorm",
            marks = new[] { new { studentId = w.Boarder, status = "excused", reasonId = absentId } },
        });
        Assert.Equal(HttpStatusCode.OK, absent.StatusCode);
        var dorm = (await DayAsync(admin, w.Date, BoardingSession.Dorm))!;
        Assert.Equal(absentId, dorm.Sections.SelectMany(s => s.Students).Single(s => s.StudentId == w.Boarder).ReasonId);

        var mismatch = await admin.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "evening",
            marks = new[] { new { studentId = w.Boarder, status = "present", reasonId = absentId } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
    }

    /// <summary>
    /// Ro'yxat qo'lda (2026-10-11): qo'shish, nomzodlar, chiqarish; har sessiyaning o'z ro'yxati va
    /// o'z ruxsati.
    /// </summary>
    [Fact]
    public async Task Royxat_qolda_yuritiladi()
    {
        var w = await SeedAsync();
        using var evening = await fixture.Api.ClientAsAsync(Roles.Staff, "attendanceEvening");

        var candidates = await evening.GetFromJsonAsync<List<BoardingCandidateDto>>(
            $"{Url}/roster/candidates?session=evening&q={w.Tag}");
        // Not on today's list: the group pupil never added and the one taken off today (can come back).
        Assert.Equal(new[] { w.DayOnly, w.Expired }.Order(StringComparer.Ordinal),
            candidates!.Select(c => c.StudentId).Order(StringComparer.Ordinal));
        Assert.NotNull(candidates.Single(c => c.StudentId == w.DayOnly).TrackName);

        var add = await evening.PostAsJsonAsync($"{Url}/roster", new { session = "evening", studentIds = new[] { w.DayOnly } });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var roster = await evening.GetFromJsonAsync<List<BoardingRosterRowDto>>($"{Url}/roster?session=evening");
        Assert.Contains(roster!, r => r.StudentId == w.DayOnly);

        // Now the pupil can be marked in the evening — but the dorm list did not change.
        Assert.Equal(HttpStatusCode.OK, (await evening.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "evening", marks = new[] { new { studentId = w.DayOnly, status = "present" } },
        })).StatusCode);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.DoesNotContain((await DayAsync(admin, w.Date, BoardingSession.Dorm))!.Sections.SelectMany(s => s.Students),
            s => s.StudentId == w.DayOnly);

        // Added today and removed today — the row goes; the mark already saved stays.
        Assert.Equal(HttpStatusCode.NoContent,
            (await evening.PostAsJsonAsync($"{Url}/roster/remove", new { session = "evening", studentId = w.DayOnly })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await evening.PostAsJsonAsync($"{Url}/roster/remove", new { session = "evening", studentId = w.DayOnly })).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.BoardingRosters.AnyAsync(r => r.StudentId == w.DayOnly));
            Assert.True(await db.BoardingAttendance.AnyAsync(a => a.StudentId == w.DayOnly));
        });

        // An older member is taken off from today, not deleted.
        Assert.Equal(HttpStatusCode.NoContent,
            (await evening.PostAsJsonAsync($"{Url}/roster/remove", new { session = "evening", studentId = w.Boarder })).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.BoardingRosters.SingleAsync(r => r.StudentId == w.Boarder && r.Session == "evening");
            Assert.Equal(AppClock.Today, row.RemovedOn);
        });

        // The evening keeper cannot touch the dorm list.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await evening.PostAsJsonAsync($"{Url}/roster", new { session = "dorm", studentIds = new[] { w.DayOnly } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evening.GetAsync($"{Url}/roster?session=dorm")).StatusCode);
    }

    /// <summary>"Davomat analitikasi 2.0" (2026-10-11): kechki dars / yotoqxona statistikasi.</summary>
    [Fact]
    public async Task Statistika_royxat_va_belgilardan()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(Url, new
        {
            date = w.Date, session = "evening",
            marks = new[] { new { studentId = w.Boarder, status = "absent" }, new { studentId = w.Loose, status = "present" } },
        })).StatusCode);

        var from = AppClock.Today.AddDays(-3).ToString("yyyy-MM-dd");
        var d = (await admin.GetFromJsonAsync<BoardingAnalyticsDto>($"{Url}/analytics?session=evening&from={from}&to={w.Date}"))!;
        var mine = d.Students.Where(s => s.StudentId == w.Boarder || s.StudentId == w.Loose).ToDictionary(s => s.StudentId);
        Assert.Equal((1, 1, 0), (mine[w.Boarder].Tally.StudentDays, mine[w.Boarder].Tally.Absent, mine[w.Boarder].Tally.Present));
        Assert.Equal(w.Date, mine[w.Boarder].LastAbsent);
        Assert.Equal(100.0, mine[w.Loose].Tally.PresentPct);
        Assert.StartsWith("Guruhsiz", mine[w.Loose].GroupName);
        Assert.Contains(d.Events, e => e.StudentId == w.Boarder && e.Status == "absent" && e.Date == w.Date);
        Assert.Contains(d.Groups, g => g.Key == w.TrackId.ToString());
        Assert.DoesNotContain(d.Students, s => s.StudentId == w.Expired);   // off the list today, no mark
        Assert.Contains(d.Trend, p => p.Date == w.Date);

        var xlsx = await admin.GetAsync($"{Url}/analytics/export?session=evening&from={from}&to={w.Date}");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);

        using var dormOnly = await fixture.Api.ClientAsAsync(Roles.Staff, "attendanceDorm");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await dormOnly.GetAsync($"{Url}/analytics?session=evening&from={from}&to={w.Date}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"{Url}/analytics?session=evening&from={w.Date}&to={from}")).StatusCode);
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
