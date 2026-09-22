using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Arizalar (surveys) — admin qoidalari va topshiriq → lid qoidalari.
//  docs/modules/sales-marketing.md §8.3 (SurveysTests), §2.6 (Rule S, Rule N),
//  §5.2 (slug, D7, publicUrl), §3.3 N9 (qo'ng'iroq).
// ===========================================================================
//
//  RULE S NEGA ALOHIDA BAZADA
//  --------------------------
//  "Doska bo'sh bo'lsa ustun yaratiladi" va "eng kichik Order li ustun" —
//  ikkalasi ham `lead_stages` jadvalining BUTUN holatiga bog'liq. Umumiy
//  bazada uni na bo'shatib bo'ladi (boshqa testlarning ustunlari), na
//  "eng kichigi shu" deb kafolatlab bo'ladi. Shuning uchun Rule S ning 1–3
//  qadamlari har test uchun shablondan olingan TOZA bazada, controller
//  chaqiradigan AYNAN o'sha `SurveySubmissionService.SubmitAsync` orqali
//  tekshiriladi (`AnalyticsReportsTests` naqshi). HTTP qatlami (ommaviy
//  controller shu metodni chaqirishi) PublicSurveyAbuseTests da.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class SurveysTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Surveys = SalesMarketingKit.AdminSurveys;

    private readonly List<TestDatabase> _freshDatabases = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var database in _freshDatabases)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. Slug (§5.2, §4.1 `ux_surveys_slug` — lower(slug) bo'yicha)
    // =====================================================================

    /// <summary>
    /// <c>QABUL-x</c> va <c>qabul-x</c> — BITTA havola: yaratishda ham,
    /// boshqa arizani tahrirlashda ham 409 <c>slug_taken</c>, bazada ikkinchi
    /// qator paydo bo'lmaydi. Arizaning O'Z slug'i esa unga to'sqinlik qilmaydi.
    /// </summary>
    [Fact]
    public async Task Slug_katta_kichik_harf_farqi_bilan_ham_band_409()
    {
        var tag = SalesMarketingKit.Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var first = await SalesMarketingKit.CreateSurveyAsync(admin, $"qabul-{tag}");
        var other = await SalesMarketingKit.CreateSurveyAsync(admin, $"boshqa-{tag}");

        var clash = await admin.PostAsJsonAsync(Surveys,
            SalesMarketingKit.SurveyBody($"QABUL-{tag.ToUpperInvariant()}", "Nusxa"));
        await AssertConflictAsync(clash, "slug_taken", "Bu havola manzili band — boshqasini tanlang");

        var renameClash = await admin.PutAsJsonAsync($"{Surveys}/{other.Id}",
            SalesMarketingKit.SurveyBody($"Qabul-{tag}", other.Name));
        await AssertConflictAsync(renameClash, "slug_taken", "Bu havola manzili band — boshqasini tanlang");

        await using (var db = NewDb())
        {
            Assert.Equal(1, await db.Surveys.CountAsync(s => s.Slug.ToLower() == $"qabul-{tag}"));
            Assert.Equal($"boshqa-{tag}", (await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == other.Id)).Slug);
        }

        Assert.False(await AvailableAsync(admin, $"QABUL-{tag.ToUpperInvariant()}"));
        Assert.True(await AvailableAsync(admin, $"QABUL-{tag.ToUpperInvariant()}", excludeId: first.Id));
        Assert.True(await AvailableAsync(admin, $"hali-bosh-{tag}"));

        var keepOwn = await admin.PutAsJsonAsync($"{Surveys}/{first.Id}",
            SalesMarketingKit.SurveyBody($"qabul-{tag}", "Yangi nom, eski slug"));
        Assert.Equal(HttpStatusCode.OK, keepOwn.StatusCode);
    }

    /// <summary>Katta harf bilan yozilgan slug RAD etilmaydi — kichik harfga keltirilib saqlanadi.</summary>
    [Fact]
    public async Task Slug_kichik_harfga_keltirilib_saqlanadi()
    {
        var tag = SalesMarketingKit.Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Surveys,
            SalesMarketingKit.SurveyBody($"  Yozgi-LAGER-{tag.ToUpperInvariant()} ", "Yozgi lager"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"yozgi-lager-{tag}", doc.RootElement.GetProperty("slug").GetString());
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("-qabul")]
    [InlineData("qabul-")]
    [InlineData("qabul--2027")]
    [InlineData("qabul 2027")]
    [InlineData("qabul_2027")]
    [InlineData("o'qish")]
    [InlineData("")]
    public async Task Slug_shakli_notogri_bolsa_400(string slug)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = $"Shakl {SalesMarketingKit.Tag()}";

        var response = await admin.PostAsJsonAsync(Surveys, SalesMarketingKit.SurveyBody(slug, name));

        var errors = await AssertValidationAsync(response);
        Assert.True(errors.ContainsKey("slug"), "errors.slug yo'q");
        await using var db = NewDb();
        Assert.False(await db.Surveys.AnyAsync(s => s.Name == name));
    }

    /// <summary>60 belgi — chegara ichida; 61 — tashqarida (§4.1 `length(slug) between 3 and 60`).</summary>
    [Fact]
    public async Task Slug_uzunligi_60_gacha()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = SalesMarketingKit.Tag();
        var sixty = tag + new string('a', 60 - tag.Length);

        var ok = await admin.PostAsJsonAsync(Surveys, SalesMarketingKit.SurveyBody(sixty, "Oltmish"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var tooLong = await admin.PostAsJsonAsync(Surveys, SalesMarketingKit.SurveyBody(sixty + "b", "Oltmish bir"));
        var errors = await AssertValidationAsync(tooLong);
        Assert.Equal("Havola manzili ko'pi bilan 60 ta belgi", errors["slug"]);
    }

    [Fact]
    public async Task Bosh_nom_va_notanish_bosqich_400()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var slug = $"nomsiz-{SalesMarketingKit.Tag()}";

        var response = await admin.PostAsJsonAsync(Surveys,
            SalesMarketingKit.SurveyBody(slug, "   ", stageId: $"yoq-{SalesMarketingKit.Tag()}"));

        var errors = await AssertValidationAsync(response);
        Assert.Equal("Ariza nomini yozing", errors["name"]);
        Assert.Equal("Tanlangan bosqich topilmadi — ro'yxatni yangilang va qaytadan tanlang", errors["stageId"]);
        await using var db = NewDb();
        Assert.False(await db.Surveys.AnyAsync(s => s.Slug == slug));
    }

    // =====================================================================
    //  2. O'chirish qulfi (D7) → yopish
    // =====================================================================

    /// <summary>
    /// Topshirig'i bor ariza o'chmaydi: 409 <c>survey_in_use</c> va xodimga
    /// nima qilish kerakligi yoziladi. Keyin yopish (<c>isActive=false</c>)
    /// ishlaydi: ommaviy sahifa 404, sukutdagi ro'yxatda yo'q, "yopilganlar"
    /// bilan bor, admin uni baribir ocha oladi va qayta ochsa havola tiklanadi.
    /// </summary>
    [Fact]
    public async Task Topshirigi_bor_arizani_ochirib_bolmaydi_409_keyin_yopish_ishlaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"band-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();
        await SalesMarketingKit.AssertAcceptedAsync(
            await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, SalesMarketingKit.Form(), ip), survey.ThankYou);

        var blocked = await admin.DeleteAsync($"{Surveys}/{survey.Id}");
        await AssertConflictAsync(blocked, "survey_in_use",
            "Bu arizada topshirilgan so'rovlar bor — uni o'chirib bo'lmaydi, faol emas qilib qo'ying");
        Assert.Equal((1, 1), await SalesMarketingKit.CountsAsync(fixture, survey.Id));
        await using (var db = NewDb())
            Assert.True(await db.Surveys.AnyAsync(s => s.Id == survey.Id));

        var close = await admin.PatchAsJsonAsync($"{Surveys}/{survey.Id}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, ip)).StatusCode);
        Assert.DoesNotContain(survey.Id, await ListIdsAsync(admin, includeInactive: false));
        var inactive = Assert.Single(await ListAsync(admin, includeInactive: true),
            r => r.GetProperty("id").GetGuid() == survey.Id);
        Assert.False(inactive.GetProperty("isActive").GetBoolean());

        var one = await admin.GetAsync($"{Surveys}/{survey.Id}");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);

        var reopen = await admin.PatchAsJsonAsync($"{Surveys}/{survey.Id}/active", new { isActive = true });
        Assert.Equal(HttpStatusCode.NoContent, reopen.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, ip)).StatusCode);
    }

    [Fact]
    public async Task Topshirigi_yoq_arizani_ochirsa_boladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"bosh-{SalesMarketingKit.Tag()}");

        var deleted = await admin.DeleteAsync($"{Surveys}/{survey.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Surveys}/{survey.Id}")).StatusCode);
        await using var db = NewDb();
        Assert.False(await db.Surveys.AnyAsync(s => s.Id == survey.Id));
    }

    // =====================================================================
    //  3. publicUrl (§5.2) — Tenancy:RootDomain dan, so'rov hostidan EMAS
    // =====================================================================

    /// <summary>
    /// Test hosti <c>https://localhost</c> ga so'rov yuboradi, lekin havola
    /// maktab REKLAMA qiladigan domen bilan chiqishi kerak:
    /// <c>https://{Tenancy:RootDomain ning birinchisi}/ariza/{slug}</c>.
    /// </summary>
    [Fact]
    public async Task PublicUrl_Tenancy_RootDomain_dan_yigiladi()
    {
        var setting = fixture.Api.Services.GetRequiredService<IConfiguration>()["Tenancy:RootDomain"];
        Assert.False(string.IsNullOrWhiteSpace(setting),
            "Test hostida Tenancy:RootDomain bo'sh — test zaxira yo'lni (so'rov hosti) tekshirib qolardi.");
        var apex = setting!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        Assert.NotEqual("localhost", apex);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"havola-{SalesMarketingKit.Tag()}");
        var expected = $"https://{apex}/ariza/{survey.Slug}";

        Assert.Equal(expected, survey.Body.GetProperty("publicUrl").GetString());

        using var one = JsonDocument.Parse(await admin.GetStringAsync($"{Surveys}/{survey.Id}"));
        Assert.Equal(expected, one.RootElement.GetProperty("publicUrl").GetString());

        var row = Assert.Single(await ListAsync(admin, includeInactive: false), r => r.GetProperty("id").GetGuid() == survey.Id);
        Assert.Equal(expected, row.GetProperty("publicUrl").GetString());
    }

    [Theory]
    [InlineData("wunderkindschool.uz, www.wunderkindschool.uz", "http", "test.wunderkindschool.uz", "https://wunderkindschool.uz/ariza/qabul-2027")]
    [InlineData(" , wunderkindschool.uz", "http", "test.wunderkindschool.uz", "https://wunderkindschool.uz/ariza/qabul-2027")]
    [InlineData("", "http", "localhost:5000", "http://localhost:5000/ariza/qabul-2027")]
    [InlineData(null, "https", "test.wunderkindschool.uz", "https://test.wunderkindschool.uz/ariza/qabul-2027")]
    public void PublicUrl_birinchi_domen_va_https_boshqa_holda_sorov_hosti(
        string? setting, string scheme, string host, string expected)
    {
        Assert.Equal(expected, SurveyService.PublicUrl(setting, scheme, host, "qabul-2027"));
    }

    // =====================================================================
    //  4. Hisoblagichlar DTO'da (§8.3)
    // =====================================================================

    /// <summary>
    /// Uchta topshiriq: A, A takrori, B. <c>submissionCount = 3</c> (takror
    /// bilan), <c>leadCount = 2</c>, <c>lastSubmissionAt</c> — oxirgisi. Qo'shni
    /// arizaga hech narsa o'tib ketmaydi.
    /// </summary>
    [Fact]
    public async Task Arizaning_hisoblagichlari_DTO_da()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"sanoq-{SalesMarketingKit.Tag()}");
        var neighbour = await SalesMarketingKit.CreateSurveyAsync(admin, $"qoshni-{SalesMarketingKit.Tag()}");

        Assert.Equal(0, survey.Body.GetProperty("submissionCount").GetInt32());
        Assert.Equal(0, survey.Body.GetProperty("leadCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, survey.Body.GetProperty("lastSubmissionAt").ValueKind);

        using var anon = fixture.Api.AnonymousClient();
        foreach (var child in new[] { "Ali", "Ali", "Vali" })
        {
            await SalesMarketingKit.AssertAcceptedAsync(
                await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
                    SalesMarketingKit.Form(studentFirst: child), SalesMarketingKit.NextIp()),
                survey.ThankYou);
        }

        DateTimeOffset last;
        await using (var db = NewDb())
            last = await db.SurveySubmissions.Where(s => s.SurveyId == survey.Id).MaxAsync(s => s.CreatedAt);

        using var one = JsonDocument.Parse(await admin.GetStringAsync($"{Surveys}/{survey.Id}"));
        AssertCounters(one.RootElement, 3, 2, last);

        var list = await ListAsync(admin, includeInactive: false);
        AssertCounters(Assert.Single(list, r => r.GetProperty("id").GetGuid() == survey.Id), 3, 2, last);
        var other = Assert.Single(list, r => r.GetProperty("id").GetGuid() == neighbour.Id);
        Assert.Equal(0, other.GetProperty("submissionCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("lastSubmissionAt").ValueKind);
    }

    // =====================================================================
    //  5. Rule S (§2.6) — toza bazada
    // =====================================================================

    /// <summary>1-qadam: arizaning o'z bosqichi, u eng kichik <c>Order</c> li bo'lmasa ham.</summary>
    [Fact]
    public async Task RuleS_sozlangan_bosqich_birinchi_tanlanadi()
    {
        var world = await FreshWorldAsync();
        var low = await AddStageAsync(world, "Yangi", order: 0);
        var configured = await AddStageAsync(world, "Qo'ng'iroq qilish", order: 5);
        var slug = await AddSurveyAsync(world, configured);

        await SubmitDirectAsync(world, slug, "Ali");

        await using var db = PostgresFixture.NewContext(world.Database.OwnerConnectionString);
        var lead = await db.Leads.AsNoTracking().SingleAsync();
        Assert.Equal(configured, lead.Stage);
        Assert.NotEqual(low, lead.Stage);
    }

    /// <summary>
    /// 2-qadam: sozlangan ustun o'chirilsa (FK <c>on delete set null</c>) — eng
    /// kichik <c>Order</c> li ustun. Ustunlarda NOL yo'q (Reorder'dan keyin
    /// shunday bo'ladi) — ya'ni qoida "<c>Order == 0</c>" emas, "eng kichigi".
    /// </summary>
    [Fact]
    public async Task RuleS_sozlangan_bosqich_ochirilsa_eng_kichik_Order_li_ustunga_tushadi()
    {
        var world = await FreshWorldAsync();
        await AddStageAsync(world, "Uchinchi", order: 7);
        var lowest = await AddStageAsync(world, "Birinchi", order: 3);
        await AddStageAsync(world, "Oxirgi", order: 9);
        var doomed = await AddStageAsync(world, "O'chiriladigan", order: 1);
        var slug = await AddSurveyAsync(world, doomed);

        await using (var db = PostgresFixture.NewContext(world.Database.OwnerConnectionString))
        {
            await db.LeadStages.Where(s => s.Id == doomed).ExecuteDeleteAsync();
            Assert.Null(await db.Surveys.Where(s => s.Slug == slug).Select(s => s.StageId).SingleAsync());
        }

        await SubmitDirectAsync(world, slug, "Ali");

        await using var check = PostgresFixture.NewContext(world.Database.OwnerConnectionString);
        Assert.Equal(lowest, (await check.Leads.AsNoTracking().SingleAsync()).Stage);
        Assert.Equal(3, await check.LeadStages.CountAsync());
    }

    /// <summary>
    /// 3-qadam: doska BUTUNLAY bo'sh — bitta <c>{Yangi arizalar, blue, 0}</c>
    /// ustuni yaratiladi, lid unga tushadi va bu audit qilinadi (aktyor — odam
    /// emas, "Ariza formasi"). Ikkinchi topshiriq ikkinchi ustun yaratmaydi.
    /// </summary>
    [Fact]
    public async Task Bosqich_yoq_bolsa_ariza_bosqich_yaratadi()
    {
        var world = await FreshWorldAsync();
        var slug = await AddSurveyAsync(world, stageId: null);

        await using (var empty = PostgresFixture.NewContext(world.Database.OwnerConnectionString))
            Assert.Equal(0, await empty.LeadStages.CountAsync());

        await SubmitDirectAsync(world, slug, "Ali");
        await SubmitDirectAsync(world, slug, "Vali");

        await using var db = PostgresFixture.NewContext(world.Database.OwnerConnectionString);
        var stage = await db.LeadStages.AsNoTracking().SingleAsync();
        Assert.Equal("Yangi arizalar", stage.Title);
        Assert.Equal("blue", stage.Color);
        Assert.Equal(0, stage.Order);

        var leads = await db.Leads.AsNoTracking().ToListAsync();
        Assert.Equal(2, leads.Count);
        Assert.All(leads, l => Assert.Equal(stage.Id, l.Stage));

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == stage.Id);
        Assert.Equal("LeadStage", audit.EntityType);
        Assert.Equal("create", audit.Action);
        Assert.Null(audit.ActorId);
        Assert.Equal("Ariza formasi", audit.ActorName);
    }

    // =====================================================================
    //  6. Rule N (§2.6) — izoh BITTA satr
    // =====================================================================

    /// <summary>
    /// Muzlatilgan <c>LeadCard.tsx</c> izohni bitta <c>&lt;p&gt;</c> da chizadi.
    /// Ariza nomida ham, formaning har maydonida ham qator uzilishi bo'lsa,
    /// izoh baribir bitta satr va lid nomlari ham bitta satr.
    /// </summary>
    [Fact]
    public async Task RuleN_izoh_bir_satr_hatto_kirishda_qator_uzilishi_bolsa()
    {
        var tag = SalesMarketingKit.Tag();
        var stageId = await SalesMarketingKit.NewStageAsync(fixture, $"RuleN {tag}");
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"rulen-{tag}",
            name: $"Qabul\n2027\r\n  {tag}", stageId: stageId, studentPhone: true);
        using var anon = fixture.Api.AnonymousClient();

        var started = AppClock.Now;
        await SalesMarketingKit.AssertAcceptedAsync(
            await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
                SalesMarketingKit.Form(parentFirst: "Aziz\nAka", studentFirst: "Ali\r\nJon",
                    studentPhone: "+998 91\n765 43 21"),
                SalesMarketingKit.NextIp()),
            survey.ThankYou);

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        SalesMarketingKit.AssertRuleNNote(lead.Note, $"Qabul 2027 {tag}", "+998 91 765 43 21", started);
        Assert.Equal("Ali Jon Karimov", lead.FullName);
        Assert.Equal("Aziz Aka Karimov", lead.ParentFullName);
    }

    /// <summary>O'quvchi telefoni kelmasa — izoh sanadan keyin tugaydi, "O'quvchi tel" qismi yo'q.</summary>
    [Fact]
    public async Task RuleN_oquvchi_telefoni_bolmasa_izohda_telefon_qismi_yoq()
    {
        var tag = SalesMarketingKit.Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"rulen-{tag}",
            name: $"Qabul {tag}", studentPhone: true);
        using var anon = fixture.Api.AnonymousClient();

        var started = AppClock.Now;
        await SalesMarketingKit.AssertAcceptedAsync(
            await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
                SalesMarketingKit.Form(studentPhone: "  "), SalesMarketingKit.NextIp()),
            survey.ThankYou);

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        SalesMarketingKit.AssertRuleNNote(lead.Note, $"Qabul {tag}", studentPhone: null, started);
    }

    // =====================================================================
    //  7. Topshiriqlar registri (§5.3) — IP faqat detalda
    // =====================================================================

    [Fact]
    public async Task Topshiriq_detali_ip_va_brauzer_satrini_beradi_royxat_esa_bermaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"detal-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();
        await SalesMarketingKit.AssertAcceptedAsync(
            await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, SalesMarketingKit.Form(), ip, SalesMarketingKit.TestUserAgent),
            survey.ThankYou);

        using var list = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin/survey-submissions?surveyId={survey.Id}"));
        var row = Assert.Single(list.RootElement.GetProperty("rows").EnumerateArray().ToList());
        Assert.False(row.TryGetProperty("ip", out _));
        Assert.False(row.TryGetProperty("userAgent", out _));
        Assert.Equal("lead", row.GetProperty("status").GetString());
        var id = row.GetProperty("id").GetGuid();

        using var detail = JsonDocument.Parse(await admin.GetStringAsync($"/api/admin/survey-submissions/{id}"));
        Assert.Equal(ip, detail.RootElement.GetProperty("ip").GetString());
        Assert.Equal(SalesMarketingKit.TestUserAgent, detail.RootElement.GetProperty("userAgent").GetString());
        Assert.Equal("Aziz", detail.RootElement.GetProperty("parentFirstName").GetString());
    }

    /// <summary>
    /// Notanish <c>status</c> filtri e'tiborsiz qolmaydi — 400: aks holda
    /// <c>status=takror</c> BUTUN registrni qaytarib "takror yo'q" deb o'qilardi.
    /// To'g'ri filtr esa faqat o'sha holatdagilarni beradi.
    /// </summary>
    [Fact]
    public async Task Topshiriqlar_holat_filtri_notanish_qiymatni_rad_etadi_togrisini_qollaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"filtr-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        foreach (var _ in new[] { 1, 2 })
        {
            await SalesMarketingKit.AssertAcceptedAsync(
                await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, SalesMarketingKit.Form(), SalesMarketingKit.NextIp()),
                survey.ThankYou);
        }

        var bad = await admin.GetAsync($"/api/admin/survey-submissions?surveyId={survey.Id}&status=takror");
        var errors = await AssertValidationAsync(bad);
        Assert.True(errors.ContainsKey("status"));

        using var dup = JsonDocument.Parse(await admin.GetStringAsync(
            $"/api/admin/survey-submissions?surveyId={survey.Id}&status=duplicate"));
        Assert.Equal(1, dup.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("duplicate",
            Assert.Single(dup.RootElement.GetProperty("rows").EnumerateArray().ToList()).GetProperty("status").GetString());
    }

    // =====================================================================
    //  8. Admin qo'ng'irog'i (§3.3 N9) — faqat lid yaratgan topshiriq
    // =====================================================================

    /// <summary>
    /// Yangi ariza qo'ng'iroqda chiqadi; o'sha oilaning takroriy bosishi esa
    /// CHIQMAYDI — bitta murojaat ikkitadek ko'rinmasin (docs/ASSUMPTIONS.md,
    /// 2026-09-21).
    /// </summary>
    [Fact]
    public async Task Qongiroqda_faqat_lid_yaratgan_topshiriq_chiqadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"qongiroq-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        foreach (var _ in new[] { 1, 2 })
        {
            await SalesMarketingKit.AssertAcceptedAsync(
                await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, SalesMarketingKit.Form(), SalesMarketingKit.NextIp()),
                survey.ThankYou);
        }

        Guid leadRow, duplicateRow;
        await using (var db = NewDb())
        {
            var rows = await db.SurveySubmissions.AsNoTracking().Where(s => s.SurveyId == survey.Id).ToListAsync();
            leadRow = rows.Single(r => r.Status == SurveySubmissionStatus.Lead).Id;
            duplicateRow = rows.Single(r => r.Status == SurveySubmissionStatus.Duplicate).Id;
        }

        using var bell = JsonDocument.Parse(await admin.GetStringAsync("/api/admin/notifications"));
        var items = bell.RootElement.GetProperty("items").EnumerateArray().ToList();

        var item = Assert.Single(items, i => i.GetProperty("id").GetString() == $"survey:{leadRow}");
        Assert.Equal("survey", item.GetProperty("kind").GetString());
        Assert.Equal("Yangi ariza", item.GetProperty("title").GetString());
        Assert.Equal($"Aziz Karimov · {survey.Name}", item.GetProperty("text").GetString());
        Assert.Equal("/admin/marketing/topshirilganlar", item.GetProperty("link").GetString());
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetString() == $"survey:{duplicateRow}");
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, doc.RootElement.GetProperty("message").GetString());
    }

    private static async Task<Dictionary<string, string>> AssertValidationAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("validation", doc.RootElement.GetProperty("code").GetString());
        return doc.RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
    }

    private static async Task<bool> AvailableAsync(HttpClient admin, string slug, Guid? excludeId = null)
    {
        var url = $"{Surveys}/slug-available?slug={Uri.EscapeDataString(slug)}"
                  + (excludeId is { } id ? $"&excludeId={id}" : "");
        using var doc = JsonDocument.Parse(await admin.GetStringAsync(url));
        return doc.RootElement.GetProperty("available").GetBoolean();
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient admin, bool includeInactive)
    {
        using var doc = JsonDocument.Parse(await admin.GetStringAsync($"{Surveys}?includeInactive={includeInactive}"));
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient admin, bool includeInactive) =>
        [.. (await ListAsync(admin, includeInactive)).Select(e => e.GetProperty("id").GetGuid())];

    private static void AssertCounters(JsonElement dto, int total, int leads, DateTimeOffset last)
    {
        Assert.Equal(total, dto.GetProperty("submissionCount").GetInt32());
        Assert.Equal(leads, dto.GetProperty("leadCount").GetInt32());
        var actual = dto.GetProperty("lastSubmissionAt").GetDateTimeOffset();
        Assert.True(Math.Abs((actual - last).TotalMilliseconds) < 1, $"lastSubmissionAt {actual:o} ≠ {last:o}");
    }

    private sealed record World(TestDatabase Database, string UserId);

    /// <summary>Shablondan toza baza + arizaga muallif bo'ladigan bitta foydalanuvchi.</summary>
    private async Task<World> FreshWorldAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("smrules");
        _freshDatabases.Add(database);

        var user = new AppUser { FullName = "Rule S muallifi", Role = Roles.Admin, Email = $"rules.{SalesMarketingKit.Tag()}" };
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new World(database, user.Id);
    }

    private static async Task<string> AddStageAsync(World world, string title, int order)
    {
        var stage = new LeadStage { Title = title, Color = "slate", Order = order };
        await using var db = PostgresFixture.NewContext(world.Database.OwnerConnectionString);
        db.LeadStages.Add(stage);
        await db.SaveChangesAsync();
        return stage.Id;
    }

    private static async Task<string> AddSurveyAsync(World world, string? stageId)
    {
        var slug = $"rules-{SalesMarketingKit.Tag()}";
        await using var db = PostgresFixture.NewContext(world.Database.OwnerConnectionString);
        db.Surveys.Add(new Survey { Slug = slug, Name = "Rule S", StageId = stageId, CreatedBy = world.UserId });
        await db.SaveChangesAsync();
        return slug;
    }

    /// <summary>
    /// Ommaviy controller chaqiradigan AYNAN o'sha ikki metod —
    /// <c>FindActiveAsync</c> va <c>SubmitAsync</c> — toza bazaga qarshi.
    /// </summary>
    private async Task SubmitDirectAsync(World world, string slug, string child)
    {
        await using var db = PostgresFixture.NewContext(world.Database.OwnerConnectionString);
        var service = new SurveySubmissionService(
            db, fixture.Api.Services.GetRequiredService<ILogger<SurveySubmissionService>>());

        var survey = await service.FindActiveAsync(slug);
        Assert.NotNull(survey);

        var outcome = await service.SubmitAsync(survey,
            new PublicSurveySubmitRequest(
                ParentFirstName: "Aziz", ParentLastName: "Karimov", ParentPhone: "+998 90 123 45 67",
                StudentFirstName: child, StudentLastName: "Karimov", StudentPhone: null,
                StudentGrade: 5, StudentGender: "male",
                ServedAt: SalesMarketingKit.MinutesAgo(5), Website: null),
            ip: "198.51.100.250", userAgent: "rule-s");

        Assert.Equal(SurveySubmitKind.Accepted, outcome.Kind);
    }
}
