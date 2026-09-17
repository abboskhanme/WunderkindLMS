using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Ruxsat darvozasi — <c>AdminGuardiansController</c> (§2.9, P-4).
///
/// <para>
/// Kontrollerning o'zi P-4 dan OLDIN ham bor edi (CRUD, farzand biriktirish,
/// akkaunt ochish) — faqat ekrani yo'q edi. Shu ekran ("Ota-onalar" → tahrirlash
/// oynasi) endi <c>GuardianEditModal.tsx</c> orqali ulangan, lekin bu darvozaga
/// alohida ruxsat testi hech qachon yozilmagan edi — shu bo'shliqni yopadi.
/// </para>
/// <para>
/// <c>[AdminPerm("app")]</c> — <c>AdminPermAttribute</c> qoidasi: xodim (staff)
/// O'QIYDI har doim, YOZADI faqat "app" claim'i bo'lsa.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AdminGuardiansPermissionTests(ApiFixture fixture)
{
    private const string Guardians = "/api/admin/guardians";

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Guardians)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Guardians, new { fullName = "A", phone = NewPhone() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync($"{Guardians}/x", new { fullName = "A", phone = NewPhone() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"{Guardians}/x/children", new { studentId = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"{Guardians}/x/children/y")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"{Guardians}/x/account", new { })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rollar_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Guardians)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Guardians, new { fullName = "A", phone = NewPhone() })).StatusCode);
    }

    /// <summary>Xodim (staff) o'qiydi, lekin "app" ruxsatisiz yoza olmaydi.</summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var guardianId = await CreateGuardianAsync(admin, Tag());

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Guardians)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync(Guardians, new { fullName = "B", phone = NewPhone() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync($"{Guardians}/{guardianId}",
                new { fullName = "B", phone = NewPhone() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync($"{Guardians}/{guardianId}/account", new { })).StatusCode);
    }

    /// <summary>"app" ruxsatli xodim esa vasiyni tahrirlaydi va farzand biriktiradi — P-4 aynan shu.</summary>
    [Fact]
    public async Task Ruxsatli_xodim_tahrirlaydi_va_farzand_biriktiradi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var guardianId = await CreateGuardianAsync(admin, tag);
        var studentId = await CreateStudentAsync(admin, tag);

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "app");

        var renamed = await staff.PutAsJsonAsync($"{Guardians}/{guardianId}",
            new { fullName = $"Yangilangan {tag}", phone = NewPhone() });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var attached = await staff.PostAsJsonAsync($"{Guardians}/{guardianId}/children",
            new { studentId, relation = "father", isPrimary = false });
        Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
        using var body = JsonDocument.Parse(await attached.Content.ReadAsStringAsync());
        var child = Assert.Single(body.RootElement.GetProperty("children").EnumerateArray().ToList());
        Assert.Equal(studentId, child.GetProperty("studentId").GetString());

        var detached = await staff.DeleteAsync($"{Guardians}/{guardianId}/children/{studentId}");
        Assert.Equal(HttpStatusCode.NoContent, detached.StatusCode);
    }

    // ------------------------------------------------------------------
    //  Yordamchilar
    // ------------------------------------------------------------------

    private static string Tag() => "G" + Guid.NewGuid().ToString("N")[..8];

    private static string NewPhone() => "+998" + Random.Shared.Next(100_000_000, 999_999_999);

    private static async Task<string> CreateGuardianAsync(HttpClient client, string tag)
    {
        var response = await client.PostAsJsonAsync(Guardians,
            new { fullName = $"Vasiy {tag}", phone = NewPhone() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateStudentAsync(HttpClient client, string tag)
    {
        var response = await client.PostAsJsonAsync("/api/admin/students", new
        {
            fullName = $"O'quvchi {tag}",
            birthDate = "2015-05-05",
            address = "Toshkent",
            gender = "male",
            parentFullName = $"Ota-ona {tag}",
            parentPhone = NewPhone(),
            // Sinf nomi — bu test guruh/vasiy ruxsatlarini tekshiradi, sinf
            // bilan ishi yo'q; §3.3 (S-9) dan beri bo'sh sinf mo'ljal
            // darajasini talab qiladi, shuning uchun oddiy (mavjud bo'lishi
            // shart emas) nom beriladi.
            className = $"AG-{tag[..4]}",
            enrollmentDate = "2026-09-01",
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
