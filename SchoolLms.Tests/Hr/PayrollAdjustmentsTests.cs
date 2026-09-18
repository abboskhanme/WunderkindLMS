using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Hr;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Hr;

// ===========================================================================
//  BONUS / JARIMA — F11.01, F11.02 (docs/modules/finance-parity.md §2.11).
// ===========================================================================
//
//  NIMA TEKSHIRILADI
//  ------------------
//  1. Yozish, ro'yxat, storno — asosiy oqim, teacher va staff xodim uchun.
//  2. Kompozit ishonch: kind/sabab mos kelmasa, faolsiz sabab, orqaga sana —
//     hammasi 400.
//  3. Storno qoidalari: ikki marta storno, storno'ni storno qilish — 409.
//  4. SPEC §4.4 — server aniqlaydigan maydonlar tanada rad etiladi.
//  5. RBAC (hr.md §5.4): faqat admin/direktor. Kassir, o'qituvchi, oddiy
//     xodim — 403; token'siz — 401. Bu F3.05 xatosining aynan o'zi
//     TAKRORLANMASLIGINI isbotlaydi — task topshirig'i.
//  6. "Trial balance" ekvivalenti: bu yozuv jurnalga (`ledger_entries`)
//     TUSHMAYDI (pul hali ko'chmagan — hr.md §2.7, §5.2), shuning uchun
//     "balans nolga tenglashadi" tasdig'i shu yerda "bonus + jarima +
//     ikkalasining stornosi = FAOL yozuvlar yig'indisi 0" ko'rinishida.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class PayrollAdjustmentsTests(ApiFixture fixture)
{
    private const string Adjustments = "/api/admin/hr/adjustments";
    private const string Reasons = "/api/admin/hr/adjustment-reasons";

    private sealed record ErrorBody(string Code, string Message);

    // =====================================================================
    //  1. Asosiy oqim
    // =====================================================================

    [Fact]
    public async Task Oqituvchiga_bonus_yozish_royxatda_va_auditda_korinadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Yaxshi ish " + Guid.NewGuid());

        var today = AppClock.Today;
        var created = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "teacher",
            employeeId = teacherId,
            kind = "bonus",
            reasonId,
            amount = 300_000m,
            periodYear = today.Year,
            periodMonth = today.Month,
            comment = "Test bonusi",
        });

        Assert.Equal("teacher", created.EmployeeKind);
        Assert.Equal(teacherId, created.EmployeeId);
        Assert.Equal(300_000m, created.Amount);
        Assert.Null(created.ReversalOf);
        Assert.False(created.Reversed);

        var list = await admin.GetFromJsonAsync<List<PayrollAdjustmentDto>>(
            $"{Adjustments}?employeeKind=teacher&employeeId={teacherId}");
        Assert.Contains(list!, a => a.Id == created.Id);

        await fixture.Api.WithDbAsync(async db =>
        {
            var log = await db.AuditLogs.AsNoTracking()
                .Where(l => l.EntityType == "PayrollAdjustment" && l.EntityId == created.Id.ToString("D"))
                .SingleAsync();
            Assert.Equal("create", log.Action);
            Assert.Contains("Bonus", log.Summary, StringComparison.Ordinal);

            // Jurnalga (`ledger_entries`) TUSHMAYDI — pul hali ko'chmagan (hr.md §2.7).
            Assert.Empty(await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefId == created.Id).ToListAsync());
        });
    }

    [Fact]
    public async Task Xodimga_jarima_yozish_ishlaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staff, _) = await fixture.Api.SeedUserAsync(Roles.Staff);
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Penalty, "Kechikish " + Guid.NewGuid());

        var today = AppClock.Today;
        var created = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "staff",
            employeeId = staff.Id,
            kind = "penalty",
            reasonId,
            amount = 50_000m,
            periodYear = today.Year,
            periodMonth = today.Month,
        });

        Assert.Equal("staff", created.EmployeeKind);
        Assert.Equal(staff.Id, created.EmployeeId);
        Assert.Equal("penalty", created.Kind);
    }

    // =====================================================================
    //  2. Storno
    // =====================================================================

    [Fact]
    public async Task Storno_qarshi_qator_qoshadi_originalni_ozgartirmaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Bonus " + Guid.NewGuid());
        var today = AppClock.Today;

        var original = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus", reasonId,
            amount = 100_000m, periodYear = today.Year, periodMonth = today.Month,
        });

        var response = await admin.PostAsJsonAsync(
            $"{Adjustments}/{original.Id}/reverse", new { reason = "Xato yozildi" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mirror = (await response.Content.ReadFromJsonAsync<PayrollAdjustmentDto>())!;
        Assert.Equal(original.Id, mirror.ReversalOf);
        Assert.Equal(original.Amount, mirror.Amount);

        var reloaded = await admin.GetFromJsonAsync<PayrollAdjustmentDto>($"{Adjustments}/{original.Id}");
        Assert.True(reloaded!.Reversed);
        Assert.Null(reloaded.ReversalOf);   // original — TEGILMAGAN
        Assert.Equal(100_000m, reloaded.Amount);
    }

    [Fact]
    public async Task Ikki_marta_storno_409()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Penalty, "Jarima " + Guid.NewGuid());
        var today = AppClock.Today;

        var original = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "penalty", reasonId,
            amount = 40_000m, periodYear = today.Year, periodMonth = today.Month,
        });

        var first = await admin.PostAsJsonAsync($"{Adjustments}/{original.Id}/reverse", new { reason = "Birinchi" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await admin.PostAsJsonAsync($"{Adjustments}/{original.Id}/reverse", new { reason = "Ikkinchi" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_reversed", (await second.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Stornoni_storno_qilib_bolmaydi_409()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Bonus " + Guid.NewGuid());
        var today = AppClock.Today;

        var original = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus", reasonId,
            amount = 20_000m, periodYear = today.Year, periodMonth = today.Month,
        });
        var reverseResponse = await admin.PostAsJsonAsync(
            $"{Adjustments}/{original.Id}/reverse", new { reason = "Xato" });
        var mirror = (await reverseResponse.Content.ReadFromJsonAsync<PayrollAdjustmentDto>())!;

        var doubleReverse = await admin.PostAsJsonAsync(
            $"{Adjustments}/{mirror.Id}/reverse", new { reason = "Yana" });
        Assert.Equal(HttpStatusCode.Conflict, doubleReverse.StatusCode);
        Assert.Equal("already_reversal", (await doubleReverse.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Storno_sababsiz_400()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Bonus " + Guid.NewGuid());
        var today = AppClock.Today;

        var original = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus", reasonId,
            amount = 10_000m, periodYear = today.Year, periodMonth = today.Month,
        });

        var response = await admin.PostAsJsonAsync($"{Adjustments}/{original.Id}/reverse", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("reason_required", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  3. Validatsiya
    // =====================================================================

    [Fact]
    public async Task Sabab_va_yozuv_kindi_mos_kelmasa_400()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var penaltyReasonId = await CreateReasonAsync(admin, AdjustmentKind.Penalty, "Kechikish " + Guid.NewGuid());
        var today = AppClock.Today;

        var response = await admin.PostAsJsonAsync(Adjustments, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus",
            reasonId = penaltyReasonId, amount = 10_000m,
            periodYear = today.Year, periodMonth = today.Month,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("reason_kind_mismatch", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Faolsiz_sabab_yangi_yozuvda_rad_etiladi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Eskirgan sabab " + Guid.NewGuid());

        var toggled = await admin.PutAsJsonAsync($"{Reasons}/{reasonId}", new
        {
            name = "Eskirgan sabab", isActive = false, position = 0,
        });
        Assert.Equal(HttpStatusCode.OK, toggled.StatusCode);

        var today = AppClock.Today;
        var response = await admin.PostAsJsonAsync(Adjustments, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus",
            reasonId, amount = 10_000m, periodYear = today.Year, periodMonth = today.Month,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("reason_inactive", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>SPEC §4 — "no back-dating": o'tgan oyga yozib bo'lmaydi.</summary>
    [Fact]
    public async Task Otgan_oyga_yozish_400()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Bonus " + Guid.NewGuid());

        var lastMonth = AppClock.Today.AddMonths(-1);
        var response = await admin.PostAsJsonAsync(Adjustments, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus", reasonId,
            amount = 10_000m, periodYear = lastMonth.Year, periodMonth = lastMonth.Month,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("period_in_past", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>SPEC §4.4 — server aniqlaydigan maydon tanada kelsa rad etiladi.</summary>
    [Fact]
    public async Task Tanadagi_createdBy_400_beradi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Bonus " + Guid.NewGuid());
        var today = AppClock.Today;

        var response = await admin.PostAsJsonAsync(Adjustments, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus", reasonId,
            amount = 10_000m, periodYear = today.Year, periodMonth = today.Month,
            createdBy = "boshqa-odam",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("identity_in_body", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  4. Sabab katalogi (F11.02)
    // =====================================================================

    [Fact]
    public async Task Bitta_kind_ichida_nom_takrorlansa_409()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Noyob sabab " + Guid.NewGuid();
        await CreateReasonAsync(admin, AdjustmentKind.Bonus, name);

        var response = await admin.PostAsJsonAsync(Reasons, new
        {
            kind = "bonus", name, position = 0,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("reason_name_taken", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task Sababni_tahrirlash_ishlaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var reasonId = await CreateReasonAsync(admin, AdjustmentKind.Penalty, "Boshlang'ich nom " + Guid.NewGuid());

        var newName = "Yangi nom " + Guid.NewGuid();
        var response = await admin.PutAsJsonAsync($"{Reasons}/{reasonId}", new
        {
            name = newName, isActive = true, position = 5,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<AdjustmentReasonDto>())!;
        Assert.Equal(newName, updated.Name);
        Assert.Equal(5, updated.Position);
        Assert.Equal("penalty", updated.Kind);   // o'zgarmadi
    }

    // =====================================================================
    //  5. RBAC (hr.md §5.4) — F3.05 xatosi bu yerda TAKRORLANMAYDI
    // =====================================================================

    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Ruxsatsiz_rol_hamma_endpointda_403(string role)
    {
        var client = await fixture.Api.ClientAsAsync(role);
        var randomId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Adjustments)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Adjustments, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{Adjustments}/{randomId}/reverse", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Reasons)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Reasons, new { })).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Adjustments)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Adjustments, new { })).StatusCode);
    }

    /// <summary>Admin va direktor ikkalasi ham amalni bajara oladi.</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_yoza_oladi(string role)
    {
        var client = await fixture.Api.ClientAsAsync(role);
        var teacherId = await SeedTeacherAsync();
        var reasonId = await CreateReasonAsync(client, AdjustmentKind.Bonus, "Bonus " + Guid.NewGuid());
        var today = AppClock.Today;

        var response = await client.PostAsJsonAsync(Adjustments, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus", reasonId,
            amount = 15_000m, periodYear = today.Year, periodMonth = today.Month,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =====================================================================
    //  6. "Trial balance" ekvivalenti
    // =====================================================================

    /// <summary>
    /// Bu jadval jurnalga (`ledger_entries`) TUSHMAYDI (hr.md §2.7, §5.2) —
    /// pul hali ko'chmagan, faqat kelgusi oylik hisob-kitobga kiradigan
    /// raqam qayd etilmoqda. Shuning uchun "balans nolga tenglashadi" degan
    /// moliyaviy invariant shu yerda: bonus va jarima YOZILADI, ikkalasi ham
    /// STORNO QILINADI, va FAOL (storno qilinmagan, o'zi storno bo'lmagan)
    /// yozuvlar yig'indisi — bonus tomonida ham, jarima tomonida ham — NOLGA
    /// tushadi. Bu — jurnal balansining ushbu modulda qanday ko'rinishi
    /// (task topshirig'i: "trial balance sums to zero after a bonus, a
    /// penalty and a storno of each").
    /// </summary>
    [Fact]
    public async Task Bonus_jarima_va_ularning_stornosidan_song_faol_qoldiq_nolga_tushadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var teacherId = await SeedTeacherAsync();
        var bonusReasonId = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Bonus " + Guid.NewGuid());
        var penaltyReasonId = await CreateReasonAsync(admin, AdjustmentKind.Penalty, "Jarima " + Guid.NewGuid());
        var today = AppClock.Today;

        var bonus = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "bonus", reasonId = bonusReasonId,
            amount = 200_000m, periodYear = today.Year, periodMonth = today.Month,
        });
        var penalty = await CreateAdjustmentAsync(admin, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind = "penalty", reasonId = penaltyReasonId,
            amount = 75_000m, periodYear = today.Year, periodMonth = today.Month,
        });

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{Adjustments}/{bonus.Id}/reverse", new { reason = "Bekor" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{Adjustments}/{penalty.Id}/reverse", new { reason = "Bekor" })).StatusCode);

        var rows = await admin.GetFromJsonAsync<List<PayrollAdjustmentDto>>(
            $"{Adjustments}?employeeKind=teacher&employeeId={teacherId}"
            + $"&periodYear={today.Year}&periodMonth={today.Month}");

        // 4 qator: 2 original (ikkalasi ham storno qilingan) + 2 ko'zgu (storno qatorlari).
        Assert.Equal(4, rows!.Count);

        // FAOL (storno qilinmagan va o'zi storno bo'lmagan) yozuv — YO'Q,
        // ya'ni ikkalasi ham to'liq bekor qilingan: qoldiq NOL.
        var active = rows.Where(r => !r.Reversed && r.ReversalOf is null).ToList();
        Assert.Empty(active);

        // Har bir juftlik ichida summalar ustma-ust — ya'ni original + storno
        // = 0 (ikkalasi ham musbat, lekin biri "bekor qiladi" degan ma'noni
        // `ReversalOf` orqali beradi, xuddi `payments`/`cash_handovers` dagidek).
        var bonusPair = rows.Where(r => r.Kind == "bonus").ToList();
        Assert.Equal(2, bonusPair.Count);
        Assert.All(bonusPair, r => Assert.Equal(200_000m, r.Amount));

        var penaltyPair = rows.Where(r => r.Kind == "penalty").ToList();
        Assert.Equal(2, penaltyPair.Count);
        Assert.All(penaltyPair, r => Assert.Equal(75_000m, r.Amount));

        // Va — hr.md §2.7/§5.2 bo'yicha kutilganidek — bu hech qaysi bosqichda
        // `ledger_entries` ga TEGMAYDI.
        await fixture.Api.WithDbAsync(async db =>
        {
            var ids = rows.Select(r => r.Id).ToList();
            Assert.Empty(await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefId != null && ids.Contains(e.RefId.Value)).ToListAsync());
        });
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<string> SeedTeacherAsync()
    {
        var teacherId = string.Empty;
        await fixture.Api.WithDbAsync(async db =>
        {
            var teacher = new Teacher
            {
                FullName = "Bonus Testi " + Guid.NewGuid().ToString("N")[..6],
                Category = "1",
                SalaryStartDate = "2023-09-01",
                SalaryStartMonth = "2023-09",
            };
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
            teacherId = teacher.Id;
        });
        return teacherId;
    }

    private static async Task<Guid> CreateReasonAsync(HttpClient client, string kind, string name)
    {
        var response = await client.PostAsJsonAsync(Reasons, new { kind, name, position = 0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdjustmentReasonDto>())!.Id;
    }

    private static async Task<PayrollAdjustmentDto> CreateAdjustmentAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Adjustments, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PayrollAdjustmentDto>())!;
    }
}
