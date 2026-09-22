using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Savdo va marketing — ROL CHEGARALARI, haqiqiy HTTP so'rovlar bilan.
//  docs/modules/sales-marketing.md §8.1 (jadval), §5.2–§5.5 (darvozalar).
// ===========================================================================
//
//  NEGA IKKALA YARMI HAM
//  ---------------------
//  `AdminPermAttribute` xodimga (staff) GET ni HAR DOIM ochadi va faqat
//  YOZISHni `marketing` claim'iga bog'laydi. Ya'ni har bir endpoint'da ikki
//  xil xato bo'lishi mumkin: o'qish yopilib qolgan (boshqa bo'lim ekrani
//  buziladi) yoki yozish ochilib qolgan (e'lonni istalgan xodim yuboradi).
//  Jadval ikkalasini ham ushlaydi.
//
//  SHAKL — `RbacMatrixTests` DAN
//  -----------------------------
//  Har qator — bitta `[Theory]`, har rol — bitta `[InlineData]`: kutilgan
//  status HAR katakda ko'rinib turadi va ro'yxat bo'ylab aylanadigan sikl
//  jimgina nolga qisqarib qololmaydi. §8.1 dagi oltita ustunga `parent` va
//  `student` ham qo'shildi: `AdminPermAttribute` ularni "outright" rad
//  etishi kerak, o'quvchi lentasida esa ular yagona haqiqiy o'quvchilar.
//
//  HAR KATAK TANANI HAM TEKSHIRADI
//  -------------------------------
//  * Rad etilgan katak (401/403) — tana BO'SH va bazada iz YO'Q (403 qaytib,
//    yozuv baribir tushib qolishi — eng yomon holat).
//  * Ruxsat etilgan katak — javobda aynan SHU test yaratgan yozuv bor va
//    bazadagi holat o'zgargan.
//
//  O'QUVCHI LENTALARI — AUDITORIYA OQIB KETMASLIGI (§8.1 oxirgi xatboshi)
//  ---------------------------------------------------------------------
//  Ota-ona faqat `for_parent`, o'quvchi faqat `for_student`, o'qituvchi faqat
//  `for_employee` yangilikni ko'radi. Har bir ruxsatli katak BOSHQA
//  auditoriyaning yangiligi javobda YO'QLIGINI ham tekshiradi.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class SalesMarketingRbacTests(ApiFixture fixture)
{
    private const string Anonim = "anonim";
    private const string StaffMarketing = "staff+marketing";
    private const string Parent = "parent";

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  §5.2 — /api/admin/surveys
    // =====================================================================

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Arizalar_royxati_GET(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync("/api/admin/surveys?includeInactive=true");

        if (await RefusedAsync(response, expected)) return;
        var row = Assert.Single(await ArrayAsync(response), r => r.GetProperty("id").GetGuid() == survey.Id);
        Assert.Equal(survey.Slug, row.GetProperty("slug").GetString());
        Assert.Equal(survey.Name, row.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Slug_bandligi_GET(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync($"/api/admin/surveys/slug-available?slug={survey.Slug}");

        if (await RefusedAsync(response, expected)) return;
        // Band slug — `false`. Bazaga qaramagan javob `true` berardi.
        using var doc = await JsonAsync(response);
        Assert.False(doc.RootElement.GetProperty("available").GetBoolean());
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Ariza_yaratish_POST(string who, HttpStatusCode expected)
    {
        using var actor = await ActorAsync(who);
        var slug = $"rbac-yangi-{SalesMarketingKit.Tag()}";

        var response = await actor.Client.PostAsJsonAsync("/api/admin/surveys",
            SalesMarketingKit.SurveyBody(slug, $"RBAC {slug}"));

        await using var db = NewDb();
        var row = await db.Surveys.AsNoTracking().SingleOrDefaultAsync(s => s.Slug == slug);
        if (await RefusedAsync(response, expected))
        {
            Assert.Null(row);
            return;
        }

        using var doc = await JsonAsync(response);
        Assert.Equal(slug, doc.RootElement.GetProperty("slug").GetString());
        Assert.NotNull(row);
        Assert.Equal(row.Id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(actor.User!.Id, row.CreatedBy);
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Ariza_tahrirlash_PUT(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        using var actor = await ActorAsync(who);
        var newName = $"Tahrirlangan {SalesMarketingKit.Tag()}";

        var response = await actor.Client.PutAsJsonAsync($"/api/admin/surveys/{survey.Id}",
            SalesMarketingKit.SurveyBody(survey.Slug, newName));

        await using var db = NewDb();
        var row = await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.Equal(survey.Name, row.Name);
            return;
        }

        using var doc = await JsonAsync(response);
        Assert.Equal(newName, doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(newName, row.Name);
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.NoContent)]
    [InlineData("admin", HttpStatusCode.NoContent)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Arizani_yopish_PATCH_active(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.PatchAsJsonAsync($"/api/admin/surveys/{survey.Id}/active", new { isActive = false });

        await using var db = NewDb();
        var row = await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.True(row.IsActive);
            return;
        }

        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.False(row.IsActive);
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.NoContent)]
    [InlineData("admin", HttpStatusCode.NoContent)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Arizani_ochirish_DELETE(string who, HttpStatusCode expected)
    {
        // Topshiriqsiz ariza — 204 katagi. 409 holati SurveysTests da.
        var survey = await SeedSurveyAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.DeleteAsync($"/api/admin/surveys/{survey.Id}");

        await using var db = NewDb();
        var exists = await db.Surveys.AnyAsync(s => s.Id == survey.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.True(exists);
            return;
        }

        Assert.False(exists);
    }

    // =====================================================================
    //  §5.3 — /api/admin/survey-submissions
    // =====================================================================

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Topshiriqlar_royxati_GET(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        var submission = await SeedSubmissionAsync(survey);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync($"/api/admin/survey-submissions?surveyId={survey.Id}");

        if (await RefusedAsync(response, expected)) return;
        using var doc = await JsonAsync(response);
        Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray().ToList());
        Assert.Equal(submission.Id, row.GetProperty("id").GetGuid());
        Assert.Equal(submission.ParentPhone, row.GetProperty("parentPhone").GetString());
        Assert.Equal(survey.Name, row.GetProperty("surveyName").GetString());
        // IP va brauzer satri faqat detal panelida (§4.2) — ro'yxatda YO'Q.
        Assert.False(row.TryGetProperty("ip", out _));
        Assert.False(row.TryGetProperty("userAgent", out _));
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Topshiriqlar_eksporti_GET(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        var submission = await SeedSubmissionAsync(survey);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync($"/api/admin/survey-submissions/export?surveyId={survey.Id}");

        if (await RefusedAsync(response, expected)) return;
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var cells = XlsxCells(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, cells.Count); // sarlavha + shu arizaning yagona topshirig'i
        Assert.Equal(survey.Name, cells[1][1]);
        Assert.Equal(submission.ParentPhone, cells[1][3]);
        // Eksportda IP ham, brauzer satri ham yo'q (§4.2, §5.3).
        var everything = string.Join("|", cells.SelectMany(r => r));
        Assert.DoesNotContain(submission.Ip!, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(submission.UserAgent!, everything, StringComparison.Ordinal);
    }

    // =====================================================================
    //  §5.4 — /api/admin/news
    // =====================================================================

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Yangiliklar_royxati_GET(string who, HttpStatusCode expected)
    {
        var news = await SeedNewsAsync(published: false, parent: true);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync("/api/admin/news?state=draft&pageSize=200");

        if (await RefusedAsync(response, expected)) return;
        using var doc = await JsonAsync(response);
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray().ToList(),
            r => r.GetProperty("id").GetGuid() == news.Id);
        Assert.Equal(news.Title, row.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Yangilik_yozish_POST(string who, HttpStatusCode expected)
    {
        using var actor = await ActorAsync(who);
        var title = $"RBAC yangilik {SalesMarketingKit.Tag()}";

        var response = await actor.Client.PostAsJsonAsync("/api/admin/news",
            new { title, body = "Ota-onalar yig'ilishi", imageUrl = (string?)null, audience = new[] { "parent" } });

        await using var db = NewDb();
        var row = await db.News.AsNoTracking().SingleOrDefaultAsync(n => n.Title == title);
        if (await RefusedAsync(response, expected))
        {
            Assert.Null(row);
            return;
        }

        using var doc = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("publishedAt").ValueKind);
        Assert.Equal(["parent"], doc.RootElement.GetProperty("audience").EnumerateArray().Select(a => a.GetString()!).ToArray());
        Assert.Equal(actor.User!.FullName, doc.RootElement.GetProperty("authorName").GetString());
        Assert.NotNull(row);
        Assert.Equal(actor.User.Id, row.AuthorId);
        Assert.Null(row.PublishedAt);
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Yangilikni_elon_qilish_POST_publish(string who, HttpStatusCode expected)
    {
        var news = await SeedNewsAsync(published: false, parent: true);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.PostAsJsonAsync($"/api/admin/news/{news.Id}/publish", new { sendTelegram = true });

        await using var db = NewDb();
        var row = await db.News.AsNoTracking().SingleAsync(n => n.Id == news.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.Null(row.PublishedAt);
            return;
        }

        using var doc = await JsonAsync(response);
        Assert.NotEqual(JsonValueKind.Null, doc.RootElement.GetProperty("publishedAt").ValueKind);
        Assert.NotNull(row.PublishedAt);
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Yangilikni_elondan_qaytarish_POST_unpublish(string who, HttpStatusCode expected)
    {
        var news = await SeedNewsAsync(published: true, parent: true);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.PostAsync($"/api/admin/news/{news.Id}/unpublish", null);

        await using var db = NewDb();
        var row = await db.News.AsNoTracking().SingleAsync(n => n.Id == news.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.NotNull(row.PublishedAt);
            return;
        }

        using var doc = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("publishedAt").ValueKind);
        Assert.Null(row.PublishedAt);
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.NoContent)]
    [InlineData("admin", HttpStatusCode.NoContent)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Yangilikni_ochirish_DELETE(string who, HttpStatusCode expected)
    {
        var news = await SeedNewsAsync(published: true, parent: true);
        using var actor = await ActorAsync(who);

        var response = await actor.Client.DeleteAsync($"/api/admin/news/{news.Id}");

        await using var db = NewDb();
        var row = await db.News.AsNoTracking().SingleAsync(n => n.Id == news.Id);
        if (await RefusedAsync(response, expected))
        {
            Assert.Null(row.DeletedAt);
            return;
        }

        Assert.NotNull(row.DeletedAt);
    }

    // =====================================================================
    //  §5.5 — o'quvchi lentalari
    // =====================================================================

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task Xodimlar_lentasi_GET_admin_news_feed(string who, HttpStatusCode expected)
    {
        var (employee, parent, _) = await SeedThreeAudiencesAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync("/api/admin/news/feed?take=50");

        if (await RefusedAsync(response, expected)) return;
        var ids = await FeedIdsAsync(response);
        Assert.Contains(employee.Id, ids);
        Assert.DoesNotContain(parent.Id, ids);
    }

    /// <summary>
    /// Sinf darvozasi <c>student,parent,admin</c> (<c>StudentPortalController</c>).
    /// Auditoriya ROLDAN: o'quvchi — <c>for_student</c>; ota-ona va admin —
    /// <c>for_parent</c>. Xodim (<c>marketing</c> bilan ham) — 403.
    /// </summary>
    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.OK)]
    [InlineData("student", HttpStatusCode.OK)]
    public async Task Portal_lentasi_GET_student_news(string who, HttpStatusCode expected)
    {
        var (employee, parent, student) = await SeedThreeAudiencesAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync("/api/student/news?take=50");

        if (await RefusedAsync(response, expected)) return;
        var ids = await FeedIdsAsync(response);
        Assert.DoesNotContain(employee.Id, ids);
        if (who == "student")
        {
            Assert.Contains(student.Id, ids);
            Assert.DoesNotContain(parent.Id, ids);
        }
        else
        {
            Assert.Contains(parent.Id, ids);
            Assert.DoesNotContain(student.Id, ids);
        }
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.Forbidden)]
    [InlineData(Parent, HttpStatusCode.OK)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task MiniApp_ota_ona_lentasi_GET_tg_parent_news(string who, HttpStatusCode expected)
    {
        var (employee, parent, student) = await SeedThreeAudiencesAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync("/api/tg/parent/news?take=50");

        if (await RefusedAsync(response, expected)) return;
        var ids = await FeedIdsAsync(response);
        Assert.Contains(parent.Id, ids);
        Assert.DoesNotContain(employee.Id, ids);
        Assert.DoesNotContain(student.Id, ids);
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.Unauthorized)]
    [InlineData("teacher", HttpStatusCode.OK)]
    [InlineData("cashier", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData(StaffMarketing, HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.Forbidden)]
    [InlineData(Parent, HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task MiniApp_oqituvchi_lentasi_GET_tg_teacher_news(string who, HttpStatusCode expected)
    {
        var (employee, parent, student) = await SeedThreeAudiencesAsync();
        using var actor = await ActorAsync(who);

        var response = await actor.Client.GetAsync("/api/tg/teacher/news?take=50");

        if (await RefusedAsync(response, expected)) return;
        var ids = await FeedIdsAsync(response);
        Assert.Contains(employee.Id, ids);
        Assert.DoesNotContain(parent.Id, ids);
        Assert.DoesNotContain(student.Id, ids);
    }

    // =====================================================================
    //  §5.1 — ommaviy endpoint'lar: HAMMAGA ochiq (token bo'lsa ham)
    // =====================================================================

    [Theory]
    [InlineData(Anonim, HttpStatusCode.OK)]
    [InlineData("teacher", HttpStatusCode.OK)]
    [InlineData("cashier", HttpStatusCode.OK)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.OK)]
    [InlineData("student", HttpStatusCode.OK)]
    public async Task Ommaviy_ariza_sahifasi_GET(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        using var actor = await ActorAsync(who);

        var response = await SalesMarketingKit.PublicGetAsync(actor.Client, survey.Slug, SalesMarketingKit.NextIp());

        Assert.Equal(expected, response.StatusCode);
        using var doc = await JsonAsync(response);
        Assert.Equal(survey.Slug, doc.RootElement.GetProperty("slug").GetString());
        Assert.Equal(survey.Name, doc.RootElement.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(Anonim, HttpStatusCode.OK)]
    [InlineData("teacher", HttpStatusCode.OK)]
    [InlineData("cashier", HttpStatusCode.OK)]
    [InlineData("staff", HttpStatusCode.OK)]
    [InlineData(StaffMarketing, HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData(Parent, HttpStatusCode.OK)]
    [InlineData("student", HttpStatusCode.OK)]
    public async Task Ommaviy_ariza_topshirish_POST(string who, HttpStatusCode expected)
    {
        var survey = await SeedSurveyAsync();
        using var actor = await ActorAsync(who);

        var response = await SalesMarketingKit.PublicPostAsync(
            actor.Client, survey.Slug, SalesMarketingKit.Form(), SalesMarketingKit.NextIp());

        Assert.Equal(expected, response.StatusCode);
        await SalesMarketingKit.AssertAcceptedAsync(response, survey.ThankYouText);

        await using var db = NewDb();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id);
        Assert.Equal(LeadSource.Survey, lead.Source);
    }

    // =====================================================================
    //  Muddati o'tgan token — har darvozada 401
    // =====================================================================

    /// <summary>
    /// Muddati o'tgan token (JWT'ning 5 daqiqalik skew'idan ham eski) — rolidan
    /// qat'i nazar 401, tana bo'sh. Rol shu endpoint'ga RUXSATI bor rol qilib
    /// tanlangan: 401 ni rol emas, aynan muddat bergani ko'rinsin.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/admin/surveys", "admin")]
    [InlineData("POST", "/api/admin/surveys", "admin")]
    [InlineData("GET", "/api/admin/survey-submissions", "admin")]
    [InlineData("POST", "/api/admin/news", "admin")]
    [InlineData("GET", "/api/admin/news/feed", "admin")]
    [InlineData("GET", "/api/student/news", Parent)]
    [InlineData("GET", "/api/tg/parent/news", Parent)]
    [InlineData("GET", "/api/tg/teacher/news", "teacher")]
    public async Task Muddati_otgan_token_401(string method, string url, string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(
            role, user.Id, user.FullName, user.Email, lifetime: TimeSpan.FromMinutes(-10)));
        var title = $"Eskirgan token {SalesMarketingKit.Tag()}";
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
            request.Content = JsonContent.Create(new
            {
                title, body = "Matn", audience = new[] { "parent" },
                name = title, slug = $"eski-{SalesMarketingKit.Tag()}",
                showStudentGradeInput = true, showStudentGenderInput = true,
            });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        await using var db = NewDb();
        Assert.False(await db.News.AnyAsync(n => n.Title == title));
        Assert.False(await db.Surveys.AnyAsync(s => s.Name == title));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed class Actor(HttpClient client, AppUser? user) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public AppUser? User { get; } = user;
        public void Dispose() => Client.Dispose();
    }

    /// <summary>
    /// Rol uchun haqiqiy foydalanuvchi va uning tokeni. <c>student</c> uchun
    /// <c>students</c> qatori ham bog'lanadi — aks holda <c>OnTokenValidated</c>
    /// tokenni bekor qiladi va katak 403 o'rniga 401 bo'lib qolardi.
    /// </summary>
    private async Task<Actor> ActorAsync(string who)
    {
        if (who == Anonim) return new Actor(fixture.Api.AnonymousClient(), null);

        var role = who == StaffMarketing ? Roles.Staff : who;
        var permissions = who == StaffMarketing ? new[] { "marketing" } : null;
        var (user, _) = await fixture.Api.SeedUserAsync(role, permissions: permissions);

        if (role == Roles.Student)
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                var pupil = GeneralSettingsFlagsTests.NewStudent(user.FullName, $"SM-{SalesMarketingKit.Tag()[..4]}", "+998900000077");
                pupil.UserId = user.Id;
                db.Students.Add(pupil);
                await db.SaveChangesAsync();
            });
        }

        return new Actor(
            fixture.Api.ClientWithToken(fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email)), user);
    }

    /// <summary>
    /// Rad etilgan katak bo'lsa — statusni, BO'SH tanani tekshirib <c>true</c>
    /// qaytaradi. Aks holda kutilgan muvaffaqiyat statusini tekshiradi.
    /// </summary>
    private static async Task<bool> RefusedAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Kutilgan {(int)expected}, keldi {(int)response.StatusCode}: {text}");
        if (expected is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Assert.Empty(text);
            return true;
        }
        return false;
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<List<JsonElement>> ArrayAsync(HttpResponseMessage response)
    {
        using var doc = await JsonAsync(response);
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<HashSet<Guid>> FeedIdsAsync(HttpResponseMessage response) =>
        [.. (await ArrayAsync(response)).Select(e => e.GetProperty("id").GetGuid())];

    private async Task<AppUser> AuthorAsync() => (await fixture.Api.SeedUserAsync(Roles.Admin)).User;

    private async Task<Survey> SeedSurveyAsync()
    {
        var tag = SalesMarketingKit.Tag();
        var author = await AuthorAsync();
        var survey = new Survey
        {
            Slug = $"rbac-{tag}",
            Name = $"RBAC ariza {tag}",
            ThankYouText = $"Rahmat {tag}",
            CreatedBy = author.Id,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Surveys.Add(survey);
            await db.SaveChangesAsync();
        });
        return survey;
    }

    private async Task<SurveySubmission> SeedSubmissionAsync(Survey survey)
    {
        var submission = new SurveySubmission
        {
            SurveyId = survey.Id,
            Status = SurveySubmissionStatus.Lead,
            ParentFirstName = "Rbac",
            ParentLastName = "Otasi",
            ParentPhone = "+998 90 555 44 33",
            ParentPhoneKey = "905554433",
            StudentFirstName = "Bola",
            StudentGrade = 3,
            StudentGender = SurveyGender.Male,
            Ip = "198.51.100.23",
            UserAgent = "RbacAgent/9.9",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.SurveySubmissions.Add(submission);
            await db.SaveChangesAsync();
        });
        return submission;
    }

    private async Task<NewsItem> SeedNewsAsync(
        bool published, bool employee = false, bool parent = false, bool student = false)
    {
        var author = await AuthorAsync();
        var news = new NewsItem
        {
            Title = $"RBAC yangilik {SalesMarketingKit.Tag()}",
            Body = "Matn",
            ForEmployee = employee,
            ForParent = parent,
            ForStudent = student,
            AuthorId = author.Id,
            AuthorName = author.FullName,
            PublishedAt = published ? DateTimeOffset.UtcNow : null,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.News.Add(news);
            await db.SaveChangesAsync();
        });
        return news;
    }

    /// <summary>Uchta e'lon qilingan yangilik — har biri FAQAT bitta auditoriyaga.</summary>
    private async Task<(NewsItem Employee, NewsItem Parent, NewsItem Student)> SeedThreeAudiencesAsync() =>
        (await SeedNewsAsync(published: true, employee: true),
         await SeedNewsAsync(published: true, parent: true),
         await SeedNewsAsync(published: true, student: true));

    /// <summary>.xlsx ning birinchi varag'idagi hamma katak matni, qatorma-qator.</summary>
    private static List<List<string>> XlsxCells(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, isEditable: false);
        var workbook = doc.WorkbookPart!;
        var sheet = Assert.Single(workbook.Workbook.Descendants<Sheet>());
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
        return part.Worksheet.Descendants<Row>()
            .Select(r => r.Elements<Cell>().Select(c => c.InnerText).ToList())
            .ToList();
    }
}
