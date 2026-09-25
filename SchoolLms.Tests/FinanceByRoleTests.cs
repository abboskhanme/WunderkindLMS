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

    /// <summary>
    /// Moliya menyusidagi har bir ekran o'qiydigan API — "finance" ruxsatli xodimga
    /// ochiq (403/401 emas). Ilgari menyu ochilsa ham ma'lumot kelmasdi (2026-09-25).
    /// </summary>
    [Theory]
    [InlineData("/api/admin/finance/salary-report")]
    [InlineData("/api/admin/finance/transactions")]
    [InlineData("/api/admin/billing/invoices")]
    [InlineData("/api/admin/finance/pnl")]
    [InlineData("/api/admin/finance/pnl/expectation")]
    [InlineData("/api/admin/finance/pnl/matrix")]
    [InlineData("/api/admin/finance/cashflow")]
    [InlineData("/api/admin/finance/cashflow/statement")]
    [InlineData("/api/admin/finance/dashboard")]
    [InlineData("/api/admin/finance/debtors")]
    [InlineData("/api/admin/finance/debtors/workflow")]
    [InlineData("/api/admin/finance/debtor-statuses")]
    [InlineData("/api/admin/finance/collection-rate")]
    [InlineData("/api/admin/finance/arrears-pivot")]
    [InlineData("/api/admin/finance/cash-day")]
    [InlineData("/api/admin/finance/cash-day/calendar")]
    [InlineData("/api/admin/finance/money-flow")]
    [InlineData("/api/admin/expenses")]
    [InlineData("/api/admin/billing/discounts")]
    [InlineData("/api/admin/billing/settings")]
    public async Task Moliya_ekranlari_malumoti_moliya_ruxsatli_xodimga_ochiq(string url)
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "finance");

        var status = (await staff.GetAsync(url)).StatusCode;

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.True((int)status < 500, $"{url} → {(int)status}");
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
