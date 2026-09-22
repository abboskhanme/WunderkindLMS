using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Ommaviy ariza formasi — SUISTE'MOL testlari.
//  docs/modules/sales-marketing.md §8.2 (§2.4, §2.5, §2.6, §5.1).
// ===========================================================================
//
//  NEGA BU FAYL BOR
//  ----------------
//  `POST /api/public/surveys/{slug}` — mahsulotdagi BIRINCHI anonim endpoint
//  bo'lib, u biznes yozuvini (lidni) yaratadi. Internetdagi har qanday skript
//  unga yozadi. Shuning uchun uchta himoya qatlami (chastota chegarasi,
//  honeypot, vaqt tuzog'i) va "hech narsa yozilmadi" degan da'vo shu yerda
//  BAZA BILAN tekshiriladi, faqat status bilan emas.
//
//  VAQT TUZOG'I — 2026-09-21 TUZATISHI (docs/ASSUMPTIONS.md)
//  ---------------------------------------------------------
//  §8.2 jadvalidagi `Eskirgan_servedAt_lid_yaratmaydi` qatori ESKIRGAN. Faqat
//  `now - servedAt < 2 s` (va kelajakdagi qiymat) jim 200 tuzog'i bo'lib
//  qoldi; yo'q, o'qib bo'lmaydigan yoki 2 soatdan eski `servedAt` — QABUL
//  qilinadi (lid yaratiladi). Bu fayl TUZATILGAN xulqni mahkamlaydi.
//
//  X-FORWARDED-FOR — HAR TEST O'Z CHELAGIDA (§2.5)
//  -----------------------------------------------
//  TestServer'da `RemoteIpAddress` yo'q, ya'ni sarlavhasiz so'rovlar butun
//  suite bo'yicha BITTA `"unknown"` chelagiga tushadi va oltinchi POST
//  aloqasiz testni 429 bilan yiqitadi. Shuning uchun ommaviy endpoint'ga
//  ketadigan HAR so'rov `SalesMarketingKit.NextIp()` bergan takrorlanmas
//  `203.0.113.<n>` (TEST-NET-3) bilan yuboriladi.
//
//  "HECH NARSA YOZILMADI" — GLOBAL SANOQ BILAN
//  -------------------------------------------
//  Butun suite bitta kolleksiyada (ketma-ket) yuradi, ya'ni test davomida
//  `leads` va `survey_submissions` ga boshqa hech kim yozmaydi. Shuning uchun
//  "yozuv yo'q" tekshiruvi arizaga bog'langan qatorlar bilan cheklanmaydi —
//  IKKALA jadvalning UMUMIY soni oldin va keyin solishtiriladi.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class PublicSurveyAbuseTests(ApiFixture fixture)
{
    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. Honeypot (§2.5, 2-qatlam)
    // =====================================================================

    /// <summary>
    /// Ko'rinmas <c>website</c> maydoni to'ldirilgan — 200, ODDIY muvaffaqiyat
    /// tanasi (haqiqiy topshiriqnikidan BAYTMA-BAYT farq qilmaydi) va IKKALA
    /// jadvalda ham birorta qator yo'q.
    /// </summary>
    [Fact]
    public async Task Honeypot_toldirilgan_sorov_lid_yaratmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"hp-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        var before = await GlobalCountsAsync();

        var trapped = SalesMarketingKit.Form();
        trapped["website"] = "https://spam.example";
        var botResponse = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, trapped, SalesMarketingKit.NextIp());
        var botBody = await SalesMarketingKit.AssertAcceptedAsync(botResponse, survey.ThankYou);

        Assert.Equal(before, await GlobalCountsAsync());

        // Xuddi shu forma, lekin odam yuborgan — javob tanasi AYNAN bir xil bo'lishi shart:
        // aks holda bot qaysi javob "tutildi" ekanini ajratib olardi (§5.1).
        var human = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(), SalesMarketingKit.NextIp());
        var humanBody = await SalesMarketingKit.AssertAcceptedAsync(human, survey.ThankYou);
        Assert.Equal(humanBody, botBody);
        Assert.Equal((before.Leads + 1, before.Submissions + 1), await GlobalCountsAsync());
    }

    /// <summary>
    /// Honeypot TEKSHIRUVDAN OLDIN ishlaydi (§2.6: 2-qadam, 3-qadamdan oldin):
    /// bot to'ldirgan forma buzuq bo'lsa ham u 400 emas, jim 200 oladi —
    /// aks holda xato matni botga "sen tutilmading" deb aytib qo'yardi.
    /// </summary>
    [Fact]
    public async Task Honeypot_buzuq_forma_bilan_ham_jim_200_qaytaradi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"hp-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var before = await GlobalCountsAsync();

        var form = SalesMarketingKit.Form(parentPhone: "1", grade: 42);
        form["website"] = "x";
        var response = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, form, SalesMarketingKit.NextIp());

        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);
        Assert.Equal(before, await GlobalCountsAsync());
    }

    // =====================================================================
    //  2. Vaqt tuzog'i (§2.5, 3-qatlam, 2026-09-21 tuzatishi bilan)
    // =====================================================================

    /// <summary>
    /// Sahifa bergan <c>servedAt</c> AYNAN qaytarildi, lekin 2 soniya o'tmay —
    /// odam bunday tez to'ldira olmaydi: 200, hech narsa yozilmaydi.
    ///
    /// <para>
    /// Qiymat qo'lda yasalmaydi, GET javobidan olinadi — bu server O'Z
    /// formatini o'qiy olishini ham isbotlaydi. O'qiy olmasa, tuzatish
    /// qoidasi bo'yicha forma QABUL qilinardi va tuzoq amalda o'lik bo'lardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Juda_tez_yuborilgan_forma_lid_yaratmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"tez-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();

        var page = await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, ip);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var pageDoc = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var servedAt = pageDoc.RootElement.GetProperty("servedAt").GetString();
        Assert.False(string.IsNullOrWhiteSpace(servedAt));

        var before = await GlobalCountsAsync();

        var echoed = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(servedAt: servedAt), ip);
        await SalesMarketingKit.AssertAcceptedAsync(echoed, survey.ThankYou);

        var justNow = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug,
            SalesMarketingKit.Form(servedAt: DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)), ip);
        await SalesMarketingKit.AssertAcceptedAsync(justNow, survey.ThankYou);

        Assert.Equal(before, await GlobalCountsAsync());
    }

    /// <summary>
    /// Kelajakdagi <c>servedAt</c> — soxta qiymat; tuzatishdan keyin ham tuzoq
    /// bo'lib qoladi (docs/ASSUMPTIONS.md, 2026-09-21).
    /// </summary>
    [Fact]
    public async Task Kelajakdagi_servedAt_lid_yaratmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"kel-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var before = await GlobalCountsAsync();

        var future = DateTimeOffset.UtcNow.AddHours(1).ToString("o", CultureInfo.InvariantCulture);
        var response = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(servedAt: future), SalesMarketingKit.NextIp());

        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);
        Assert.Equal(before, await GlobalCountsAsync());
    }

    /// <summary>
    /// TUZATILGAN xulq (§2.5, 2026-09-21): formani kechqurun ochib ertalab
    /// yuborgan ota-onaning arizasi YO'QOLMAYDI — 3 soatlik <c>servedAt</c>
    /// bilan lid yaratiladi. (§8.2 dagi asl <c>Eskirgan_servedAt_lid_yaratmaydi</c>
    /// o'rniga.)
    /// </summary>
    [Fact]
    public async Task Eskirgan_servedAt_ham_qabul_qilinadi_va_lid_yaratadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"eski-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        var threeHoursAgo = DateTimeOffset.UtcNow.AddHours(-3).ToString("o", CultureInfo.InvariantCulture);
        var response = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(studentFirst: "Kechqurun"), SalesMarketingKit.NextIp());
        // Birinchisi — oddiy (5 daqiqalik) forma; ikkinchisi — 3 soatlik.
        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);
        var old = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(studentFirst: "Ertalab", servedAt: threeHoursAgo),
            SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(old, survey.ThankYou);

        await using var db = NewDb();
        var leads = await db.Leads.AsNoTracking().Where(l => l.SurveyId == survey.Id).ToListAsync();
        Assert.Equal(2, leads.Count);
        Assert.Contains(leads, l => l.FullName == "Ertalab Karimov");
    }

    /// <summary>
    /// TUZATILGAN xulq: <c>servedAt</c> umuman kelmasa, bo'sh bo'lsa yoki sana
    /// sifatida o'qilmasa — ariza QABUL qilinadi (lid va topshiriq yoziladi).
    /// </summary>
    [Theory]
    [InlineData("yoq")]
    [InlineData("bosh")]
    [InlineData("buzuq")]
    public async Task ServedAt_yoq_yoki_buzuq_bolsa_ham_lid_yaratadi(string holat)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"sa-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        var form = SalesMarketingKit.Form();
        switch (holat)
        {
            case "yoq": form.Remove("servedAt"); break;
            case "bosh": form["servedAt"] = ""; break;
            default: form["servedAt"] = "kecha kechqurun"; break;
        }

        var response = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, form, SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);

        Assert.Equal((1, 1), await SalesMarketingKit.CountsAsync(fixture, survey.Id));
    }

    // =====================================================================
    //  3. Chastota chegarasi (§2.5, 1-qatlam)
    // =====================================================================

    /// <summary>
    /// Bitta manzildan 10 daqiqada beshta topshiriq — oltinchisi 429 (tanasi bo'sh,
    /// global rad javobi) va u HECH NARSA yozmaydi. Boshqa manzil esa o'sha
    /// paytda bemalol o'tadi: chelak butun dunyo uchun emas, IP bo'yicha.
    /// </summary>
    [Fact]
    public async Task Chegaradan_oshgan_sorov_429_qaytaradi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"lim-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();

        for (var i = 1; i <= 5; i++)
        {
            var ok = await SalesMarketingKit.PublicPostAsync(
                anon, survey.Slug, SalesMarketingKit.Form(studentFirst: $"Bola{i}"), ip);
            await SalesMarketingKit.AssertAcceptedAsync(ok, survey.ThankYou);
        }

        var before = await GlobalCountsAsync();
        var sixth = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(studentFirst: "Oltinchi"), ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.Empty(await sixth.Content.ReadAsStringAsync());
        Assert.Equal(before, await GlobalCountsAsync());
        Assert.Equal((5, 5), await SalesMarketingKit.CountsAsync(fixture, survey.Id));

        var otherIp = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(studentFirst: "Qoshni"), SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(otherIp, survey.ThankYou);
    }

    /// <summary>
    /// Sahifani ochish chegarasi alohida va yumshoq: daqiqasiga 60 ta (§2.5
    /// <c>survey-read</c>). 61-chisi 429.
    /// </summary>
    [Fact]
    public async Task Sahifa_ochish_chegarasi_daqiqasiga_60_ta()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"read-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();

        for (var i = 1; i <= 60; i++)
        {
            var ok = await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, ip);
            Assert.True(ok.StatusCode == HttpStatusCode.OK, $"{i}-so'rov: {(int)ok.StatusCode}");
        }

        var over = await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode);
        Assert.Empty(await over.Content.ReadAsStringAsync());

        var other = await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, SalesMarketingKit.NextIp());
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    // =====================================================================
    //  4. 404 — havola qachondir mavjud bo'lganini tasdiqlamaydi (§5.1, D8)
    // =====================================================================

    /// <summary>
    /// Yopilgan ariza va umuman mavjud bo'lmagan slug — GET da ham, POST da ham
    /// BAYTMA-BAYT bir xil 404 tanasi. Yopilgan arizaga POST hech narsa yozmaydi.
    /// </summary>
    [Fact]
    public async Task Yopilgan_ariza_404_va_tanasi_notanish_slug_bilan_bir_xil()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"yop-{SalesMarketingKit.Tag()}");
        var closed = await admin.PatchAsJsonAsync($"{SalesMarketingKit.AdminSurveys}/{survey.Id}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);

        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();
        var unknownSlug = $"hech-qachon-bolmagan-{SalesMarketingKit.Tag()}";

        var getClosed = await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, ip);
        var getUnknown = await SalesMarketingKit.PublicGetAsync(anon, unknownSlug, ip);
        await AssertIdentical404Async(getClosed, getUnknown);

        var before = await GlobalCountsAsync();
        var postClosed = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, SalesMarketingKit.Form(), ip);
        var postUnknown = await SalesMarketingKit.PublicPostAsync(anon, unknownSlug, SalesMarketingKit.Form(), ip);
        await AssertIdentical404Async(postClosed, postUnknown);
        Assert.Equal(before, await GlobalCountsAsync());

        // GET va POST ham bir-biridan farq qilmaydi — bitta tana, bitta joyda.
        Assert.Equal(
            await getClosed.Content.ReadAsByteArrayAsync(),
            await postClosed.Content.ReadAsByteArrayAsync());
    }

    // =====================================================================
    //  5. Tekshiruv (§2.4) — 400 `validation`, hech narsa yozilmaydi
    // =====================================================================

    [Fact]
    public async Task Notogri_telefon_400()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"tel-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var before = await GlobalCountsAsync();

        var response = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(parentPhone: "123"), SalesMarketingKit.NextIp());

        var errors = await AssertValidationAsync(response);
        Assert.Equal("Telefon raqamini to'liq kiriting", errors["parentPhone"]);
        Assert.Equal(before, await GlobalCountsAsync());
    }

    /// <summary>
    /// Har bir majburiy/chegaralangan maydon o'z xatosini o'z kaliti bilan
    /// qaytaradi (sahifa xatoni maydon ostida chizadi) va bazaga hech narsa
    /// tushmaydi.
    /// </summary>
    [Theory]
    [InlineData("studentGrade", "12", "Sinfni 0 dan 11 gacha tanlang")]
    [InlineData("studentGrade", "-1", "Sinfni 0 dan 11 gacha tanlang")]
    [InlineData("studentGrade", null, "Sinfni tanlang")]
    [InlineData("studentGender", "other", "Jinsini tanlang")]
    [InlineData("studentGender", null, "Jinsini tanlang")]
    [InlineData("parentFirstName", "A", "Ism kamida 2 ta harfdan iborat bo'lsin")]
    [InlineData("parentFirstName", "   ", "Ismni kiriting")]
    [InlineData("studentFirstName", "", "O'quvchining ismini kiriting")]
    public async Task Notogri_maydon_400_va_hech_narsa_yozilmaydi(string field, string? value, string message)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"val-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var before = await GlobalCountsAsync();

        var form = SalesMarketingKit.Form();
        form[field] = field == "studentGrade" && value is not null
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : value;

        var response = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug, form, SalesMarketingKit.NextIp());

        var errors = await AssertValidationAsync(response);
        Assert.Equal(message, errors[field]);
        Assert.Equal(before, await GlobalCountsAsync());
    }

    /// <summary>
    /// <c>0</c> — HAQIQIY sinf (nol sinf), "noma'lum" emas (§2.1). U rad
    /// etilmaydi va lidga AYNAN 0 bo'lib tushadi.
    /// </summary>
    [Fact]
    public async Task Nol_sinf_haqiqiy_sinf_sifatida_qabul_qilinadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"nol-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        var response = await SalesMarketingKit.PublicPostAsync(
            anon, survey.Slug, SalesMarketingKit.Form(grade: 0), SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        Assert.Equal(0, lead.TargetGrade);
        var submission = await db.SurveySubmissions.AsNoTracking().SingleAsync(s => s.SurveyId == survey.Id);
        Assert.Equal((short)0, submission.StudentGrade);
    }

    // =====================================================================
    //  6. Takror (§2.6, 4-qadam)
    // =====================================================================

    /// <summary>
    /// Bitta oila tugmani ikki marta bosdi: ikkinchi javob ham AYNAN o'sha 200,
    /// topshiriq qatori YOZILADI (<c>status='duplicate'</c>, birinchi lidga
    /// bog'langan), lekin ikkinchi lid yaratilmaydi. Telefon boshqa formatda,
    /// ism boshqa registr va bo'shliqlar bilan yozilgan — solishtirish
    /// normallashgan qiymat bo'yicha.
    /// </summary>
    [Fact]
    public async Task Bir_xil_telefon_va_ism_24_soat_ichida_ikkinchi_lid_yaratmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"dup-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        var first = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(parentPhone: "+998 90 123 45 67", studentFirst: "Ali", studentLast: "Karimov"),
            SalesMarketingKit.NextIp());
        var firstBody = await SalesMarketingKit.AssertAcceptedAsync(first, survey.ThankYou);

        var again = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(parentPhone: "90-123-45-67", studentFirst: "  ali ", studentLast: "KARIMOV "),
            SalesMarketingKit.NextIp());
        var againBody = await SalesMarketingKit.AssertAcceptedAsync(again, survey.ThankYou);
        Assert.Equal(firstBody, againBody);

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        var submissions = await db.SurveySubmissions.AsNoTracking()
            .Where(s => s.SurveyId == survey.Id).OrderBy(s => s.CreatedAt).ToListAsync();

        Assert.Equal(2, submissions.Count);
        Assert.Equal(SurveySubmissionStatus.Lead, submissions[0].Status);
        Assert.Equal(SurveySubmissionStatus.Duplicate, submissions[1].Status);
        Assert.Equal(lead.Id, submissions[0].LeadId);
        Assert.Equal(lead.Id, submissions[1].LeadId);
        // Takror qatori ham ota-ona AYNAN nima yozganini saqlaydi.
        Assert.Equal("90-123-45-67", submissions[1].ParentPhone);
        Assert.Equal("901234567", submissions[1].ParentPhoneKey);
    }

    /// <summary>
    /// Xodim lidni doskadan o'chirdi, oila esa 24 soat ichida qayta yubordi — bu YANGI
    /// murojaat (ASSUMPTIONS.md, 2026-09-21). Aks holda ikkinchi topshiriq lidi yo'q
    /// "takror" bo'lib qolar va oilaga hech kim qo'ng'iroq qilmasdi.
    /// </summary>
    [Fact]
    public async Task Lid_ochirilgach_qayta_topshirish_yangi_lid_yaratadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"del-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        var first = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(studentFirst: "Ali"), SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(first, survey.ThankYou);

        string firstLeadId;
        await using (var db = NewDb())
            firstLeadId = (await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id)).Id;
        var deleted = await admin.DeleteAsync($"/api/admin/leads/{firstLeadId}");
        Assert.True(deleted.IsSuccessStatusCode, $"lid o'chirilmadi: {deleted.StatusCode}");

        var again = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(studentFirst: "Ali"), SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(again, survey.ThankYou);

        await using var check = NewDb();
        var lead = await check.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        Assert.NotEqual(firstLeadId, lead.Id);
        var latest = await check.SurveySubmissions.AsNoTracking()
            .Where(x => x.SurveyId == survey.Id).OrderByDescending(x => x.CreatedAt).FirstAsync();
        Assert.Equal(SurveySubmissionStatus.Lead, latest.Status);
        Assert.Equal(lead.Id, latest.LeadId);
    }

    /// <summary>
    /// Ism so'ralmaydigan arizada bir oila ikki farzandini yubordi: telefon bir xil,
    /// ism kaliti ikkalasida bo'sh — farq faqat sinf va jinsda. Bu IKKI lid; faqat
    /// telefon bo'yicha solishtirilsa ikkinchi farzand "takror" bo'lib yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Ismsiz_arizada_boshqa_sinfdagi_farzand_yangi_lid_yaratadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"ikki-{SalesMarketingKit.Tag()}",
            firstName: false, lastName: false);
        using var anon = fixture.Api.AnonymousClient();

        foreach (var (grade, gender) in new[] { (1, "male"), (4, "female") })
        {
            var response = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
                SalesMarketingKit.Form(studentFirst: null, studentLast: null, grade: grade, gender: gender),
                SalesMarketingKit.NextIp());
            await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);
        }

        await using var db = NewDb();
        var grades = await db.Leads.AsNoTracking().Where(l => l.SurveyId == survey.Id)
            .Select(l => l.TargetGrade).OrderBy(g => g).ToListAsync();
        Assert.Equal([1, 4], grades);
    }

    /// <summary>
    /// O'chirilgan maydon tugmasi bazaga <c>false</c> bo'lib yoziladi. EF Core'ning
    /// "sukut qiymati" tuzog'ida (<c>HasDefaultValue(true)</c> sentinel'siz) <c>false</c>
    /// INSERT'dan tushib qolib, baza DEFAULT'i <c>true</c> g'olib chiqardi.
    /// </summary>
    [Fact]
    public async Task Ochirilgan_maydon_tugmasi_bazaga_false_bolib_yoziladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"false-{SalesMarketingKit.Tag()}",
            firstName: false, lastName: false);

        await using var db = NewDb();
        var row = await db.Surveys.AsNoTracking().SingleAsync(x => x.Id == survey.Id);
        Assert.False(row.ShowStudentFirstNameInput);
        Assert.False(row.ShowStudentLastNameInput);
        Assert.True(row.ShowStudentGradeInput);
        Assert.True(row.ShowStudentGenderInput);
        Assert.True(row.IsActive);
    }

    /// <summary>
    /// Taklif hujjati va rasm — faqat yuklangan fayl yoki <c>https://</c>. <c>javascript:</c>
    /// havola ochiq sahifada xom <c>href</c> bo'lib, JWT turgan domenda ishga tushardi.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com/offer.pdf")]
    [InlineData("data:text/html,<b>x</b>")]
    public async Task Xavfli_taklif_havolasi_rad_etiladi(string offerUrl)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var slug = $"url-{SalesMarketingKit.Tag()}";
        var response = await admin.PostAsJsonAsync("/api/admin/surveys", new
        {
            name = "Havola", slug, offerUrl,
            showStudentFirstNameInput = true, showStudentLastNameInput = true,
            showStudentPhoneNumberInput = false, showStudentGradeInput = true, showStudentGenderInput = true,
        });
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"offerUrl\"", text, StringComparison.Ordinal);

        await using var db = NewDb();
        Assert.False(await db.Surveys.AnyAsync(x => x.Slug == slug));
    }

    /// <summary>Bir xil telefon, BOSHQA bola — ikkinchi farzand, ikkinchi lid (§2.6).</summary>
    [Fact]
    public async Task Bir_xil_telefon_boshqa_bola_yangi_lid_yaratadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"aka-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        foreach (var child in new[] { "Ali", "Vali" })
        {
            var response = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
                SalesMarketingKit.Form(studentFirst: child), SalesMarketingKit.NextIp());
            await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);
        }

        await using var db = NewDb();
        var names = await db.Leads.AsNoTracking().Where(l => l.SurveyId == survey.Id)
            .Select(l => l.FullName).OrderBy(n => n).ToListAsync();
        Assert.Equal(["Ali Karimov", "Vali Karimov"], names);
        Assert.Equal(2, await db.SurveySubmissions.CountAsync(
            s => s.SurveyId == survey.Id && s.Status == SurveySubmissionStatus.Lead));
    }

    /// <summary>
    /// Takror oynasi ARIZA bo'yicha: o'sha oila BOSHQA arizani (masalan yozgi
    /// lager) to'ldirsa — bu yangi murojaat, yangi lid.
    /// </summary>
    [Fact]
    public async Task Boshqa_arizadagi_bir_xil_oila_takror_hisoblanmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var qabul = await SalesMarketingKit.CreateSurveyAsync(admin, $"qabul-{SalesMarketingKit.Tag()}");
        var lager = await SalesMarketingKit.CreateSurveyAsync(admin, $"lager-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();

        foreach (var survey in new[] { qabul, lager })
        {
            var response = await SalesMarketingKit.PublicPostAsync(
                anon, survey.Slug, SalesMarketingKit.Form(), SalesMarketingKit.NextIp());
            await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);
        }

        Assert.Equal((1, 1), await SalesMarketingKit.CountsAsync(fixture, qabul.Id));
        Assert.Equal((1, 1), await SalesMarketingKit.CountsAsync(fixture, lager.Id));
    }

    // =====================================================================
    //  7. Topshiriq → lid (§2.4 jadvali, §2.6)
    // =====================================================================

    /// <summary>
    /// §2.4 moslash jadvali maydonma-maydon: lid, topshiriq qatori, Rule N izohi,
    /// <c>Source = "survey"</c>, sozlangan bosqich (Rule S 1-qadami), IP
    /// (<c>X-Forwarded-For</c> dan, qo'lda o'qilmagan) va brauzer satri. Anonim
    /// topshiriq audit qatori YOZMAYDI (§2.6 "Audit").
    /// </summary>
    [Fact]
    public async Task Ariza_lidni_togri_maydonlarga_yozadi()
    {
        var tag = SalesMarketingKit.Tag();
        var stageId = await SalesMarketingKit.NewStageAsync(fixture, $"Ariza ustuni {tag}");
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"map-{tag}",
            name: $"2027 qabul {tag}", stageId: stageId, studentPhone: true);
        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();

        var startedLocal = AppClock.Now;
        var response = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(
                parentFirst: "  Aziz ", parentLast: "Karimov", parentPhone: "+998 90 123 45 67",
                studentFirst: "Ali", studentLast: "Karimov", studentPhone: "+998 91 765 43 21",
                grade: 5, gender: "female"),
            ip, SalesMarketingKit.TestUserAgent);
        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        Assert.Equal("Ali Karimov", lead.FullName);
        Assert.Equal("female", lead.Gender);
        Assert.Equal("", lead.BirthDate);
        Assert.Equal("Aziz Karimov", lead.ParentFullName);
        Assert.Equal("+998 90 123 45 67", lead.ParentPhone);
        Assert.Equal(5, lead.TargetGrade);
        Assert.Equal(stageId, lead.Stage);
        Assert.Equal(LeadSource.Survey, lead.Source);
        Assert.Equal(survey.Id, lead.SurveyId);
        Assert.NotNull(lead.CreatedAt);
        Assert.InRange(lead.CreatedAt!.Value, startedLocal.AddMinutes(-1), AppClock.Now.AddMinutes(1));

        // Rule N: bitta satr, id/slug yo'q, sana maktab soatida.
        SalesMarketingKit.AssertRuleNNote(lead.Note, $"2027 qabul {tag}", "+998 91 765 43 21", startedLocal);
        Assert.DoesNotContain(survey.Slug, lead.Note!, StringComparison.Ordinal);
        Assert.DoesNotContain(survey.Id.ToString(), lead.Note!, StringComparison.OrdinalIgnoreCase);

        var submission = await db.SurveySubmissions.AsNoTracking().SingleAsync(s => s.SurveyId == survey.Id);
        Assert.Equal(SurveySubmissionStatus.Lead, submission.Status);
        Assert.Equal(lead.Id, submission.LeadId);
        Assert.Equal("Aziz", submission.ParentFirstName);
        Assert.Equal("Karimov", submission.ParentLastName);
        Assert.Equal("+998 90 123 45 67", submission.ParentPhone);
        Assert.Equal("901234567", submission.ParentPhoneKey);
        Assert.Equal("Ali", submission.StudentFirstName);
        Assert.Equal("Karimov", submission.StudentLastName);
        Assert.Equal("+998 91 765 43 21", submission.StudentPhone);
        Assert.Equal((short)5, submission.StudentGrade);
        Assert.Equal("female", submission.StudentGender);
        Assert.Equal(ip, submission.Ip);
        Assert.Equal(SalesMarketingKit.TestUserAgent, submission.UserAgent);

        // Anonim topshiriq audit jurnaliga yozmaydi — `survey_submissions` qatori o'zi yozuv.
        Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(
            a => a.EntityId == lead.Id || a.EntityId == submission.Id.ToString()));
    }

    /// <summary>
    /// Ism tugmasi o'chiq arizada lid <c>"{ota-ona} — farzandi"</c> nomi bilan
    /// yaratiladi (§2.4, Rule T). O'chiq tugmaning qiymati qo'lda yasalgan
    /// so'rovda kelsa ham YOZILMAYDI — forma so'ramagan narsani hech kim
    /// yozib keta olmaydi.
    /// </summary>
    [Fact]
    public async Task Ismsiz_ariza_ota_ona_nomi_bilan_lid_yaratadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"ismsiz-{SalesMarketingKit.Tag()}",
            firstName: false, lastName: false, studentPhone: false);
        using var anon = fixture.Api.AnonymousClient();

        var response = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(parentFirst: "Aziz", parentLast: "Karimov",
                studentFirst: "Yashirin", studentLast: "Qiymat", studentPhone: "+998 91 000 00 00"),
            SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYou);

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        Assert.Equal("Aziz Karimov — farzandi", lead.FullName);
        Assert.DoesNotContain("O'quvchi tel", lead.Note!, StringComparison.Ordinal);

        var submission = await db.SurveySubmissions.AsNoTracking().SingleAsync(s => s.SurveyId == survey.Id);
        Assert.Null(submission.StudentFirstName);
        Assert.Null(submission.StudentLastName);
        Assert.Null(submission.StudentPhone);
    }

    // =====================================================================
    //  8. Rule T — admin saqlashi (§2.4)
    // =====================================================================

    /// <summary>
    /// Jins yoki sinf tugmasini o'chirgan ariza SAQLANMAYDI: 400
    /// <c>survey_fields_required</c>, <c>fields</c> da AYNAN o'chirilganlari,
    /// xabarda faqat ularning nomi. Bazada qator paydo bo'lmaydi.
    /// </summary>
    [Theory]
    [InlineData(false, true, "showStudentGenderInput", "Bu maydonlarni o'chirib bo'lmaydi: jins")]
    [InlineData(true, false, "showStudentGradeInput", "Bu maydonlarni o'chirib bo'lmaydi: sinf")]
    [InlineData(false, false, "showStudentGenderInput,showStudentGradeInput", "Bu maydonlarni o'chirib bo'lmaydi: jins, sinf")]
    public async Task Jins_va_sinf_ochirilgan_ariza_saqlanmaydi(
        bool gender, bool grade, string expectedFields, string expectedMessage)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var slug = $"rulet-{SalesMarketingKit.Tag()}";

        var response = await admin.PostAsJsonAsync(SalesMarketingKit.AdminSurveys,
            SalesMarketingKit.SurveyBody(slug, $"Rule T {slug}", gender: gender, grade: grade));

        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("survey_fields_required", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(expectedFields.Split(','),
            doc.RootElement.GetProperty("fields").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(expectedMessage, doc.RootElement.GetProperty("message").GetString());

        await using var db = NewDb();
        Assert.False(await db.Surveys.AnyAsync(s => s.Slug == slug));
    }

    /// <summary>Tahrirda ham xuddi shunday — mavjud arizaning tugmalari o'zgarmaydi.</summary>
    [Fact]
    public async Task Tahrirda_sinf_tugmasini_ochirib_bolmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"rulet-{SalesMarketingKit.Tag()}");

        var response = await admin.PutAsJsonAsync($"{SalesMarketingKit.AdminSurveys}/{survey.Id}",
            SalesMarketingKit.SurveyBody(survey.Slug, "Boshqa nom", grade: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("survey_fields_required", doc.RootElement.GetProperty("code").GetString());

        await using var db = NewDb();
        var row = await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        Assert.True(row.ShowStudentGradeInput);
        Assert.NotEqual("Boshqa nom", row.Name);
    }

    // =====================================================================
    //  9. Ommaviy GET hech kim haqida hech narsa aytmaydi (§5.1)
    // =====================================================================

    /// <summary>
    /// GET javobida AYNAN sahifa chizadigan sakkizta maydon bor — id yo'q,
    /// bosqich yo'q, muallif yo'q. Oldin tushgan arizaning lidi, o'quvchisi,
    /// telefoni va hech bir mavjud yozuvning id'si javobda uchramaydi.
    /// </summary>
    [Fact]
    public async Task Ommaviy_endpoint_boshqa_malumot_qaytarmaydi()
    {
        var tag = SalesMarketingKit.Tag();
        var stageId = await SalesMarketingKit.NewStageAsync(fixture, $"Maxfiy ustun {tag}");
        var (creator, _) = await fixture.Api.SeedUserAsync(Roles.Admin, fullName: $"Maxfiy Xodim {tag}");
        using var admin = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Admin, creator.Id, creator.FullName, creator.Email));
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"ochiq-{tag}", stageId: stageId);
        using var anon = fixture.Api.AnonymousClient();
        var ip = SalesMarketingKit.NextIp();

        var submitted = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(parentFirst: $"Sirli{tag}", studentFirst: $"Bola{tag}",
                parentPhone: "+998 97 111 22 33"), ip);
        await SalesMarketingKit.AssertAcceptedAsync(submitted, survey.ThankYou);

        var response = await SalesMarketingKit.PublicGetAsync(anon, survey.Slug, ip);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);

        using var doc = JsonDocument.Parse(text);
        Assert.Equal(
            ["fields", "imageUrl", "name", "offerUrl", "servedAt", "slug", "subtitle", "thankYouText"],
            doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        var fields = doc.RootElement.GetProperty("fields");
        Assert.Equal(
            ["studentFirstName", "studentGender", "studentGrade", "studentLastName", "studentPhone"],
            fields.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.True(fields.GetProperty("studentGender").GetBoolean());
        Assert.True(fields.GetProperty("studentGrade").GetBoolean());
        Assert.Equal(survey.Slug, doc.RootElement.GetProperty("slug").GetString());

        var servedAt = doc.RootElement.GetProperty("servedAt").GetDateTimeOffset();
        Assert.InRange(servedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        foreach (var secret in new[]
                 {
                     survey.Id.ToString(), stageId, creator.Id, creator.FullName, lead.Id,
                     $"Sirli{tag}", $"Bola{tag}", "97 111 22 33", "971112233",
                 })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<(int Leads, int Submissions)> GlobalCountsAsync()
    {
        await using var db = NewDb();
        return (await db.Leads.CountAsync(), await db.SurveySubmissions.CountAsync());
    }

    private static async Task AssertIdentical404Async(HttpResponseMessage a, HttpResponseMessage b)
    {
        Assert.Equal(HttpStatusCode.NotFound, a.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, b.StatusCode);

        var bytesA = await a.Content.ReadAsByteArrayAsync();
        var bytesB = await b.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytesA, bytesB);
        Assert.Equal(a.Content.Headers.ContentType?.ToString(), b.Content.Headers.ContentType?.ToString());

        using var doc = JsonDocument.Parse(bytesA);
        Assert.Equal("survey_not_found", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Bu ariza topilmadi yoki yopilgan", doc.RootElement.GetProperty("message").GetString());
    }

    private static async Task<Dictionary<string, string>> AssertValidationAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("validation", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Ma'lumotlarni tekshiring", doc.RootElement.GetProperty("message").GetString());
        return doc.RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
    }
}

// ===========================================================================
//  Savdo va marketing testlarining UMUMIY yordamchilari. To'rtta HTTP fayli
//  (shu fayl, SurveysTests, NewsTests, SalesMarketingRbacTests) ishlatadi.
//
//  Eng muhimi — `NextIp()`: chastota chegarasi chelagi IP bo'yicha va butun
//  test yurishi uchun bitta ilova ichida yashaydi. Takrorlanmaslik faqat
//  BITTA hisoblagich bilan kafolatlanadi — shuning uchun u shu yerda, bitta
//  joyda.
// ===========================================================================

internal static class SalesMarketingKit
{
    public const string PublicSurveys = "/api/public/surveys";
    public const string AdminSurveys = "/api/admin/surveys";
    public const string TestUserAgent = "WunderkindTests/1.0 (+sales-marketing)";

    private static int _lastOctet;

    /// <summary>
    /// Butun test yurishi davomida takrorlanmaydigan <c>203.0.113.&lt;n&gt;</c>
    /// (TEST-NET-3, RFC 5737). 254 tadan oshsa — aniq xabar bilan yiqiladi,
    /// jimgina bir chelakni ikki test bo'lishmaydi.
    /// </summary>
    public static string NextIp()
    {
        var n = Interlocked.Increment(ref _lastOctet);
        Assert.True(n <= 254,
            $"203.0.113.0/24 manzillari tugadi ({n}). Chastota chegarasi chelaklari bo'lishilib qoladi — "
            + "ajratishni boshqa TEST-NET (198.51.100.0/24) ga kengaytiring.");
        return $"203.0.113.{n}";
    }

    public static string Tag() => Guid.NewGuid().ToString("N")[..8];

    public static string MinutesAgo(int minutes) =>
        DateTimeOffset.UtcNow.AddMinutes(-minutes).ToString("o", CultureInfo.InvariantCulture);

    /// <summary>
    /// To'g'ri to'ldirilgan ommaviy forma (§5.1). <c>servedAt</c> sukut bo'yicha
    /// 5 daqiqa oldin — odam to'ldirgan forma. Lug'at: test kalitni olib
    /// tashlashi ("maydon umuman kelmadi") yoki almashtirishi mumkin.
    /// </summary>
    public static Dictionary<string, object?> Form(
        string parentFirst = "Aziz",
        string? parentLast = "Karimov",
        string parentPhone = "+998 90 123 45 67",
        string? studentFirst = "Ali",
        string? studentLast = "Karimov",
        string? studentPhone = "",
        int? grade = 5,
        string? gender = "male",
        string? servedAt = null,
        string website = "") => new(StringComparer.Ordinal)
    {
        ["parentFirstName"] = parentFirst,
        ["parentLastName"] = parentLast,
        ["parentPhone"] = parentPhone,
        ["studentFirstName"] = studentFirst,
        ["studentLastName"] = studentLast,
        ["studentPhone"] = studentPhone,
        ["studentGrade"] = grade,
        ["studentGender"] = gender,
        ["servedAt"] = servedAt ?? MinutesAgo(5),
        ["website"] = website,
    };

    public static async Task<HttpResponseMessage> PublicGetAsync(HttpClient client, string slug, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PublicSurveys}/{slug}");
        request.Headers.Add("X-Forwarded-For", ip);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PublicPostAsync(
        HttpClient client, string slug, object body, string ip, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{PublicSurveys}/{slug}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Forwarded-For", ip);
        if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Muvaffaqiyat tanasi (§5.1): <c>{ ok: true, thankYou }</c>. Xom matn
    /// qaytadi — to'rt holat (haqiqiy, takror, honeypot, vaqt tuzog'i)
    /// baytma-bayt solishtirilishi uchun.
    /// </summary>
    public static async Task<string> AssertAcceptedAsync(HttpResponseMessage response, string? expectedThankYou)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(expectedThankYou, doc.RootElement.GetProperty("thankYou").GetString());
        return text;
    }

    public static object SurveyBody(
        string slug, string name, string? stageId = null,
        bool firstName = true, bool lastName = true, bool studentPhone = false,
        bool grade = true, bool gender = true, string? thankYou = null) => new
    {
        name,
        slug,
        subtitle = "Ariza qoldiring — biz bog'lanamiz",
        imageUrl = (string?)null,
        offerUrl = (string?)null,
        thankYouText = thankYou,
        stageId,
        showStudentFirstNameInput = firstName,
        showStudentLastNameInput = lastName,
        showStudentPhoneNumberInput = studentPhone,
        showStudentGradeInput = grade,
        showStudentGenderInput = gender,
    };

    public sealed record CreatedSurvey(Guid Id, string Slug, string Name, string ThankYou, JsonElement Body);

    /// <summary>Admin API orqali ariza yaratadi — mahsulot qanday qilsa, test ham shunday.</summary>
    public static async Task<CreatedSurvey> CreateSurveyAsync(
        HttpClient admin, string slug, string? name = null, string? stageId = null,
        bool firstName = true, bool lastName = true, bool studentPhone = false)
    {
        name ??= $"Ariza {slug}";
        var thankYou = $"Rahmat — {slug}";
        var response = await admin.PostAsJsonAsync(AdminSurveys,
            SurveyBody(slug, name, stageId, firstName, lastName, studentPhone, thankYou: thankYou));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var body = doc.RootElement.Clone();
        return new CreatedSurvey(body.GetProperty("id").GetGuid(), body.GetProperty("slug").GetString()!,
            name, thankYou, body);
    }

    /// <summary>
    /// Yangi kanban ustuni. <c>Order</c> ATAYLAB katta: umumiy bazada Rule S
    /// 2-qadamining ("eng kichik Order") natijasini o'zgartirib qo'ymasin.
    /// </summary>
    public static async Task<string> NewStageAsync(ApiFixture fixture, string title)
    {
        var stage = new LeadStage { Title = title, Color = "violet", Order = 900 };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.LeadStages.Add(stage);
            await db.SaveChangesAsync();
        });
        return stage.Id;
    }

    public static async Task<(int Leads, int Submissions)> CountsAsync(ApiFixture fixture, Guid surveyId)
    {
        await using var db = PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);
        return (await db.Leads.CountAsync(l => l.SurveyId == surveyId),
            await db.SurveySubmissions.CountAsync(s => s.SurveyId == surveyId));
    }

    /// <summary>
    /// Rule N (§2.6): <c>Ariza: {nom} · {DD.MM.YYYY HH:mm}[ · O'quvchi tel: {tel}]</c>,
    /// BITTA satr. Daqiqa test davomida almashishi mumkin — shuning uchun vaqt
    /// aniq satr bilan emas, oraliq bilan tekshiriladi.
    /// </summary>
    public static void AssertRuleNNote(string? note, string surveyName, string? studentPhone, DateTime startedLocal)
    {
        Assert.NotNull(note);
        Assert.DoesNotContain('\n', note);
        Assert.DoesNotContain('\r', note);

        var prefix = $"Ariza: {surveyName} · ";
        Assert.StartsWith(prefix, note, StringComparison.Ordinal);
        var rest = note[prefix.Length..];
        Assert.True(rest.Length >= 16, $"Izohda sana yo'q: '{note}'");

        var stamp = DateTime.ParseExact(rest[..16], "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        var floor = startedLocal.AddSeconds(-startedLocal.Second).AddMilliseconds(-startedLocal.Millisecond);
        Assert.InRange(stamp, floor.AddMinutes(-1), AppClock.Now.AddMinutes(1));

        var tail = rest[16..];
        Assert.Equal(studentPhone is null ? "" : $" · O'quvchi tel: {studentPhone}", tail);
    }
}
