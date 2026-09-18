using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Jadval ko'rinishi sozlamalari — docs/modules/students-parity.md §2.11 (X-1).
///
/// <para>
/// <b>Diqqat markazi — "faqat o'zinikini".</b> So'rovda boshqa foydalanuvchining
/// id'si UMUMAN qabul qilinmaydi (userId — tokendan), shuning uchun IDOR
/// testi shu yerda "begona so'rov 403" emas, balki "ikki xodim BIR XIL
/// sahifa kaliti bilan MUSTAQIL yozadi va bir-birining qatorini ko'rmaydi/
/// buzmaydi" ko'rinishida.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class UserTableSettingsTests(ApiFixture fixture)
{
    private const string Base = "/api/user-table-settings";
    private static string Url(string page) => $"{Base}/{page}";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        var page = Page();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url(page))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync(Url(page), new { settings = new { } })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Admin_panelidan_tashqari_rollar_403(string role)
    {
        // `ClientAsAsync` foydalanuvchini BAZAGA ham yozadi. Tasodifiy id bilan
        // qo'lda yasalgan token bu yerda 401 berardi (tokenni tekshirish
        // bosqichi foydalanuvchini topa olmaydi) — ya'ni test rolni emas,
        // mavjud emaslikni tekshirgan bo'lardi.
        using var client = await fixture.Api.ClientAsAsync(role);
        var page = Page();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url(page))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(Url(page), new { settings = new { } })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.Staff)]
    public async Task Admin_panel_rollari_erkin_foydalanadi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var page = Page();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url(page))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync(Url(page), new { settings = new { hidden = new[] { "x" } } }))
                .StatusCode);
    }

    // =====================================================================
    //  2. SAQLASH / O'QISH
    // =====================================================================

    [Fact]
    public async Task Sozlanmagan_sahifa_bosh_royxat_bilan_200_qaytadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);
        var page = Page();

        var response = await client.GetAsync(Url(page));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await JsonAsync(response);
        Assert.Equal(page, body.GetProperty("page").GetString());
        Assert.Equal(JsonValueKind.Object, body.GetProperty("settings").ValueKind);
        Assert.Equal(0, body.GetProperty("settings").EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Saqlash_va_oqish_royxatga_tushadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var page = Page();

        var settings = new
        {
            order = new[] { "name", "subject", "classes" },
            hidden = new[] { "subject" },
            pinned = new[] { "name" },
        };

        var saved = await client.PutAsJsonAsync(Url(page), new { settings });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedBody = await JsonAsync(saved);
        Assert.NotEqual(JsonValueKind.Null, savedBody.GetProperty("updatedAt").ValueKind);

        var loaded = await JsonAsync(await client.GetAsync(Url(page)));
        var order = loaded.GetProperty("settings").GetProperty("order")
            .EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(new[] { "name", "subject", "classes" }, order);
        var hidden = loaded.GetProperty("settings").GetProperty("hidden")
            .EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(new[] { "subject" }, hidden);

        // Qayta saqlash — TO'LIQ almashtiradi, eskisi qolmaydi.
        var overwritten = await client.PutAsJsonAsync(Url(page),
            new { settings = new { order = new[] { "classes" } } });
        Assert.Equal(HttpStatusCode.OK, overwritten.StatusCode);
        var afterOverwrite = await JsonAsync(await client.GetAsync(Url(page)));
        Assert.False(afterOverwrite.GetProperty("settings").TryGetProperty("hidden", out _));
    }

    /// <summary>Bo'sh sahifa kaliti — 400 (bazadagi <c>btrim(page) &lt;&gt; ''</c> CHECK'ining oldi).</summary>
    [Fact]
    public async Task Bosh_sahifa_kaliti_400()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Ikkala holatni ham MARSHRUTLASH rad etadi, amal ishga tushmaydi:
        // bo'sh segment mos kelmaydi (404), faqat probeldan iborati esa 400.
        // Shuning uchun javob TANASI bizniki emas — controller'dagi
        // `PageRequiredMessage` himoyasi joyida qoladi (amal baribir bo'sh
        // kalitni qabul qilmasligi kerak), lekin uni bu yerdan tekshirib
        // bo'lmaydi: test o'z kodimizni emas, framework javobini o'qigan bo'lardi.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"{Base}/%20")).StatusCode);
    }

    // =====================================================================
    //  3. "FAQAT O'ZINIKI" — bitta sahifa kaliti, ikki foydalanuvchi
    // =====================================================================

    /// <summary>
    /// Ikki xodim AYNAN BIR XIL sahifa kaliti bilan mustaqil ishlaydi: har
    /// birining saqlagani faqat o'ziga qaytadi, boshqasiga taъsir qilmaydi va
    /// undan ko'rinmaydi. So'rovda "kimning qatori" degan parametr yo'qligi
    /// sababli bu holat IDOR'ning o'zi imkonsiz ekanini isbotlaydi.
    /// </summary>
    [Fact]
    public async Task Har_foydalanuvchi_faqat_ozining_qatorini_kor_va_yozadi()
    {
        using var alice = await fixture.Api.ClientAsAsync(Roles.Staff);
        using var bob = await fixture.Api.ClientAsAsync(Roles.Staff);
        var page = Page(); // ikkalasi ham AYNAN bir xil sahifa kaliti

        // Boshida — ikkalasi ham bo'sh.
        Assert.Equal(0, (await JsonAsync(await alice.GetAsync(Url(page))))
            .GetProperty("settings").EnumerateObject().Count());
        Assert.Equal(0, (await JsonAsync(await bob.GetAsync(Url(page))))
            .GetProperty("settings").EnumerateObject().Count());

        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(Url(page),
            new { settings = new { pinned = new[] { "alice-ustuni" } } })).StatusCode);

        // Bob HALI YOZMAGAN — Alice yozgani unga sira ko'rinmaydi.
        var bobAfterAliceWrote = await JsonAsync(await bob.GetAsync(Url(page)));
        Assert.Equal(0, bobAfterAliceWrote.GetProperty("settings").EnumerateObject().Count());

        Assert.Equal(HttpStatusCode.OK, (await bob.PutAsJsonAsync(Url(page),
            new { settings = new { pinned = new[] { "bob-ustuni" } } })).StatusCode);

        // Ikkalasi ham FAQAT o'zinikini o'qiydi.
        var aliceFinal = await JsonAsync(await alice.GetAsync(Url(page)));
        Assert.Equal("alice-ustuni",
            aliceFinal.GetProperty("settings").GetProperty("pinned")[0].GetString());

        var bobFinal = await JsonAsync(await bob.GetAsync(Url(page)));
        Assert.Equal("bob-ustuni",
            bobFinal.GetProperty("settings").GetProperty("pinned")[0].GetString());
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Page() => "test.page." + Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

}
