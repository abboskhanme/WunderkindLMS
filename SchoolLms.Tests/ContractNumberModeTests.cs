using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Shartnoma raqamlash qoidasi — docs/modules/students-parity.md §2.10.3 (K-6).
///
/// <para>
/// <b>Doirasi.</b> Bu to'lqinda <c>StudentContractsController.cs</c> boshqa
/// agentga tegishli fayl — SHU SLICE uni o'zgartirmaydi (parallel ish qoidasi,
/// worktree buyrug'idagi "Do not touch" ro'yxati). Shuning uchun bu yerda IKKI
/// narsa alohida tekshiriladi:
/// (1) sozlamaning o'zi — <c>SettingsController</c> orqali o'qish/yozish, ruxsat,
///     yaroqsiz qiymat;
/// (2) <c>ContractService</c> dagi qoida FUNKSIYASI — <c>GetNumberModeAsync</c> va
///     <c>Resolve</c> — ular <c>StudentContractsController</c> ULANGANDA ishlatiladigan
///     tayyor, sinovdan o'tgan mantiq (wiring — keyingi bosqich, quyidagi izohga qarang).
/// </para>
/// <para>
/// <b>"Kollizyani sinash".</b> Ilova qatlamidan MUSTAQIL — <c>ux_student_contracts_number</c>
/// qisman unikal indeksining O'ZI (baza darajasi) ikkita qatorga bir xil raqam yozishga
/// yo'l qo'ymasligini bevosita <c>DbContext</c> orqali tekshiradi (controller qatnashmaydi).
/// Ilova qatlamidagi tekshiruv (<c>NumberTakenAsync</c> → 400) allaqachon
/// <c>StudentContractTests.Crud_va_takroriy_raqam_rad_etiladi</c> da qamrab olingan.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ContractNumberModeTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/settings/contracts";

    // =====================================================================
    //  1. SOZLAMA — SettingsController
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync(Url, new { numberMode = "manual" })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Settings_ruxsatisiz_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(Url, new { numberMode = "manual" })).StatusCode);
    }

    /// <summary>"settings" ruxsatli xodim ham, admin ham o'qiy/yoza oladi — pul emas, oddiy sozlama.</summary>
    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Settings_ruxsatli_rollar_oqiydi_va_yozadi(string role)
    {
        using var client = role == Roles.Staff
            ? await fixture.Api.ClientAsAsync(role, "settings")
            : await fixture.Api.ClientAsAsync(role);

        await WithModeAsync(null, async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await client.PutAsJsonAsync(Url, new { numberMode = "manual" })).StatusCode);
        });
    }

    [Fact]
    public async Task Qator_yoq_bolsa_sukut_auto()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        await WithModeAsync(null, async () =>
        {
            var json = await JsonAsync(client, Url);
            Assert.Equal(ContractNumberMode.Auto, json.GetProperty("numberMode").GetString());
        });
    }

    [Fact]
    public async Task Yaroqsiz_qiymat_400()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        await WithModeAsync(null, async () =>
        {
            var bad = await client.PutAsJsonAsync(Url, new { numberMode = "weekly" });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(SettingsController.ContractNumberModeInvalidMessage, await MessageAsync(bad));

            var blank = await client.PutAsJsonAsync(Url, new { numberMode = "" });
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        });
    }

    [Fact]
    public async Task Saqlash_va_oqish_royxatga_tushadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        await WithModeAsync(null, async () =>
        {
            var saved = await client.PutAsJsonAsync(Url, new { numberMode = "MANUAL" });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(ContractNumberMode.Manual,
                (await JsonAsync(saved)).GetProperty("numberMode").GetString());

            var loaded = await JsonAsync(client, Url);
            Assert.Equal(ContractNumberMode.Manual, loaded.GetProperty("numberMode").GetString());

            await fixture.Api.WithDbAsync(async db =>
                Assert.Equal(ContractNumberMode.Manual,
                    (await db.SchoolMeta.AsNoTracking().SingleAsync()).ContractNumberMode));
        });
    }

    // =====================================================================
    //  2. QOIDA FUNKSIYASI — ContractService (wiring tayyor, controller kutmoqda)
    // =====================================================================

    [Fact]
    public async Task GetNumberModeAsync_qator_yoq_bolsa_auto_qaytaradi()
    {
        await WithModeAsync(null, async () =>
        {
            using var scope = fixture.Api.Services.CreateScope();
            var contracts = scope.ServiceProvider.GetRequiredService<ContractService>();
            Assert.Equal(ContractNumberMode.Auto, await contracts.GetNumberModeAsync());
        });
    }

    [Fact]
    public async Task GetNumberModeAsync_saqlangan_qiymatni_qaytaradi()
    {
        await WithModeAsync(ContractNumberMode.Manual, async () =>
        {
            using var scope = fixture.Api.Services.CreateScope();
            var contracts = scope.ServiceProvider.GetRequiredService<ContractService>();
            Assert.Equal(ContractNumberMode.Manual, await contracts.GetNumberModeAsync());
        });
    }

    [Fact]
    public void Resolve_auto_rejimida_generatsiyaga_yuboradi_qolda_kiritilgan_qiymat_bolsa_ham()
    {
        var decision = ContractService.Resolve(ContractNumberMode.Auto, "2026/14-A");
        Assert.True(decision.ShouldGenerate);
        Assert.True(decision.IsValid);
        Assert.Null(decision.ManualNumber);
    }

    [Fact]
    public void Resolve_manual_rejimida_qolda_raqamni_ishlatadi()
    {
        var decision = ContractService.Resolve(ContractNumberMode.Manual, "  2026/14-A  ");
        Assert.False(decision.ShouldGenerate);
        Assert.True(decision.IsValid);
        Assert.Equal("2026/14-A", decision.ManualNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_manual_rejimida_bosh_raqamni_rad_etadi(string? raw)
    {
        var decision = ContractService.Resolve(ContractNumberMode.Manual, raw);
        Assert.False(decision.IsValid);
        Assert.Equal(ContractService.ManualNumberRequiredMessage, decision.Error);
    }

    // =====================================================================
    //  3. KOLLIZIYA — baza darajasidagi qisman unikal indeks (controllerdan mustaqil)
    // =====================================================================

    /// <summary>
    /// Ikkita <c>StudentContract</c> qatorini bevosita <c>DbContext</c> orqali BIR XIL
    /// raqam bilan yozishga urinamiz — <c>StudentContractsController</c> UMUMAN
    /// qatnashmaydi. Bu "manual" rejimda ilova qatlami chetlab o'tilsa ham (yoki hali
    /// unga ulanmagan bo'lsa ham) unikallikning BAZA o'zi tomonidan kafolatlanishini
    /// isbotlaydi — K-6 ning "faqat formada emas" talabi shu yerda.
    /// </summary>
    [Fact]
    public async Task Baza_darajasidagi_unikal_indeks_raqam_takrorlanishini_rad_etadi()
    {
        var tag = "KM" + Guid.NewGuid().ToString("N")[..8];
        var (author, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var s1 = GeneralSettingsFlagsTests.NewStudent($"Kolliziya A {tag}", $"KM-{tag[..4]}", "+998900000008");
        var s2 = GeneralSettingsFlagsTests.NewStudent($"Kolliziya B {tag}", $"KM-{tag[..4]}", "+998900000009");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.AddRange(s1, s2);
            await db.SaveChangesAsync();
        });

        await fixture.Api.WithDbAsync(async db =>
        {
            db.StudentContracts.Add(new StudentContract
            {
                StudentId = s1.Id,
                Number = tag,
                Source = StudentContractSource.Uploaded,
                CreatedBy = author.Id,
                CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
        });

        await fixture.Api.WithDbAsync(async db =>
        {
            db.StudentContracts.Add(new StudentContract
            {
                StudentId = s2.Id,
                Number = tag, // xuddi shu raqam — ikkinchi bolaga
                Source = StudentContractSource.Uploaded,
                CreatedBy = author.Id,
                CreatedAt = AppClock.NowInstant,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>
    /// <c>school_meta.contract_number_mode</c> ni test davomida almashtiradi va keyin
    /// ASL holatiga qaytaradi (test yiqilsa ham) — <c>GeneralSettingsFlagsTests</c> dagi
    /// <c>SchoolMetaFlags.WithAsync</c> bilan bir xil naqsh, faqat bitta ustun uchun.
    /// <paramref name="mode"/> — <c>null</c> bo'lsa qiymat o'zgartirilmaydi (qator faqat
    /// kerak bo'lsa yaratiladi, joriy rejim tekshiriladi).
    /// </summary>
    private async Task WithModeAsync(string? mode, Func<Task> body)
    {
        string? createdId = null;
        var before = ContractNumberMode.Auto;

        await fixture.Api.WithDbAsync(async db =>
        {
            var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstOrDefaultAsync();
            if (meta is null)
            {
                meta = new SchoolMeta();
                db.SchoolMeta.Add(meta);
                await db.SaveChangesAsync();
                createdId = meta.Id;
            }
            before = meta.ContractNumberMode;
            if (mode is not null)
            {
                meta.ContractNumberMode = mode;
                await db.SaveChangesAsync();
            }
        });

        try
        {
            await body();
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                if (createdId is not null)
                {
                    await db.SchoolMeta.Where(m => m.Id == createdId).ExecuteDeleteAsync();
                    return;
                }
                var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstAsync();
                meta.ContractNumberMode = before;
                await db.SaveChangesAsync();
            });
        }
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url) =>
        await JsonAsync(await client.GetAsync(url));

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }
}
