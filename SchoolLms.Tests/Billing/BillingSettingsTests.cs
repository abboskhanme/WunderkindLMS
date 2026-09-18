using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Moliya sozlamalari (docs/modules/finance-parity.md §2.14, F14.01) —
/// to'lov muddati, muddati o'tgan kun va chiqim tasdiq chegarasi.
///
/// <para>
/// <b>Baza — umumiy, qator YAGONA.</b> <c>billing_settings</c> butun test
/// bazasi uchun bitta qator (<c>BillingSettings.SingletonId</c>) va
/// <c>ExpenseService</c> uni har chiqim yaratilganda o'qiydi
/// (<c>ExpensesTests.Chegara_sozlamadan_oqiladi</c>). Shuning uchun har bir
/// yozuvchi test qiymatlarni <c>try/finally</c> bilan MAJBURAN qaytaradi —
/// aks holda parallel yugurgan boshqa test klassining chiqim chegarasi
/// kutilmagan qiymatga o'zgarib qolardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BillingSettingsTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/billing/settings";

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. Ruxsat — SPEC §4.3: admin va direktor, boshqa hech kim
    // =====================================================================

    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Moliyaviy_bolmagan_rol_sozlamani_oqiy_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_finance_ruxsati_bilan_ham_sozlamani_ozgartira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "finance");

        var response = await client.PutAsJsonAsync(
            Url, new UpdateBillingSettingsRequest(10, 15, 5_000_000m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_sozlamani_oqiy_oladi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        var response = await client.GetAsync(Url);

        Assert.True(response.IsSuccessStatusCode,
            $"{role} → {(int)response.StatusCode} {response.StatusCode}");
    }

    // =====================================================================
    //  2. To'liq oqim — o'qish, saqlash, audit
    // =====================================================================

    /// <summary>
    /// Sozlamani o'zgartiradi, javobni va bazani tekshiradi, keyin
    /// MAJBURAN asl holiga qaytaradi — yagona qator butun test bazasiga umumiy.
    /// </summary>
    [Fact]
    public async Task Saqlash_bazani_ozgartiradi_va_audit_yozadi()
    {
        var (director, client) = await ActorAsync(Roles.SuperAdmin);
        using var _client = client;

        await using var db = NewDb();
        var before = await db.BillingSettings.AsNoTracking()
            .FirstAsync(s => s.Id == SchoolLms.Domain.BillingSettings.SingletonId);
        var original = (before.PaymentDueDay, before.OverdueAfterDay, before.ExpenseApprovalThreshold);
        var auditBefore = await db.AuditLogs.CountAsync(a => a.EntityType == "BillingSettings");

        try
        {
            var response = await client.PutAsJsonAsync(
                Url, new UpdateBillingSettingsRequest(12, 20, 7_500_000m));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<BillingSettingsDto>();
            Assert.NotNull(dto);
            Assert.Equal(12, dto!.PaymentDueDay);
            Assert.Equal(20, dto.OverdueAfterDay);
            Assert.Equal(7_500_000m, dto.ExpenseApprovalThreshold);
            Assert.Equal(director.FullName, dto.UpdatedByName);

            await using var verify = NewDb();
            var saved = await verify.BillingSettings.AsNoTracking()
                .FirstAsync(s => s.Id == SchoolLms.Domain.BillingSettings.SingletonId);
            Assert.Equal(12, saved.PaymentDueDay);
            Assert.Equal(20, saved.OverdueAfterDay);
            Assert.Equal(7_500_000m, saved.ExpenseApprovalThreshold);
            Assert.Equal(director.Id, saved.UpdatedBy);

            var auditAfter = await verify.AuditLogs.CountAsync(a => a.EntityType == "BillingSettings");
            Assert.Equal(auditBefore + 1, auditAfter);
        }
        finally
        {
            await using var restore = NewDb();
            var settings = await restore.BillingSettings
                .FirstAsync(s => s.Id == SchoolLms.Domain.BillingSettings.SingletonId);
            settings.PaymentDueDay = original.PaymentDueDay;
            settings.OverdueAfterDay = original.OverdueAfterDay;
            settings.ExpenseApprovalThreshold = original.ExpenseApprovalThreshold;
            await restore.SaveChangesAsync();
        }
    }

    // =====================================================================
    //  3. Tekshiruv — noto'g'ri qiymat 400, baza tegilmaydi
    // =====================================================================

    [Theory]
    [InlineData(0, 15, 5_000_000, "invalid_payment_due_day")]     // kun < 1
    [InlineData(29, 29, 5_000_000, "invalid_payment_due_day")]    // kun > 28
    [InlineData(15, 10, 5_000_000, "invalid_overdue_after_day")]  // muddat < to'lov kuni
    [InlineData(10, 29, 5_000_000, "invalid_overdue_after_day")]  // muddat > 28
    [InlineData(10, 15, -1, "invalid_expense_threshold")]         // manfiy chegara
    public async Task Notogri_qiymat_400_va_bazani_ozgartirmaydi(
        int paymentDueDay, int overdueAfterDay, decimal threshold, string expectedCode)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);

        await using var db = NewDb();
        var before = await db.BillingSettings.AsNoTracking()
            .FirstAsync(s => s.Id == SchoolLms.Domain.BillingSettings.SingletonId);

        var response = await client.PutAsJsonAsync(
            Url, new UpdateBillingSettingsRequest(paymentDueDay, overdueAfterDay, threshold));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<BillingErrorDto>();
        Assert.Equal(expectedCode, error?.Code);

        await using var verify = NewDb();
        var after = await verify.BillingSettings.AsNoTracking()
            .FirstAsync(s => s.Id == SchoolLms.Domain.BillingSettings.SingletonId);
        Assert.Equal(before.PaymentDueDay, after.PaymentDueDay);
        Assert.Equal(before.OverdueAfterDay, after.OverdueAfterDay);
        Assert.Equal(before.ExpenseApprovalThreshold, after.ExpenseApprovalThreshold);
    }

    private async Task<(AppUser User, HttpClient Client)> ActorAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }
}
