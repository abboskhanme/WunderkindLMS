using System.Net;
using System.Net.Http.Json;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Moliya xodim roli orqali (Boshqaruv → Rollar, 2026-09-25): rolida "finance"
/// ruxsati bor xodim moliyada admin darajasida ishlaydi; ruxsatsiz xodim — yo'q.
/// Direktor tasdiqlari (chegirmani tasdiqlash) bunga kirmaydi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class FinanceByRoleTests(ApiFixture fixture)
{
    private const string Billing = "/api/admin/billing";

    [Fact]
    public async Task Moliya_ruxsatli_xodim_moliyani_oqiydi_va_yozadi()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "finance");

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Billing}/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Billing}/subscriptions")).StatusCode);

        var code = $"r{Guid.NewGuid():N}"[..12];
        var created = await staff.PostAsJsonAsync($"{Billing}/categories",
            new { code, name = $"Rol {code}", isActive = true });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    [Fact]
    public async Task Moliya_ruxsatisiz_xodim_kira_olmaydi()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"{Billing}/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync($"{Billing}/categories", new { code = "x", name = "X", isActive = true })).StatusCode);
    }

    [Fact]
    public async Task Moliya_ruxsatli_xodim_chegirmani_tasdiqlay_olmaydi()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "finance");

        var res = await staff.PostAsync($"{Billing}/discounts/{Guid.NewGuid()}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
