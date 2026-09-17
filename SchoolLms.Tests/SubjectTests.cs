using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Fanlar katalogi — docs/modules/students-parity.md §2.5 (F-3, F-4).
///
/// <para>
/// <b>F-4 — ruxsat.</b> Ilgari <c>SubjectsController</c> <c>schedule</c>ni
/// talab qilardi, menyu esa "O'quv bo'limi" (<c>students</c>) ostida edi —
/// <c>students</c> ruxsatli-lekin-<c>schedule</c>siz xodim menyuda "Fanlar"ni
/// ko'rar, bosganda "ruxsatingiz yo'q" olardi. Endi darvoza <c>students</c>.
/// Testlar buni ikki tomondan qamrab oladi: <c>students</c> li xodim yozadi,
/// <c>schedule</c> li (eski ruxsat) va <c>classes</c> li xodim yozmaydi.
/// </para>
/// <para>
/// <b>F-3 — rang va faollik.</b> Rang <c>#RRGGBB</c> shaklida (yoki yo'q),
/// va o'chirish o'rniga faolsizlantirish: <c>isActive=false</c> qilingan fan
/// GetAll'dan FILTRLANMASA HAM ko'rinadi (sukut — hammasi), chunki jadval,
/// jurnal va sertifikat kabi o'quvchi ekranlari ID bo'yicha shu ro'yxatdan
/// nom qidiradi va ular buzilmasligi kerak.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class SubjectTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/subjects";

    // =====================================================================
    //  1. RUXSAT (F-4)
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Url, new { name = "X" })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Url, new { name = "X" })).StatusCode);
    }

    /// <summary>
    /// Xodim ro'yxatni o'qiydi (GET — bo'limlararo ochiq); yozish esa faqat
    /// "students" bilan. "schedule" — F-4 dan OLDINGI ruxsat — endi yordam
    /// bermaydi, xuddi "classes" kabi.
    /// </summary>
    [Theory]
    [InlineData("classes")]
    [InlineData("schedule")]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi(string perm)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, perm);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Url, new { name = "X-" + Tag() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"{Url}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Oquv_bolimi_ruxsatli_xodim_yozadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "students");

        var created = await client.PostAsJsonAsync(Url, new { name = "X-" + Tag() });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    // =====================================================================
    //  2. RANG VA FAOLLIK (F-3)
    // =====================================================================

    [Fact]
    public async Task Rang_saqlanadi_va_shakli_tekshiriladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Fizika " + Tag();

        var created = await JsonAsync(await client.PostAsJsonAsync(Url,
            new { name, color = "#34c759" }));
        var id = created.GetProperty("id").GetString();
        // Katta harfga keltiriladi — StudentStatuses bilan bir xil naqsh.
        Assert.Equal("#34C759", created.GetProperty("color").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        var bad = await client.PostAsJsonAsync(Url, new { name = "Y-" + Tag(), color = "qizil" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(SubjectsController.ColorMessage, await MessageAsync(bad));

        // Rangni tozalash — bo'sh satr.
        var cleared = await JsonAsync(await client.PutAsJsonAsync($"{Url}/{id}",
            new { name, isGroupable = false, color = "" }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("color").ValueKind);
    }

    /// <summary>
    /// Faolsizlantirilgan fan o'quvchi/guruh ekranlarini BUZMAYDI: sukut
    /// bo'yicha (filtrsiz) ro'yxatda hali ham ko'rinadi, faqat
    /// <c>isActive=true</c> so'ralganda filtrlanadi.
    /// </summary>
    [Fact]
    public async Task Faolsizlantirilgan_fan_royxatdan_yoqolmaydi_lekin_filtrlanadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Kimyo " + Tag();

        var created = await JsonAsync(await client.PostAsJsonAsync(Url, new { name }));
        var id = created.GetProperty("id").GetString();

        var off = await JsonAsync(await client.PutAsJsonAsync($"{Url}/{id}",
            new { name, isGroupable = false, isActive = false }));
        Assert.False(off.GetProperty("isActive").GetBoolean());

        // Filtrsiz — hali ham bor (mavjud jadval/jurnal/sertifikat buzilmasin).
        var all = await RowsAsync(client, Url);
        Assert.Contains(all, s => s.GetProperty("id").GetString() == id);

        // Faqat faollar — filtrlanadi.
        var actives = await RowsAsync(client, $"{Url}?isActive=true");
        Assert.DoesNotContain(actives, s => s.GetProperty("id").GetString() == id);

        var inactives = await RowsAsync(client, $"{Url}?isActive=false");
        Assert.Contains(inactives, s => s.GetProperty("id").GetString() == id);
    }

    /// <summary>F-1 xabari endi faolsizlantirishni ham tavsiya qiladi.</summary>
    [Fact]
    public async Task Ochirish_xabari_faolsizlantirishni_taklif_qiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Biologiya " + Tag();
        var subjectId = (await JsonAsync(await client.PostAsJsonAsync(Url, new { name })))
            .GetProperty("id").GetString();

        await fixture.Api.WithDbAsync(async db =>
        {
            db.QuarterGrades.Add(new SchoolLms.Domain.QuarterGrade
            {
                ClassId = Guid.NewGuid().ToString(), SubjectId = subjectId!,
                Quarter = 1, StudentId = Guid.NewGuid().ToString(), Grade = 4,
            });
            await db.SaveChangesAsync();
        });

        var refused = await client.DeleteAsync($"{Url}/{subjectId}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("faolsizlantiring", await MessageAsync(refused), StringComparison.Ordinal);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }
}
