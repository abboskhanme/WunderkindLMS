using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// A teacher who is also a dorm commandant (client, 2026-10-12: "o'qituvchi profilida role berish mumkin
/// bo'lsin"): the superadmin gives the teacher account an access role, and the teacher marks the evening
/// lesson / dorm roll call with the same login. Only the roll-call keys of a role ever apply to a teacher.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherCommandantTests(ApiFixture fixture)
{
    private const string RolesUrl = "/api/admin/roles";
    private const string TeachersUrl = "/api/admin/teachers";
    private const string BoardingUrl = "/api/admin/boarding-attendance";

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    private async Task<AccessRoleDto> CreateRoleAsync(HttpClient boss, params string[] permissions)
    {
        var res = await boss.PostAsJsonAsync(RolesUrl, new AccessRolePayload(Unique("Komendant"), null, [.. permissions]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AccessRoleDto>())!;
    }

    /// <summary>A teacher (account + teacher row): its teacher id, account id and a client signed in as it.</summary>
    private async Task<(string TeacherId, string UserId, HttpClient Client)> TeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
            teacherId = (await db.Teachers.SingleAsync(t => t.UserId == user.Id)).Id);
        return (teacherId, user.Id, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private Task<HttpResponseMessage> AssignAsync(HttpClient client, string teacherId, Guid? roleId) =>
        client.PutAsJsonAsync($"{TeachersUrl}/{teacherId}/role", new SetStaffRoleRequest(roleId));

    private static Task<HttpResponseMessage> DayAsync(HttpClient client, string session) =>
        client.GetAsync($"{BoardingUrl}?date={Today}&session={session}");

    [Fact]
    public async Task Komendant_oqituvchi_yotoqxona_davomatini_oladi()
    {
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var role = await CreateRoleAsync(boss, "attendanceDorm");
        var (teacherId, userId, client) = await TeacherAsync();
        using var teacher = client;

        // Without the role the roll call is closed.
        Assert.Equal(HttpStatusCode.Forbidden, (await DayAsync(teacher, BoardingSession.Dorm)).StatusCode);

        var assigned = await AssignAsync(boss, teacherId, role.Id);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Equal(role.Name, (await assigned.Content.ReadFromJsonAsync<TeacherRoleDto>())!.AccessRoleName);

        // The profile carries the role, the dorm opens, the evening lesson stays closed.
        var me = (await teacher.GetFromJsonAsync<TeacherProfileDto>("/api/teacher/me"))!;
        Assert.Equal(role.Name, me.AccessRoleName);
        Assert.Equal(["attendanceDorm"], me.RolePermissions);
        Assert.Equal(HttpStatusCode.OK, (await DayAsync(teacher, BoardingSession.Dorm)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DayAsync(teacher, BoardingSession.Evening)).StatusCode);

        // Marking works with the teacher's own login.
        var pupil = new Student
        {
            FullName = $"Yotoqli {role.Id:N}"[..20], LastName = "Yotoqli", FirstName = "Bola", BirthDate = "2010-01-01",
            Gender = "male", ClassName = "10-K", EnrollmentDate = "2025-09-01",
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(pupil);
            db.BoardingRosters.Add(new BoardingRosterEntry
            {
                Session = BoardingSession.Dorm, StudentId = pupil.Id, AddedOn = AppClock.Today.AddDays(-1),
                AddedBy = userId, CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
        });
        var saved = await teacher.PutAsJsonAsync(BoardingUrl, new SaveBoardingRequest(Today, BoardingSession.Dorm,
            [new BoardingMarkInput(pupil.Id, BoardingStatus.Present)]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(1, (await saved.Content.ReadFromJsonAsync<SaveBoardingResult>())!.Saved);

        // The staff list shows the teacher's role; removing it closes the roll call again.
        var rows = (await boss.GetFromJsonAsync<List<TeacherRoleDto>>($"{TeachersUrl}/roles"))!;
        Assert.Equal(role.Id, Assert.Single(rows, r => r.TeacherId == teacherId).AccessRoleId);
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(boss, teacherId, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DayAsync(teacher, BoardingSession.Dorm)).StatusCode);
        Assert.Null((await teacher.GetFromJsonAsync<TeacherProfileDto>("/api/teacher/me"))!.AccessRoleName);
    }

    /// <summary>A male teacher-commandant (2026-10-15): the role's gender flag reaches the teacher and limits the dorm.</summary>
    [Fact]
    public async Task Erkak_komendant_oqituvchi_faqat_ogil_bolalarni_koradi()
    {
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var role = await CreateRoleAsync(boss, "/admin/attendance/boarding?session=dorm|edit", "attendanceDorm", DormGender.BoysOnlyKey);
        var (teacherId, userId, client) = await TeacherAsync();
        using var teacher = client;
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(boss, teacherId, role.Id)).StatusCode);

        var me = (await teacher.GetFromJsonAsync<TeacherProfileDto>("/api/teacher/me"))!;
        Assert.Equal(["attendanceDorm", DormGender.BoysOnlyKey], me.RolePermissions);

        var tag = Guid.NewGuid().ToString("N")[..6];
        Student Pupil(string gender) => new()
        {
            FullName = $"{gender} {tag}", LastName = gender, FirstName = tag, BirthDate = "2010-01-01",
            Gender = gender, ClassName = "10-K", EnrollmentDate = "2025-09-01",
        };
        var boy = Pupil("male");
        var girl = Pupil("female");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.AddRange(boy, girl);
            db.BoardingRosters.AddRange(new[] { boy, girl }.Select(s => new BoardingRosterEntry
            {
                Session = BoardingSession.Dorm, StudentId = s.Id, AddedOn = AppClock.Today.AddDays(-1),
                AddedBy = userId, CreatedAt = AppClock.NowInstant,
            }));
            await db.SaveChangesAsync();
        });

        var day = (await teacher.GetFromJsonAsync<BoardingDayDto>($"{BoardingUrl}?date={Today}&session=dorm"))!;
        var students = day.Sections.SelectMany(s => s.Students).ToList();
        Assert.Contains(students, s => s.StudentId == boy.Id);
        Assert.DoesNotContain(students, s => s.StudentId == girl.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PutAsJsonAsync(BoardingUrl, new SaveBoardingRequest(Today,
            BoardingSession.Dorm, [new BoardingMarkInput(girl.Id, BoardingStatus.Present)]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.PutAsJsonAsync(BoardingUrl, new SaveBoardingRequest(Today,
            BoardingSession.Dorm, [new BoardingMarkInput(boy.Id, BoardingStatus.Present)]))).StatusCode);
    }

    [Fact]
    public async Task Oqituvchiga_faqat_davomat_roli_va_faqat_rol_beruvchi_beradi()
    {
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var wide = await CreateRoleAsync(boss, "attendanceDorm", "students");
        var narrow = await CreateRoleAsync(boss, "attendanceEvening:view");
        var (teacherId, _, client) = await TeacherAsync();
        using var teacher = client;

        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(boss, teacherId, wide.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(boss, teacherId, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AssignAsync(boss, "no-such-teacher", narrow.Id)).StatusCode);

        // Staff with the teachers list but without the roles section, and the teacher itself, cannot;
        // the admin can (2026-10-12: whoever has the roles section assigns roles — RoleAssignmentTests).
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var hr = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers");
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(hr, teacherId, narrow.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(teacher, teacherId, narrow.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(admin, teacherId, narrow.Id)).StatusCode);

        // A role saved from Boshqaruv → Rollar carries page grants next to its keys — it still fits.
        var paged = await CreateRoleAsync(boss, "/admin/attendance/boarding?session=dorm|edit", "attendanceDorm");
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(boss, teacherId, paged.Id)).StatusCode);
        var me = (await teacher.GetFromJsonAsync<TeacherProfileDto>("/api/teacher/me"))!;
        Assert.Equal(["attendanceDorm"], me.RolePermissions);

        // A view-only key fits: the teacher reads the evening roll call but cannot mark it.
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(boss, teacherId, narrow.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DayAsync(teacher, BoardingSession.Evening)).StatusCode);
        var write = await teacher.PutAsJsonAsync(BoardingUrl, new SaveBoardingRequest(Today, BoardingSession.Evening,
            [new BoardingMarkInput("x", BoardingStatus.Present)]));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Rol_ozgarsa_oqituvchiga_otadi_lekin_boshqa_bolimlar_ochilmaydi()
    {
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var role = await CreateRoleAsync(boss, "attendanceDorm");
        var (teacherId, _, client) = await TeacherAsync();
        using var teacher = client;
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(boss, teacherId, role.Id)).StatusCode);

        // The superadmin later widens the role: the evening key reaches the teacher at once, the rest never.
        var updated = await boss.PutAsJsonAsync($"{RolesUrl}/{role.Id}",
            new AccessRolePayload(role.Name, null, ["attendanceDorm", "attendanceEvening", "students", "finance"]));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(1, (await updated.Content.ReadFromJsonAsync<AccessRoleDto>())!.StaffCount);

        Assert.Equal(HttpStatusCode.OK, (await DayAsync(teacher, BoardingSession.Evening)).StatusCode);
        var me = (await teacher.GetFromJsonAsync<TeacherProfileDto>("/api/teacher/me"))!;
        Assert.Equal(["attendanceDorm", "attendanceEvening"], me.RolePermissions);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync("/api/admin/students", new { fullName = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/admin/finance/debtors")).StatusCode);

        // A role with a teacher member cannot be deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await boss.DeleteAsync($"{RolesUrl}/{role.Id}")).StatusCode);
    }
}
