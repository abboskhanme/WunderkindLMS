using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Boshqaruv → Rollar: permissions are granted to a role, staff are only assigned one.
/// Covers RBAC on the role endpoints, the copy of role permissions onto members
/// (assign, edit, unassign) and that the copy is what the request pipeline enforces.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AccessRolesTests(ApiFixture fixture)
{
    private const string RolesUrl = "/api/admin/roles";
    private const string StaffUrl = "/api/admin/staff";

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Faqat_superadmin_rol_yarata_oladi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "staff");

        var res = await client.PostAsJsonAsync(RolesUrl,
            new AccessRolePayload(Unique("Rol"), null, ["students"]));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rollar_royxatni_ham_korolmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(RolesUrl)).StatusCode);
    }

    [Fact]
    public async Task Rol_ruxsatlari_xodimga_otadi_va_ozgarishi_darrov_amal_qiladi()
    {
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);

        var (member, _) = await fixture.Api.SeedUserAsync(Roles.Staff);
        using var memberClient = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Staff, member.Id, member.FullName, member.Email));

        // Without a role the member cannot write to the staff section.
        var denied = await memberClient.PostAsJsonAsync(StaffUrl, new StaffPayload("X Y", ""));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var created = await boss.PostAsJsonAsync(RolesUrl,
            new AccessRolePayload(Unique("Kadrlar"), "Xodimlar bilan ishlaydi", ["staff", "staff", " students "]));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var role = (await created.Content.ReadFromJsonAsync<AccessRoleDto>())!;
        Assert.Equal(["staff", "students"], role.Permissions);

        var assigned = await boss.PutAsJsonAsync($"{StaffUrl}/{member.Id}/role", new SetStaffRoleRequest(role.Id));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var dto = (await assigned.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Equal(role.Id, dto.AccessRoleId);
        Assert.Equal(role.Name, dto.AccessRoleName);
        Assert.Equal(["staff", "students"], dto.Permissions);

        // The role's grant is enforced for the member — no re-login needed.
        var allowed = await memberClient.PostAsJsonAsync(StaffUrl, new StaffPayload("Yangi Xodim", ""));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        var list = await boss.GetFromJsonAsync<List<AccessRoleDto>>(RolesUrl);
        Assert.Equal(1, list!.Single(r => r.Id == role.Id).StaffCount);

        // Editing the role rewrites the member's permissions.
        var edited = await boss.PutAsJsonAsync($"{RolesUrl}/{role.Id}",
            new AccessRolePayload(role.Name, role.Description, ["students"]));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var again = await memberClient.PostAsJsonAsync(StaffUrl, new StaffPayload("Yana Xodim", ""));
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);

        // A role with members cannot be deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await boss.DeleteAsync($"{RolesUrl}/{role.Id}")).StatusCode);

        // Removing the role removes its permissions, then the role can go.
        var unassigned = await boss.PutAsJsonAsync($"{StaffUrl}/{member.Id}/role", new SetStaffRoleRequest(null));
        var cleared = (await unassigned.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Null(cleared.AccessRoleId);
        Assert.Empty(cleared.Permissions);

        Assert.Equal(HttpStatusCode.NoContent, (await boss.DeleteAsync($"{RolesUrl}/{role.Id}")).StatusCode);
    }

    [Fact]
    public async Task Rol_nomi_takrorlanmaydi()
    {
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var name = Unique("Buxgalter");

        Assert.Equal(HttpStatusCode.OK,
            (await boss.PostAsJsonAsync(RolesUrl, new AccessRolePayload(name, null, ["finance"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await boss.PostAsJsonAsync(RolesUrl, new AccessRolePayload(name, null, []))).StatusCode);
    }

    [Fact]
    public async Task Admin_xodimga_rol_biriktira_olmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (member, _) = await fixture.Api.SeedUserAsync(Roles.Staff);

        var res = await admin.PutAsJsonAsync($"{StaffUrl}/{member.Id}/role", new SetStaffRoleRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
