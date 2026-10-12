using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Boshqaruv → Xodimlar lists the owners (superadmin) too (client, 2026-10-11: "super admin bo'lsa ham
/// umumiy xodimlar ro'yxatida bo'lishi kerak"). An owner's row is opened and changed only by a superadmin —
/// a "staff" grant must never read or reset the owner's password — and it is never deleted or given a role.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StaffOwnerRowsTests(ApiFixture fixture)
{
    private const string StaffUrl = "/api/admin/staff";

    [Fact]
    public async Task Ega_royxatda_belgisi_bilan_chiqadi()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.SuperAdmin);
        var (member, _) = await fixture.Api.SeedUserAsync(Roles.Staff);
        using var hr = await fixture.Api.ClientAsAsync(Roles.Staff, "staff");

        var list = (await hr.GetFromJsonAsync<List<StaffDto>>(StaffUrl))!;

        var row = Assert.Single(list, s => s.Id == owner.Id);
        Assert.True(row.IsOwner);
        Assert.Equal(owner.Email, row.Login);
        Assert.False(Assert.Single(list, s => s.Id == member.Id).IsOwner);
    }

    [Fact]
    public async Task Staff_ruxsati_ega_akkauntiga_tega_olmaydi()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.SuperAdmin);
        using var hr = await fixture.Api.ClientAsAsync(Roles.Staff, "staff");

        Assert.Equal(HttpStatusCode.Forbidden, (await hr.GetAsync($"{StaffUrl}/{owner.Id}/credentials")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.PostAsync($"{StaffUrl}/{owner.Id}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await hr.PutAsJsonAsync($"{StaffUrl}/{owner.Id}", new StaffPayload("Boshqa Ism", "", NewPassword: "Egallab-olaman1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.DeleteAsync($"{StaffUrl}/{owner.Id}")).StatusCode);

        // Admin is not an owner either.
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"{StaffUrl}/{owner.Id}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task Superadmin_egani_tahrirlaydi_lekin_ochirmaydi_va_rol_bermaydi()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.SuperAdmin);
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);

        var creds = await boss.GetFromJsonAsync<CredentialsDto>($"{StaffUrl}/{owner.Id}/credentials");
        Assert.Equal(owner.Email, creds!.Login);

        var saved = await boss.PutAsJsonAsync($"{StaffUrl}/{owner.Id}", new StaffPayload("Ega Ismi", "Direktor", Phone: "+998 90 000 00 00"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = (await saved.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Equal("Direktor", dto.Position);
        Assert.True(dto.IsOwner);

        Assert.Equal(HttpStatusCode.Conflict, (await boss.DeleteAsync($"{StaffUrl}/{owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await boss.PutAsJsonAsync($"{StaffUrl}/{owner.Id}/role", new SetStaffRoleRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await boss.PutAsJsonAsync($"{StaffUrl}/{owner.Id}/permissions", new SetStaffPermissionsRequest(["staff"]))).StatusCode);

        // Still an owner after all of it.
        var list = (await boss.GetFromJsonAsync<List<StaffDto>>(StaffUrl))!;
        Assert.True(Assert.Single(list, s => s.Id == owner.Id).IsOwner);
    }
}
