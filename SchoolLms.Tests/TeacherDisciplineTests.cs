using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Xulq-atvor from the teacher's lesson screen (docs/modules/behaviour.md §Teachers, client 2026-10-15).
//
//  World (one per test, unique names):
//    · class A — the subject teacher T has a lesson there (pupils a1, a2 and an archived one);
//    · class B — H is its homeroom teacher, no lessons (pupil b1);
//    · class C — nobody's but a substitution today for S (pupil c1); class D — nobody's (pupil d1);
//    · reasons: +3 positive, −10 negative with NotifyParent, an inactive one, and an attendance reason.
//  The bot token is empty in tests, so NotifyParent sends nothing and the answer carries 0.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class TeacherDisciplineTests(ApiFixture fixture)
{
    private const string Base = "/api/teacher/discipline";

    // =====================================================================
    //  1. Gate: teacher role + journal key; reach decides the pupil
    // =====================================================================

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData("parent")]
    public async Task Non_teacher_roles_are_refused(string role)
    {
        var w = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(role, "discipline");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/reasons")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Add(client, w.A1, w.PositiveId)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Base}/reasons")).StatusCode);
    }

    [Fact]
    public async Task Teacher_without_the_journal_key_is_refused()
    {
        var w = await SeedAsync();
        var (_, noJournal) = await TeacherAsync(w, TeacherPermissions.Schedule, lessonInA: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await noJournal.GetAsync($"{Base}/reasons")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Add(noJournal, w.A1, w.PositiveId)).StatusCode);
    }

    [Fact]
    public async Task Reasons_are_the_active_behaviour_reasons_with_their_type()
    {
        var w = await SeedAsync();
        var reasons = await ArrayAsync(w.T, $"{Base}/reasons");

        var positive = reasons.Single(r => Str(r, "id") == w.PositiveId);
        Assert.Equal("positive", Str(positive, "type"));
        Assert.Equal(3, positive.GetProperty("points").GetInt32());
        var negative = reasons.Single(r => Str(r, "id") == w.NegativeId);
        Assert.Equal("negative", Str(negative, "type"));
        Assert.True(negative.GetProperty("notifyParent").GetBoolean());
        Assert.DoesNotContain(reasons, r => Str(r, "id") == w.InactiveId);
        Assert.DoesNotContain(reasons, r => Str(r, "id") == w.AttendanceReasonId);
    }

    [Fact]
    public async Task Subject_teacher_records_for_a_pupil_of_their_class_author_from_the_token()
    {
        var w = await SeedAsync();
        var response = await Add(w.T, w.A1, w.NegativeId, "Tanaffusda urushdi");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(-10, body.RootElement.GetProperty("points").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("notifiedParents").GetInt32());
        Assert.True(body.RootElement.GetProperty("canDelete").GetBoolean());

        await fixture.Api.WithDbAsync(async db =>
        {
            var p = await db.DisciplinePoints.AsNoTracking().SingleAsync(x => x.StudentId == w.A1);
            Assert.Equal((w.NegativeId, -10, "Tanaffusda urushdi"), (p.ReasonId, p.Points, p.Note));
            Assert.Equal(w.TName, p.CreatedBy);
            Assert.Equal(w.TUserId, p.CreatedByUserId);
        });

        // It is the same entry the office sees in Harakatlar.
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var feed = await ObjectAsync(admin, $"/api/admin/discipline/feed?className={w.ClassA}");
        var row = Assert.Single(feed.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(w.TName, Str(row, "createdBy"));
    }

    [Fact]
    public async Task Homeroom_teacher_and_todays_substitute_reach_their_pupils()
    {
        var w = await SeedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Add(w.H, w.B1, w.PositiveId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Add(w.S, w.C1, w.PositiveId)).StatusCode);

        // …and nobody else's.
        Assert.Equal(HttpStatusCode.Forbidden, (await Add(w.T, w.B1, w.PositiveId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Add(w.H, w.A1, w.PositiveId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Add(w.S, w.D1, w.PositiveId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.T.GetAsync($"{Base}/points?studentId={w.D1}")).StatusCode);
    }

    [Fact]
    public async Task A_substitution_on_another_day_does_not_open_the_class()
    {
        var w = await SeedAsync();
        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.LessonSubstitutions.SingleAsync(x => x.OwnerId == w.ClassCId);
            s.Date = AppClock.Today.AddDays(-1);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await Add(w.S, w.C1, w.PositiveId)).StatusCode);
    }

    [Fact]
    public async Task Inactive_or_attendance_reason_and_archived_pupil_are_refused()
    {
        var w = await SeedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(w.T, w.A1, w.InactiveId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(w.T, w.A1, w.AttendanceReasonId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Add(w.T, w.Archived, w.PositiveId)).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.DisciplinePoints.AnyAsync(p => p.StudentId == w.A1 || p.StudentId == w.Archived)));
    }

    // =====================================================================
    //  2. Reads: the roster badges and the pupil sheet
    // =====================================================================

    [Fact]
    public async Task Scores_give_every_roster_pupil_the_100_based_score_and_todays_points()
    {
        var w = await SeedAsync();
        await fixture.Api.WithDbAsync(async db =>
        {
            // An old entry (not today) and an attendance day worth −2.
            db.DisciplinePoints.Add(new DisciplinePoint
            {
                StudentId = w.A2, ReasonId = w.PositiveId, ReasonName = "Eski", Points = 5,
                CreatedAt = "2026-09-01T09:00:00", CreatedBy = "Admin",
            });
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = w.ClassAId, SubjectId = w.SubjectId, Quarter = 1, StudentId = w.A2,
                Date = "2026-09-02", Period = 1, ReasonId = w.AttendanceReasonId,
            });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await Add(w.T, w.A1, w.NegativeId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Add(w.T, w.A1, w.PositiveId)).StatusCode);

        var rows = await ArrayAsync(w.T, $"{Base}/scores?ownerId={w.ClassAId}");
        Assert.Equal(2, rows.Count); // the archived pupil is not on the roster
        var a1 = rows.Single(r => Str(r, "studentId") == w.A1);
        Assert.Equal((93, -7), (a1.GetProperty("remaining").GetInt32(), a1.GetProperty("today").GetInt32()));
        var a2 = rows.Single(r => Str(r, "studentId") == w.A2);
        Assert.Equal((103, 0), (a2.GetProperty("remaining").GetInt32(), a2.GetProperty("today").GetInt32()));

        // Not the teacher's class → 403.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.T.GetAsync($"{Base}/scores?ownerId={w.ClassDId}")).StatusCode);
    }

    [Fact]
    public async Task Pupil_sheet_lists_history_with_canDelete_only_on_own_entries_of_today()
    {
        var w = await SeedAsync();
        await fixture.Api.WithDbAsync(async db =>
        {
            db.DisciplinePoints.Add(new DisciplinePoint
            {
                StudentId = w.A1, ReasonId = w.PositiveId, ReasonName = "Kecha", Points = 3,
                CreatedAt = AppClock.Today.AddDays(-1).ToString("yyyy-MM-dd") + "T10:00:00",
                CreatedBy = w.TName, CreatedByUserId = w.TUserId,
            });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await Add(w.T, w.A1, w.NegativeId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Add(w.H2, w.A1, w.PositiveId)).StatusCode);

        var sheet = await ObjectAsync(w.T, $"{Base}/points?studentId={w.A1}");
        Assert.Equal(96, sheet.GetProperty("remaining").GetInt32());
        Assert.Equal(-7, sheet.GetProperty("today").GetInt32());
        var items = sheet.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Single(items, i => i.GetProperty("canDelete").GetBoolean());
        Assert.True(items.Single(i => i.GetProperty("points").GetInt32() == -10).GetProperty("canDelete").GetBoolean());
    }

    // =====================================================================
    //  3. Delete: own entry, same day only
    // =====================================================================

    [Fact]
    public async Task Teacher_deletes_own_entry_of_today_but_not_others_or_older_ones()
    {
        var w = await SeedAsync();
        var own = await IdOf(await Add(w.T, w.A1, w.NegativeId));
        var colleague = await IdOf(await Add(w.H2, w.A1, w.PositiveId));
        var yesterday = Guid.NewGuid().ToString();
        await fixture.Api.WithDbAsync(async db =>
        {
            db.DisciplinePoints.Add(new DisciplinePoint
            {
                Id = yesterday, StudentId = w.A1, ReasonId = w.PositiveId, ReasonName = "Kecha", Points = 3,
                CreatedAt = AppClock.Today.AddDays(-1).ToString("yyyy-MM-dd") + "T10:00:00",
                CreatedBy = w.TName, CreatedByUserId = w.TUserId,
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Forbidden, (await w.T.DeleteAsync($"{Base}/points/{colleague}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.T.DeleteAsync($"{Base}/points/{yesterday}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.T.DeleteAsync($"{Base}/points/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.T.DeleteAsync($"{Base}/points/{own}")).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var left = await db.DisciplinePoints.AsNoTracking().Where(p => p.StudentId == w.A1)
                .Select(p => p.Id).ToListAsync();
            Assert.Equal(new[] { colleague, yesterday }.Order(), left.Order());
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.EntityType == AuditService.EntityDisciplinePoint && a.EntityId == own && a.Action == "delete"));
        });
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record World(
        string ClassA, string ClassAId, string ClassCId, string ClassDId, string SubjectId,
        string A1, string A2, string Archived, string B1, string C1, string D1,
        string PositiveId, string NegativeId, string InactiveId, string AttendanceReasonId,
        HttpClient T, string TName, string TUserId, HttpClient H, HttpClient H2, HttpClient S);

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var classA = new SchoolClass { Name = $"XA-{tag}", Grade = 5 };
        var classB = new SchoolClass { Name = $"XB-{tag}", Grade = 5 };
        var classC = new SchoolClass { Name = $"XC-{tag}", Grade = 5 };
        var classD = new SchoolClass { Name = $"XD-{tag}", Grade = 5 };
        var subject = new Subject { Name = $"Matematika {tag}" };
        var a1 = GeneralSettingsFlagsTests.NewStudent($"Ali {tag}", classA.Name, "+998900000091");
        var a2 = GeneralSettingsFlagsTests.NewStudent($"Vali {tag}", classA.Name, "+998900000092");
        var archived = GeneralSettingsFlagsTests.NewStudent($"Ketgan {tag}", classA.Name, "+998900000093");
        archived.IsArchived = true;
        var b1 = GeneralSettingsFlagsTests.NewStudent($"Rahbarlikda {tag}", classB.Name, "+998900000094");
        var c1 = GeneralSettingsFlagsTests.NewStudent($"O'rindosh {tag}", classC.Name, "+998900000095");
        var d1 = GeneralSettingsFlagsTests.NewStudent($"Begona {tag}", classD.Name, "+998900000096");
        var positive = new DisciplineReason { Name = $"Faol ishtirok {tag}", Points = 3 };
        var negative = new DisciplineReason { Name = $"Urushdi {tag}", Points = -10, NotifyParent = true };
        var inactive = new DisciplineReason { Name = $"Eski sabab {tag}", Points = -2, IsActive = false };
        var attendance = new AbsenceReason { Name = $"Sababsiz {tag}", Short = "S", Points = -2 };
        var absentTeacher = new Teacher { FullName = $"Kasal ustoz {tag}" };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB, classC, classD);
            db.Subjects.Add(subject);
            db.Students.AddRange(a1, a2, archived, b1, c1, d1);
            db.DisciplineReasons.AddRange(positive, negative, inactive);
            db.AbsenceReasons.Add(attendance);
            db.Teachers.Add(absentTeacher);
            await db.SaveChangesAsync();
        });

        var w0 = new World(classA.Name, classA.Id, classC.Id, classD.Id, subject.Id,
            a1.Id, a2.Id, archived.Id, b1.Id, c1.Id, d1.Id,
            positive.Id, negative.Id, inactive.Id, attendance.Id,
            null!, "", "", null!, null!, null!);

        var (tUser, t) = await TeacherAsync(w0, TeacherPermissions.Journal, lessonInA: true);
        var (_, h) = await TeacherAsync(w0, TeacherPermissions.Journal, homeroomOf: classB.Id);
        // A second teacher of class A (homeroom) — a colleague whose entries T cannot delete.
        var (_, h2) = await TeacherAsync(w0, TeacherPermissions.Journal, homeroomOf: classA.Id);
        var (sUser, s) = await TeacherAsync(w0, TeacherPermissions.Journal);

        await fixture.Api.WithDbAsync(async db =>
        {
            var substitute = await db.Teachers.SingleAsync(x => x.UserId == sUser.Id);
            db.LessonSubstitutions.Add(new LessonSubstitution
            {
                Date = AppClock.Today, Period = 2, OwnerKind = LessonOwnerKind.Class, OwnerId = classC.Id,
                SubjectId = subject.Id, AbsentTeacherId = absentTeacher.Id, SubstituteTeacherId = substitute.Id,
                CreatedBy = "test", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        return w0 with { T = t, TName = tUser.FullName, TUserId = tUser.Id, H = h, H2 = h2, S = s };
    }

    private async Task<(AppUser User, HttpClient Client)> TeacherAsync(
        World w, string permission, bool lessonInA = false, string? homeroomOf = null)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [permission];
            if (lessonInA)
            {
                var tpl = new ScheduleTemplate { ClassId = w.ClassAId, Name = "Asosiy " + user.Id[..6] };
                tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 1, Period = 1, SubjectId = w.SubjectId, TeacherId = t.Id });
                db.ScheduleTemplates.Add(tpl);
            }
            if (homeroomOf is not null)
                db.ClassHomeroomTeachers.Add(new ClassHomeroomTeacher { ClassId = homeroomOf, TeacherId = t.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user, fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, string studentId, string reasonId, string? note = null) =>
        client.PostAsJsonAsync($"{Base}/points", new { studentId, reasonId, note });

    private static async Task<string> IdOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    private static string? Str(JsonElement e, string name) => e.GetProperty(name).GetString();

    private static async Task<JsonElement> ObjectAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> ArrayAsync(HttpClient client, string url) =>
        (await ObjectAsync(client, url)).EnumerateArray().ToList();
}
