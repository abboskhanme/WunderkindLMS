using System.Net;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Bosh sahifa ham ruxsat bilan (mijoz, 2026-09-24): "bosh sahifa ham role uchun chiqishi belgilanishi kerak,
/// bazilarga u sahifa uchun ham dostup bo'lmaydi". Boshqa admin bo'limlaridan farqli ravishda bu yerda
/// O'QISH ham ruxsatga bog'liq (<c>GatedRead</c>).
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DashboardPermissionTests(ApiFixture fixture)
{
    private const string Dashboard = "/api/admin/dashboard";

    [Fact]
    public async Task Ruxsatsiz_xodim_bosh_sahifani_ocholmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        var response = await client.GetAsync(Dashboard);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_ruxsati_bor_xodim_bosh_sahifani_ochadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "dashboard");
        var response = await client.GetAsync(Dashboard);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_superadmin_uchun_cheklov_yoq(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var response = await client.GetAsync(Dashboard);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
