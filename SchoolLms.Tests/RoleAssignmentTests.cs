using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Client, 2026-10-12: "rolelarga dostupi bor odam ham role biriktira olsin". A staff member with the
/// Rollar page at the full level assigns roles to staff and teachers — never their own, and never more
/// than they hold themselves (no escalation through a colleague or a second account).
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RoleAssignmentTests(ApiFixture fixture)
{
    private const string RolesUrl = "/api/admin/roles";
    private const string StaffUrl = "/api/admin/staff";
    private const string TeachersUrl = "/api/admin/teachers";

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];

    private async Task<AccessRoleDto> CreateRoleAsync(params string[] permissions)
    {
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var res = await boss.PostAsJsonAsync(RolesUrl, new AccessRolePayload(Unique("Rol"), null, [.. permissions]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AccessRoleDto>())!;
    }

    /// <summary>A staff account with these permissions and a client signed in as it.</summary>
    private async Task<(AppUser User, HttpClient Client)> StaffAsync(params string[] permissions)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: permissions);
        return (user, fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Staff, user.Id, user.FullName, user.Email)));
    }

    private static Task<HttpResponseMessage> AssignStaffAsync(HttpClient client, string userId, Guid? roleId) =>
        client.PutAsJsonAsync($"{StaffUrl}/{userId}/role", new SetStaffRoleRequest(roleId));

    [Fact]
    public async Task Rollar_bolimi_bor_xodim_ozidagi_huquq_doirasida_rol_beradi()
    {
        var narrow = await CreateRoleAsync("students");
        var wider = await CreateRoleAsync("students", "finance");
        var (manager, managerClient) = await StaffAsync("staff", "students");
        using var client = managerClient;
        var (member, _) = await fixture.Api.SeedUserAsync(Roles.Staff);

        var res = await AssignStaffAsync(client, member.Id, narrow.Id);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(["students"], (await res.Content.ReadFromJsonAsync<StaffDto>())!.Permissions);

        // Finance the manager does not hold — refused, the member keeps the old role.
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignStaffAsync(client, member.Id, wider.Id)).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(narrow.Id, (await db.Users.SingleAsync(u => u.Id == member.Id)).AccessRoleId));

        // Their own role — never, not even a narrower one.
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignStaffAsync(client, manager.Id, narrow.Id)).StatusCode);

        // Removing the role works.
        Assert.Equal(HttpStatusCode.OK, (await AssignStaffAsync(client, member.Id, null)).StatusCode);
    }

    [Fact]
    public async Task Ozidan_keng_huquqli_xodimga_tegolmaydi()
    {
        var narrow = await CreateRoleAsync("students");
        var (_, managerClient) = await StaffAsync("staff", "students");
        using var client = managerClient;
        var (boss, _) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: ["finance", "students"]);

        // Changing or removing the role of someone above them would also be a way to hurt or reshape access.
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignStaffAsync(client, boss.Id, narrow.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignStaffAsync(client, boss.Id, null)).StatusCode);
    }

    [Fact]
    public async Task Rollar_sahifasi_faqat_korish_bolsa_yoki_umuman_bolmasa_rad_etiladi()
    {
        var narrow = await CreateRoleAsync("students");
        var (member, _) = await fixture.Api.SeedUserAsync(Roles.Staff);

        // Page grants: Xodimlar full, Rollar view only.
        var (_, viewer) = await StaffAsync(
            "/admin/boshqaruv/staff|edit", "/admin/boshqaruv/roles|view", "staff", "teachers", "students");
        using (viewer)
            Assert.Equal(HttpStatusCode.Forbidden, (await AssignStaffAsync(viewer, member.Id, narrow.Id)).StatusCode);

        // A view-only legacy key does not open writes at all.
        var (_, reader) = await StaffAsync("staff:view", "students");
        using (reader)
            Assert.Equal(HttpStatusCode.Forbidden, (await AssignStaffAsync(reader, member.Id, narrow.Id)).StatusCode);

        // The same page grants with Rollar at the full level — allowed.
        var (_, editor) = await StaffAsync(
            "/admin/boshqaruv/staff|edit", "/admin/boshqaruv/roles|edit", "staff", "teachers", "students");
        using (editor)
            Assert.Equal(HttpStatusCode.OK, (await AssignStaffAsync(editor, member.Id, narrow.Id)).StatusCode);
    }

    [Fact]
    public async Task Oqituvchiga_komendant_rolini_shu_huquqi_bor_xodim_beradi()
    {
        var komendant = await CreateRoleAsync("attendanceDorm");
        var (teacherUser, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
            teacherId = (await db.Teachers.SingleAsync(t => t.UserId == teacherUser.Id)).Id);

        // Without the dorm roll call themselves — refused.
        var (_, plain) = await StaffAsync("staff", "teachers");
        using (plain)
        {
            var res = await plain.PutAsJsonAsync($"{TeachersUrl}/{teacherId}/role", new SetStaffRoleRequest(komendant.Id));
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        var (_, manager) = await StaffAsync("staff", "teachers", "attendanceDorm");
        using (manager)
        {
            var res = await manager.PutAsJsonAsync($"{TeachersUrl}/{teacherId}/role", new SetStaffRoleRequest(komendant.Id));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(komendant.Name, (await res.Content.ReadFromJsonAsync<TeacherRoleDto>())!.AccessRoleName);
        }
    }
}
