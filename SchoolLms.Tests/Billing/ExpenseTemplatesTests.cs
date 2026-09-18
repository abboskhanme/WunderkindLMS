using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  Rejalashtirilgan chiqim shabloni — F6.01
//  (docs/modules/finance-parity.md §2.6.3: `/api/admin/finance/expense-templates`).
// ===========================================================================
//
//  NIMA TEKSHIRILADI
//  ------------------
//  1. Asosiy oqim: yaratish, ro'yxat, audit.
//  2. Tekshiruvlar: noma'lum toifa, manfiy/nol summa, kun oralig'idan
//     tashqari — hammasi 400, aniq kod bilan.
//  3. Qisman tahrir (PUT): faqat berilgan maydon o'zgaradi.
//  4. O'chirish — HAQIQIY DELETE (moliyaviy emas, `expense_templates_guards.sql`).
//  5. RBAC: faqat admin/direktor. Kassir, o'qituvchi, xodim — 403; token'siz — 401.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ExpenseTemplatesTests(ApiFixture fixture)
{
    private const string Templates = "/api/admin/finance/expense-templates";

    private sealed record ErrorBody(string Code, string Message);

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // =====================================================================
    //  1. Asosiy oqim
    // =====================================================================

    [Fact]
    public async Task Yaratish_royxatda_va_auditda_korinadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = $"Internet {Tag()}";

        var response = await admin.PostAsJsonAsync(Templates, new
        {
            name, categoryCode = "utilities", amount = 350_000m, dayOfMonth = 5, isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = (await response.Content.ReadFromJsonAsync<ExpenseTemplateDto>())!;
        Assert.Equal(name, created.Name);
        Assert.Equal("utilities", created.CategoryCode);
        Assert.Equal("Kommunal", created.CategoryName);
        Assert.Equal(350_000m, created.Amount);
        Assert.Equal(5, created.DayOfMonth);
        Assert.True(created.IsActive);

        var list = await admin.GetFromJsonAsync<List<ExpenseTemplateDto>>(Templates);
        Assert.Contains(list!, t => t.Id == created.Id);

        await fixture.Api.WithDbAsync(async db =>
        {
            var log = await db.AuditLogs.AsNoTracking()
                .Where(l => l.EntityType == "ExpenseTemplate" && l.EntityId == created.Id.ToString("D"))
                .SingleAsync();
            Assert.Equal("create", log.Action);
            Assert.Contains(name, log.Summary, StringComparison.Ordinal);
        });
    }

    // =====================================================================
    //  2. Tekshiruvlar
    // =====================================================================

    [Theory]
    [InlineData("not-a-real-category", 100_000, 5, "invalid_category")]
    [InlineData("utilities", 0, 5, "invalid_amount")]
    [InlineData("utilities", -100, 5, "invalid_amount")]
    [InlineData("utilities", 100_000, 0, "invalid_day_of_month")]
    [InlineData("utilities", 100_000, 29, "invalid_day_of_month")]
    public async Task Notogri_qiymatlar_rad_etiladi(
        string category, decimal amount, int dayOfMonth, string expectedCode)
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Templates, new
        {
            name = $"Shablon {Tag()}", categoryCode = category, amount, dayOfMonth, isActive = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Bosh_nom_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Templates, new
        {
            name = "   ", categoryCode = "utilities", amount = 100_000m, dayOfMonth = 5, isActive = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("name_required", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  3. Qisman tahrir
    // =====================================================================

    [Fact]
    public async Task Qisman_tahrir_faqat_berilgan_maydonlarni_ozgartiradi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = $"Ijara {Tag()}";
        var created = await CreateAsync(admin, name, "rent", 4_500_000m, 10, true);

        // Faqat summa berilgan — qolgani o'z holicha qolishi kerak.
        var response = await admin.PutAsJsonAsync($"{Templates}/{created.Id}", new { amount = 4_700_000m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<ExpenseTemplateDto>())!;
        Assert.Equal(name, updated.Name);
        Assert.Equal("rent", updated.CategoryCode);
        Assert.Equal(4_700_000m, updated.Amount);
        Assert.Equal(10, updated.DayOfMonth);
        Assert.True(updated.IsActive);

        // Faqat faollikni o'chirish — summa TEGILMAYDI.
        var deactivated = await admin.PutAsJsonAsync($"{Templates}/{created.Id}", new { isActive = false });
        var afterDeactivate = (await deactivated.Content.ReadFromJsonAsync<ExpenseTemplateDto>())!;
        Assert.False(afterDeactivate.IsActive);
        Assert.Equal(4_700_000m, afterDeactivate.Amount);
    }

    [Fact]
    public async Task Mavjud_bolmagan_shablonni_tahrirlash_404()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PutAsJsonAsync($"{Templates}/{Guid.NewGuid()}", new { amount = 1000m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("template_not_found", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  4. O'chirish — haqiqiy DELETE
    // =====================================================================

    [Fact]
    public async Task Ochirish_royxatdan_haqiqatan_olib_tashlaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var created = await CreateAsync(admin, $"O'chiriladigan {Tag()}", "other", 50_000m, 15, true);

        var deleteResponse = await admin.DeleteAsync($"{Templates}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var list = await admin.GetFromJsonAsync<List<ExpenseTemplateDto>>(Templates);
        Assert.DoesNotContain(list!, t => t.Id == created.Id);

        // Ikkinchi o'chirish — endi yo'q, 404.
        var second = await admin.DeleteAsync($"{Templates}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var log = await db.AuditLogs.AsNoTracking()
                .Where(l => l.EntityType == "ExpenseTemplate" && l.EntityId == created.Id.ToString("D")
                            && l.Action == "delete")
                .SingleOrDefaultAsync();
            Assert.NotNull(log);
        });
    }

    // =====================================================================
    //  5. RBAC
    // =====================================================================

    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Ruxsatsiz_rol_hamma_endpointda_403(string role)
    {
        var client = await fixture.Api.ClientAsAsync(role);
        var randomId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Templates)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Templates, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"{Templates}/{randomId}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"{Templates}/{randomId}")).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Templates)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Templates, new { })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_royxatni_kora_va_yoza_oladi(string role)
    {
        var client = await fixture.Api.ClientAsAsync(role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Templates)).StatusCode);

        var response = await client.PostAsJsonAsync(Templates, new
        {
            name = $"Shablon {Tag()}", categoryCode = "supplies", amount = 200_000m, dayOfMonth = 3, isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static async Task<ExpenseTemplateDto> CreateAsync(
        HttpClient client, string name, string category, decimal amount, int dayOfMonth, bool isActive)
    {
        var response = await client.PostAsJsonAsync(Templates, new
        {
            name, categoryCode = category, amount, dayOfMonth, isActive,
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExpenseTemplateDto>())!;
    }
}
