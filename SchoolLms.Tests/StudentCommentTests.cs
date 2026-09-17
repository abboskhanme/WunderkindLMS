using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quvchi izohlari — docs/modules/students-parity.md §2.3 (S-11).
///
/// <para>
/// <b>Muallif qoidasi</b> alohida tekshiriladi: o'zganing izohini oddiy xodim
/// o'zgartira ham, o'chira ham olmaydi; admin esa o'zgartira oladi (§2.3.1
/// dagi <c>editStudentComments</c> / <c>deleteStudentComments</c> ruxsatlari
/// o'rniga — bizda ruxsat ro'yxati kengaytirilmagan).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentCommentTests(ApiFixture fixture)
{
    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        var url = "/api/admin/students/x/comments";

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(url, new { kind = "positive", body = "salom" })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");
        var url = "/api/admin/students/x/comments";

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(url, new { kind = "positive", body = "salom" })).StatusCode);
    }

    /// <summary>Xodim izohlarni o'qiydi; "students" ruxsatisiz yoza olmaydi.</summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);
        var student = await SeedStudentAsync(Tag());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/admin/students/{student}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/admin/students/{student}/comments",
                new { kind = "positive", body = "salom" })).StatusCode);
    }

    // =====================================================================
    //  2. CRUD
    // =====================================================================

    [Fact]
    public async Task Izoh_yoziladi_tahrirlanadi_va_ochiriladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync(Tag());
        var url = $"/api/admin/students/{student}/comments";

        var created = await client.PostAsJsonAsync(url,
            new { kind = "positive", body = "Olimpiadaga tayyorlanmoqda", imageUrl = "/uploads/a.png" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("positive", body.RootElement.GetProperty("kind").GetString());
        Assert.True(body.RootElement.GetProperty("canEdit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("updatedAt").ValueKind);

        var updated = await client.PutAsJsonAsync($"/api/admin/students/comments/{id}",
            new { kind = "negative", body = "Darsga kech qoldi" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using (var after = JsonDocument.Parse(await updated.Content.ReadAsStringAsync()))
        {
            Assert.Equal("negative", after.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Darsga kech qoldi", after.RootElement.GetProperty("body").GetString());
            Assert.NotEqual(JsonValueKind.Null, after.RootElement.GetProperty("updatedAt").ValueKind);
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/admin/students/comments/{id}")).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.StudentComments.AnyAsync(c => c.Id == id)));
    }

    [Fact]
    public async Task Bosh_matn_va_notogri_tur_rad_etiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync(Tag());
        var url = $"/api/admin/students/{student}/comments";

        var empty = await client.PostAsJsonAsync(url, new { kind = "positive", body = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(StudentCommentsController.BodyRequiredMessage, await MessageAsync(empty));

        var badKind = await client.PostAsJsonAsync(url, new { kind = "neytral", body = "salom" });
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal(StudentCommentsController.KindMessage, await MessageAsync(badKind));

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/admin/students/yoq-bunday/comments",
                new { kind = "positive", body = "salom" })).StatusCode);
    }

    /// <summary>Tur bo'yicha filtr — §2.3.1 dagi ijobiy/salbiy chipi.</summary>
    [Fact]
    public async Task Tur_boyicha_filtr()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync(Tag());
        var url = $"/api/admin/students/{student}/comments";

        await client.PostAsJsonAsync(url, new { kind = "positive", body = "Yaxshi" });
        await client.PostAsJsonAsync(url, new { kind = "negative", body = "Yomon" });
        await client.PostAsJsonAsync(url, new { kind = "negative", body = "Yana yomon" });

        Assert.Equal(3, (await RowsAsync(client, url)).Count);
        Assert.Single(await RowsAsync(client, $"{url}?kind=positive"));
        Assert.Equal(2, (await RowsAsync(client, $"{url}?kind=negative")).Count);
    }

    // =====================================================================
    //  3. MUALLIF QOIDASI
    // =====================================================================

    /// <summary>
    /// Xodim o'z izohini tahrirlaydi/o'chiradi; O'ZGANIKINI — yo'q. Admin esa
    /// istalganini o'zgartira oladi.
    /// </summary>
    [Fact]
    public async Task Ozganing_izohini_xodim_ozgartira_olmaydi()
    {
        using var author = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        using var other = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var student = await SeedStudentAsync(Tag());
        var url = $"/api/admin/students/{student}/comments";

        var created = await author.PostAsJsonAsync(url, new { kind = "positive", body = "Mening izohim" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();

        // Boshqa xodim — na tahrir, na o'chirish.
        var refusedEdit = await other.PutAsJsonAsync($"/api/admin/students/comments/{id}",
            new { kind = "negative", body = "O'zgartirdim" });
        Assert.Equal(HttpStatusCode.BadRequest, refusedEdit.StatusCode);
        Assert.Equal(StudentCommentsController.ForbiddenMessage, await MessageAsync(refusedEdit));

        var refusedDelete = await other.DeleteAsync($"/api/admin/students/comments/{id}");
        Assert.Equal(HttpStatusCode.BadRequest, refusedDelete.StatusCode);

        // Muallif ko'zida canEdit=false bo'lmaydi, o'zganikida esa false.
        var mine = (await RowsAsync(author, url)).Single();
        Assert.True(mine.GetProperty("canEdit").GetBoolean());
        var theirs = (await RowsAsync(other, url)).Single();
        Assert.False(theirs.GetProperty("canEdit").GetBoolean());

        // Muallifning o'zi — bemalol.
        Assert.Equal(HttpStatusCode.OK,
            (await author.PutAsJsonAsync($"/api/admin/students/comments/{id}",
                new { kind = "positive", body = "Tuzatdim" })).StatusCode);

        // Admin ham bemalol.
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/admin/students/comments/{id}")).StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "C" + Guid.NewGuid().ToString("N")[..8];

    private async Task<string> SeedStudentAsync(string tag)
    {
        var student = GeneralSettingsFlagsTests.NewStudent(
            $"Izoh {Guid.NewGuid().ToString("N")[..4]} {tag}", $"IZ-{tag}", "+998900000003");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return student.Id;
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
