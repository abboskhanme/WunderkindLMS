using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  Tranzaksiya turi katalogi (Kirim/Chiqim) — mijoz yuborgan EduSchool kassa
//  kirim shakli va moliya sozlamalari ekrani, 2026-09-18.
//  `/api/admin/finance/transaction-types`.
// ===========================================================================
//
//  NIMA TEKSHIRILADI
//  ------------------
//  1. Asosiy oqim: yaratish, ro'yxat (kind filtri bilan), audit.
//  2. Tekshiruvlar: noma'lum kind, bo'sh nom, (kind, name) takrori — hammasi 400.
//  3. Qisman tahrir (PUT): faqat berilgan maydon o'zgaradi, `kind` YO'QOTILADI
//     (so'rov shaklida umuman yo'q — jo'natilsa ham e'tiborsiz qoldiriladi).
//  4. O'chirish: HAQIQIY DELETE — lekin SEED qilingan yoki ISHLATILGAN
//     turga RAD ETILADI (409).
//  5. Kirimga bog'lash: `CashBoxService.PayInAsync` (`CashBoxesController`,
//     boshqa fayl) turni qanday ISHLATISHINI — mos, mos kelmagan, faolsiz,
//     mavjud bo'lmagan va umuman ko'rsatilmagan holatlarni tekshiradi.
//  6. RBAC: faqat admin/direktor. Kassir, o'qituvchi, xodim — 403; token'siz — 401.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class TransactionTypesTests(ApiFixture fixture)
{
    private const string Types = "/api/admin/finance/transaction-types";
    private const string Boxes = "/api/admin/cash-boxes";

    private sealed record ErrorBody(string Code, string Message);

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // =====================================================================
    //  1. Asosiy oqim
    // =====================================================================

    [Fact]
    public async Task Yaratish_royxatda_va_auditda_korinadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = $"Sport to'plami {Tag()}";

        var response = await admin.PostAsJsonAsync(Types, new { kind = "in", name, isActive = true, position = 7 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = (await response.Content.ReadFromJsonAsync<TransactionTypeDto>())!;
        Assert.Equal("in", created.Kind);
        Assert.Equal(name, created.Name);
        Assert.True(created.IsActive);
        Assert.False(created.IsSeeded);
        Assert.Equal(7, created.Position);

        var list = await admin.GetFromJsonAsync<List<TransactionTypeDto>>(Types);
        Assert.Contains(list!, t => t.Id == created.Id);

        await fixture.Api.WithDbAsync(async db =>
        {
            var log = await db.AuditLogs.AsNoTracking()
                .Where(l => l.EntityType == "TransactionType" && l.EntityId == created.Id.ToString("D"))
                .SingleAsync();
            Assert.Equal("create", log.Action);
            Assert.Contains(name, log.Summary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Kind_filtri_faqat_soralgan_turkumni_qaytaradi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var inName = $"Kirim filtri {Tag()}";
        var outName = $"Chiqim filtri {Tag()}";
        await CreateAsync(admin, "in", inName);
        await CreateAsync(admin, "out", outName);

        var inList = await admin.GetFromJsonAsync<List<TransactionTypeDto>>($"{Types}?kind=in");
        Assert.Contains(inList!, t => t.Name == inName);
        Assert.DoesNotContain(inList!, t => t.Name == outName);
        Assert.All(inList!, t => Assert.Equal("in", t.Kind));

        var outList = await admin.GetFromJsonAsync<List<TransactionTypeDto>>($"{Types}?kind=out");
        Assert.Contains(outList!, t => t.Name == outName);
        Assert.DoesNotContain(outList!, t => t.Name == inName);
    }

    // =====================================================================
    //  2. Tekshiruvlar
    // =====================================================================

    [Fact]
    public async Task Notogri_kind_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Types, new { kind = "bonus", name = $"Turi {Tag()}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_kind", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Bosh_nom_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Types, new { kind = "in", name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("name_required", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Bitta_kindda_takroriy_nom_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = $"Takror {Tag()}";
        await CreateAsync(admin, "in", name);

        var response = await admin.PostAsJsonAsync(Types, new { kind = "in", name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("duplicate_name", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Boshqa kind'da AYNAN shu nom — muammo emas.
        var otherKind = await admin.PostAsJsonAsync(Types, new { kind = "out", name });
        Assert.Equal(HttpStatusCode.OK, otherKind.StatusCode);
    }

    // =====================================================================
    //  3. Qisman tahrir
    // =====================================================================

    [Fact]
    public async Task Qisman_tahrir_faqat_berilgan_maydonlarni_ozgartiradi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = $"Tahrir {Tag()}";
        var created = await CreateAsync(admin, "out", name, position: 2);

        var response = await admin.PutAsJsonAsync($"{Types}/{created.Id}", new { position = 9 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<TransactionTypeDto>())!;
        Assert.Equal(name, updated.Name);
        Assert.Equal("out", updated.Kind);
        Assert.Equal(9, updated.Position);
        Assert.True(updated.IsActive);

        var deactivated = await admin.PutAsJsonAsync($"{Types}/{created.Id}", new { isActive = false });
        var afterDeactivate = (await deactivated.Content.ReadFromJsonAsync<TransactionTypeDto>())!;
        Assert.False(afterDeactivate.IsActive);
        Assert.Equal(9, afterDeactivate.Position);
    }

    /// <summary>
    /// So'rov shaklida (<c>UpdateTransactionTypeRequest</c>) <c>kind</c> maydoni
    /// UMUMAN yo'q — yuborilsa ham System.Text.Json uni jimgina e'tiborsiz
    /// qoldiradi, tur o'z kindida qoladi (`TransactionTypeService.cs` izohi).
    /// </summary>
    [Fact]
    public async Task Kind_tahrirda_ozgarmaydi_hatto_sorovda_yuborilsa_ham()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var created = await CreateAsync(admin, "in", $"Kind qotgan {Tag()}");

        var response = await admin.PutAsJsonAsync($"{Types}/{created.Id}", new { kind = "out", name = "Yangi nom" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<TransactionTypeDto>())!;
        Assert.Equal("in", updated.Kind);
        Assert.Equal("Yangi nom", updated.Name);
    }

    [Fact]
    public async Task Mavjud_bolmagan_turni_tahrirlash_404()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PutAsJsonAsync($"{Types}/{Guid.NewGuid()}", new { name = "Yo'q" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("transaction_type_not_found", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  4. O'chirish — seed va ishlatilgan turga RAD ETILADI
    // =====================================================================

    [Fact]
    public async Task Ochirish_royxatdan_haqiqatan_olib_tashlaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var created = await CreateAsync(admin, "out", $"O'chiriladigan {Tag()}");

        var deleteResponse = await admin.DeleteAsync($"{Types}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var list = await admin.GetFromJsonAsync<List<TransactionTypeDto>>(Types);
        Assert.DoesNotContain(list!, t => t.Id == created.Id);

        var second = await admin.DeleteAsync($"{Types}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Seed_qilingan_turni_ochirib_bolmaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var seeded = (await admin.GetFromJsonAsync<List<TransactionTypeDto>>($"{Types}?kind=in"))!
            .First(t => t.IsSeeded);

        var response = await admin.DeleteAsync($"{Types}/{seeded.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("seeded_type_protected", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Lekin nomini o'zgartirish MUMKIN — mijoz skrinshotidagi xatti-harakat.
        var rename = await admin.PutAsJsonAsync($"{Types}/{seeded.Id}", new { name = seeded.Name });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task Kassa_tranzaksiyasida_ishlatilgan_turni_ochirib_bolmaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var type = await CreateAsync(admin, "in", $"Ishlatiladigan {Tag()}");

        var boxResponse = await admin.PostAsJsonAsync(Boxes, new { name = $"TT test kassa {Tag()}" });
        Assert.Equal(HttpStatusCode.OK, boxResponse.StatusCode);
        var box = (await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>())!;

        var payIn = await admin.PostAsJsonAsync($"{Boxes}/{box.Id}/in",
            new { amount = 50_000m, method = PaymentMethod.Cash, transactionTypeId = type.Id });
        Assert.Equal(HttpStatusCode.OK, payIn.StatusCode);

        var response = await admin.DeleteAsync($"{Types}/{type.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("transaction_type_in_use", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  5. Kirimga bog'lash — `CashBoxService.PayInAsync` (mijoz kassa kirim
    //     shaklidagi "Tranzaksiya turi" tanlovi)
    // =====================================================================

    [Fact]
    public async Task Kirimda_togri_turdagi_id_qabul_qilinadi_va_jurnalda_nomi_korinadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var type = await CreateAsync(admin, "in", $"Kirimga bog'langan {Tag()}");

        var boxResponse = await admin.PostAsJsonAsync(Boxes, new { name = $"PayIn wiring kassa {Tag()}" });
        var box = (await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>())!;

        var payIn = await admin.PostAsJsonAsync($"{Boxes}/{box.Id}/in",
            new { amount = 25_000m, method = PaymentMethod.Cash, transactionTypeId = type.Id });
        Assert.Equal(HttpStatusCode.OK, payIn.StatusCode);
        var row = (await payIn.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
        Assert.Equal(type.Name, row.TransactionTypeName);

        var page = (await (await admin.GetAsync($"{Boxes}/transactions?boxId={box.Id}"))
            .Content.ReadFromJsonAsync<CashBoxTransactionsPageDto>())!;
        Assert.Contains(page.Rows, r => r.Id == row.Id && r.TransactionTypeName == type.Name);
    }

    [Fact]
    public async Task Kirimda_transactionTypeIdsiz_ham_ishlaydi_orqaga_moslik()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var boxResponse = await admin.PostAsJsonAsync(Boxes, new { name = $"Turisiz kirim {Tag()}" });
        var box = (await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>())!;

        var payIn = await admin.PostAsJsonAsync($"{Boxes}/{box.Id}/in",
            new { amount = 10_000m, method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.OK, payIn.StatusCode);
        var row = (await payIn.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
        Assert.Null(row.TransactionTypeName);
    }

    [Fact]
    public async Task Kirimda_chiqim_turidagi_id_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var outType = await CreateAsync(admin, "out", $"Chiqim turi {Tag()}");
        var boxResponse = await admin.PostAsJsonAsync(Boxes, new { name = $"Mos kelmas tur kassa {Tag()}" });
        var box = (await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>())!;

        var payIn = await admin.PostAsJsonAsync($"{Boxes}/{box.Id}/in",
            new { amount = 10_000m, method = PaymentMethod.Cash, transactionTypeId = outType.Id });

        Assert.Equal(HttpStatusCode.BadRequest, payIn.StatusCode);
        Assert.Equal("transaction_type_kind_mismatch", (await payIn.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Kirimda_faolsiz_tur_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var type = await CreateAsync(admin, "in", $"Faolsiz {Tag()}");
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"{Types}/{type.Id}", new { isActive = false })).StatusCode);

        var boxResponse = await admin.PostAsJsonAsync(Boxes, new { name = $"Faolsiz tur kassa {Tag()}" });
        var box = (await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>())!;

        var payIn = await admin.PostAsJsonAsync($"{Boxes}/{box.Id}/in",
            new { amount = 10_000m, method = PaymentMethod.Cash, transactionTypeId = type.Id });

        Assert.Equal(HttpStatusCode.Conflict, payIn.StatusCode);
        Assert.Equal("transaction_type_inactive", (await payIn.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Kirimda_mavjud_bolmagan_tur_404()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var boxResponse = await admin.PostAsJsonAsync(Boxes, new { name = $"Yo'q tur kassa {Tag()}" });
        var box = (await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>())!;

        var payIn = await admin.PostAsJsonAsync($"{Boxes}/{box.Id}/in",
            new { amount = 10_000m, method = PaymentMethod.Cash, transactionTypeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, payIn.StatusCode);
        Assert.Equal("transaction_type_not_found", (await payIn.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  6. RBAC
    // =====================================================================

    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Ruxsatsiz_rol_hamma_endpointda_403(string role)
    {
        var client = await fixture.Api.ClientAsAsync(role);
        var randomId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Types)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Types, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"{Types}/{randomId}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"{Types}/{randomId}")).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Types)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Types, new { })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_royxatni_kora_va_yoza_oladi(string role)
    {
        var client = await fixture.Api.ClientAsAsync(role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Types)).StatusCode);

        var response = await client.PostAsJsonAsync(Types, new { kind = "out", name = $"RBAC {Tag()}" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static async Task<TransactionTypeDto> CreateAsync(
        HttpClient client, string kind, string name, bool isActive = true, int position = 0)
    {
        var response = await client.PostAsJsonAsync(Types, new { kind, name, isActive, position });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TransactionTypeDto>())!;
    }
}
