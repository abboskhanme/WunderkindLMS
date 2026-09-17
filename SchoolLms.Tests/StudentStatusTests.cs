using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quvchi holati taglari (docs/modules/students-parity.md §2.3, S-5):
/// katalog CRUD, rang shakli, ishlatilgan qatorni himoya qilish va
/// o'quvchiga holat qo'yish.
///
/// <para>
/// <b>Rang alohida tekshiriladi</b> — u bazada CHECK constraint bilan
/// himoyalangan (<c>ck_student_statuses_color</c>). Agar controller shaklni
/// tekshirmasa, foydalanuvchi "qizil" deb yozganda ilova 23514 bilan
/// yiqilardi: tushunarsiz 500 o'rniga tushunarli 400 chiqishi kerak.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentStatusTests(ApiFixture fixture)
{
    private const string Statuses = "/api/admin/student-statuses";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Statuses)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Statuses, new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/admin/students/x/status", new { statusId = (Guid?)null })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Statuses)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Statuses, new { name = "X" })).StatusCode);
    }

    /// <summary>Xodim katalogni o'qiydi; "students" ruxsatisiz yoza olmaydi va holat qo'ya olmaydi.</summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Statuses)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Statuses, new { name = "Xodim " + Tag() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync("/api/admin/students/x/status", new { statusId = (Guid?)null })).StatusCode);
    }

    /// <summary>"students" ruxsatli xodim esa yoza oladi — darvoza aynan shu kalitga bog'langan.</summary>
    [Fact]
    public async Task Students_ruxsatli_xodim_yozadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "students");

        var response = await client.PostAsJsonAsync(Statuses, new { name = $"Ruxsatli {Tag()}" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =====================================================================
    //  2. KATALOG
    // =====================================================================

    /// <summary>Migratsiya birorta holat SEED QILMAYDI (StudentStatuses.cs dagi qaror).</summary>
    [Fact]
    public async Task Katalog_seed_qilinmagan()
    {
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.StudentStatuses.AnyAsync(s => s.IsDefault)));
    }

    [Fact]
    public async Task Crud_nom_unikal_va_faolsizlantirish()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var created = await client.PostAsJsonAsync(Statuses,
            new { name = $"Sinov muddatida {tag}", color = "#34C759", position = 3 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = await IdAsync(created);

        var dup = await client.PostAsJsonAsync(Statuses, new { name = $"Sinov muddatida {tag}" });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Equal(StudentStatusesController.NameTakenMessage, await MessageAsync(dup));

        // Faolsizlantirish: sukut ro'yxatdan chiqadi, includeInactive bilan qoladi.
        var off = await client.PutAsJsonAsync($"{Statuses}/{id}",
            new { name = $"Sinov muddatida {tag}", isActive = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.DoesNotContain(await RowsAsync(client, Statuses), r => r.GetProperty("id").GetGuid() == id);
        Assert.Contains(await RowsAsync(client, $"{Statuses}?includeInactive=true"),
            r => r.GetProperty("id").GetGuid() == id);

        // Ishlatilmagan qator o'chiriladi.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Statuses}/{id}")).StatusCode);
    }

    /// <summary>
    /// Rang <c>#RRGGBB</c> bo'lishi shart — baza constraint'i bilan bir xil.
    /// <c>#</c> siz yozilgani QABUL qilinadi va normallashtiriladi; "qizil" — 400.
    /// </summary>
    [Fact]
    public async Task Rang_shakli_tekshiriladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var bad = await client.PostAsJsonAsync(Statuses, new { name = $"Yomon {tag}", color = "qizil" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(StudentStatusesController.ColorMessage, await MessageAsync(bad));

        var tooShort = await client.PostAsJsonAsync(Statuses, new { name = $"Qisqa {tag}", color = "#FFF" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var ok = await client.PostAsJsonAsync(Statuses, new { name = $"Yaxshi {tag}", color = "34c759" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal("#34C759", json.RootElement.GetProperty("color").GetString());

        // Bo'sh satr rangni TOZALAYDI (neytral).
        var id = json.RootElement.GetProperty("id").GetGuid();
        var cleared = await client.PutAsJsonAsync($"{Statuses}/{id}", new { name = $"Yaxshi {tag}", color = "" });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var after = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("color").ValueKind);
    }

    /// <summary>Ishlatilgan holat O'CHIRILMAYDI — aks holda o'quvchilarning tagi jimgina bo'shardi.</summary>
    [Fact]
    public async Task Ishlatilgan_holat_ochirilmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var id = await IdAsync(await client.PostAsJsonAsync(Statuses, new { name = $"VIP {tag}" }));
        var student = await SeedStudentAsync(tag);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/admin/students/{student}/status",
                new { statusId = id })).StatusCode);

        var row = (await RowsAsync(client, Statuses)).Single(r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal(1, row.GetProperty("usedBy").GetInt32());

        var delete = await client.DeleteAsync($"{Statuses}/{id}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Contains("faolsizlantiring", await MessageAsync(delete));

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(id, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == student)).StatusId));
    }

    // =====================================================================
    //  3. O'QUVCHIGA HOLAT QO'YISH
    // =====================================================================

    /// <summary>
    /// Qo'yish, almashtirish va olib tashlash. Faolsizlantirilgan holatni
    /// QO'YIB BO'LMAYDI, lekin allaqachon qo'yilgani joyida qoladi.
    /// </summary>
    [Fact]
    public async Task Holat_qoyiladi_almashadi_va_olinadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var vip = await IdAsync(await client.PostAsJsonAsync(Statuses, new { name = $"VIP {tag}" }));
        var leaving = await IdAsync(await client.PostAsJsonAsync(Statuses, new { name = $"Ketmoqchi {tag}" }));
        var student = await SeedStudentAsync(tag);
        var url = $"/api/admin/students/{student}/status";

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync(url, new { statusId = vip })).StatusCode);
        await AssertStatusAsync(student, vip);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync(url, new { statusId = leaving })).StatusCode);
        await AssertStatusAsync(student, leaving);

        // Faolsizlantirilgan holatni qo'yib bo'lmaydi.
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"{Statuses}/{vip}", new { name = $"VIP {tag}", isActive = false })).StatusCode);
        var refused = await client.PutAsJsonAsync(url, new { statusId = vip });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        await AssertStatusAsync(student, leaving);

        // Olib tashlash.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync(url, new { statusId = (Guid?)null })).StatusCode);
        await AssertStatusAsync(student, null);
    }

    [Fact]
    public async Task Notogri_holat_va_notogri_oquvchi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync(Tag());

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync("/api/admin/students/yoq-bunday/status",
                new { statusId = (Guid?)null })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/admin/students/{student}/status",
                new { statusId = Guid.NewGuid() })).StatusCode);
    }

    /// <summary>Holat o'zgarishi audit'ga tushadi — kartochkadagi "Faoliyat tarixi" shundan o'qiydi.</summary>
    [Fact]
    public async Task Holat_ozgarishi_auditga_tushadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var id = await IdAsync(await client.PostAsJsonAsync(Statuses, new { name = $"Audit {tag}" }));
        var student = await SeedStudentAsync(tag);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/admin/students/{student}/status", new { statusId = id })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var log = await db.AuditLogs.AsNoTracking()
                .Where(a => a.StudentId == student
                            && a.EntityType == StudentStatusesController.AuditEntity)
                .SingleAsync();
            Assert.Contains($"Audit {tag}", log.Summary);
        });
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "S" + Guid.NewGuid().ToString("N")[..8];

    private async Task<string> SeedStudentAsync(string tag)
    {
        var student = GeneralSettingsFlagsTests.NewStudent(
            $"Holat {Guid.NewGuid().ToString("N")[..4]} {tag}", $"H-{tag}", "+998900000002");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return student.Id;
    }

    private async Task AssertStatusAsync(string studentId, Guid? expected)
    {
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(expected,
                (await db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId)).StatusId));
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
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
