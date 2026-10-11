using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Sent announcements in detail (client, 2026-10-11: "yuborilgan elonlar haqida batafsil malumot
/// chiqib turishi. masalan kimga") and formatted text ("bold, kursiv va boshqa funksiyalar").
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BroadcastDetailTests(ApiFixture fixture)
{
    [Fact]
    public async Task Elon_kimga_yetgani_va_kimga_yetmagani_saqlanadi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var className = $"BD-{tag}";
        var linked = GeneralSettingsFlagsTests.NewStudent($"Ulangan {tag}", className, "+998900000011");
        var offline = GeneralSettingsFlagsTests.NewStudent($"Ulanmagan {tag}", className, "+998900000012");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.AddRange(linked, offline);
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = linked.Id, ChatId = 7_000_001, ParentName = "Ona" });
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var sent = await admin.PostAsJsonAsync("/api/admin/messages/broadcast", new
        {
            scope = "class", className, onlyDebtors = false,
            text = "**Diqqat**, {fish}! <script>alert(1)</script> __Ertaga__",
        });
        var body = await sent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sent.IsSuccessStatusCode, body.ToString());
        Assert.Equal(2, body.GetProperty("students").GetInt32());
        Assert.Equal(1, body.GetProperty("noTelegram").GetInt32());
        var id = body.GetProperty("id").GetString();

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/messages/broadcasts/{id}");
        Assert.True(detail.GetProperty("hasDetails").GetBoolean());
        // Telegram-style marks become tags, the script stays text; the stored text is what was typed.
        Assert.Equal("**Diqqat**, {fish}! <script>alert(1)</script> __Ertaga__",
            detail.GetProperty("broadcast").GetProperty("text").GetString());
        Assert.Equal("<b>Diqqat</b>, {fish}! &lt;script&gt;alert(1)&lt;/script&gt; <i>Ertaga</i>",
            detail.GetProperty("html").GetString());
        var rows = detail.GetProperty("recipients").EnumerateArray()
            .ToDictionary(r => r.GetProperty("studentId").GetString()!, r => r.GetProperty("status").GetString());
        // The test API has no bot token: the registered parent is "failed", the other "no_telegram".
        Assert.Equal("failed", rows[linked.Id]);
        Assert.Equal("no_telegram", rows[offline.Id]);

        var list = await admin.GetFromJsonAsync<List<JsonElement>>($"/api/admin/messages/broadcasts?className={className}");
        Assert.Equal(1, Assert.Single(list!).GetProperty("noTelegram").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/messages/broadcasts/no-such")).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/admin/messages/broadcasts/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("<b>a</b> <i>b</i> <u>c</u> <s>d</s>", "<b>a</b> <i>b</i> <u>c</u> <s>d</s>")]
    [InlineData("<b>yopilmagan", "<b>yopilmagan</b>")]
    [InlineData("ochilmagan</b>", "ochilmagan")]
    [InlineData("<b><i>x</b></i>", "<b><i>x</i></b>")]
    [InlineData("5 < 6 & 7 > 3", "5 &lt; 6 &amp; 7 &gt; 3")]
    [InlineData("<img src=x onerror=alert(1)>", "&lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("<b onclick=\"x\">a</b>", "&lt;b onclick=\"x\"&gt;a")]
    [InlineData("<a href=\"https://wunderkind.uz\">sayt</a>", "<a href=\"https://wunderkind.uz\">sayt</a>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "x")]
    [InlineData("<tg-spoiler>sir</tg-spoiler>", "<tg-spoiler>sir</tg-spoiler>")]
    public void Html_tozalanadi(string input, string expected) =>
        Assert.Equal(expected, TelegramHtml.Sanitize(input));

    [Theory]
    [InlineData("**qalin** va __kursiv__", "<b>qalin</b> va <i>kursiv</i>")]
    [InlineData("++tag++ ~~chiz~~ ||sir||", "<u>tag</u> <s>chiz</s> <tg-spoiler>sir</tg-spoiler>")]
    [InlineData("`**kod**` ichida yo'q", "<code>**kod**</code> ichida yo'q")]
    [InlineData("[sayt](https://wunderkind.uz) va [yomon](javascript:x)", "<a href=\"https://wunderkind.uz\">sayt</a> va [yomon](javascript:x)")]
    [InlineData("> birinchi\n> ikkinchi\noddiy", "<blockquote>birinchi\nikkinchi</blockquote>\noddiy")]
    [InlineData("2 ** 3 va {ota_ona}", "2 ** 3 va {ota_ona}")]
    [InlineData("<b>teg</b> & <script>", "<b>teg</b> &amp; &lt;script&gt;")]
    public void Belgilar_tegga_aylanadi(string input, string expected) =>
        Assert.Equal(expected, TelegramHtml.FromMarkup(input));

    [Fact]
    public void Oddiy_matnga_aylanadi() =>
        Assert.Equal("Diqqat, 5 < 6", TelegramHtml.ToPlain(TelegramHtml.Sanitize("<b>Diqqat</b>, 5 < 6")));
}
