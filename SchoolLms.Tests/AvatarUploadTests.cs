using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Profil rasmi — <c>POST/DELETE /api/auth/avatar</c> (mijoz, 2026-09-22: "barcha uchun
/// profile image yuklash mumkin bo'lsin"). Rol bo'yicha farq YO'Q: kim tizimga kirsa, o'z
/// rasmini qo'yadi. Faqat rasm fayli qabul qilinadi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AvatarUploadTests(ApiFixture fixture)
{
    // 1×1 shaffof PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent Form(byte[] bytes, string name, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Har_qanday_rol_oz_rasmini_yuklaydi_va_olib_tashlaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        var up = await client.PostAsync("/api/auth/avatar", Form(Png, "men.png", "image/png"));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var user = (await up.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.StartsWith("/uploads/", user.AvatarUrl);

        var me = (await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!;
        Assert.Equal(user.AvatarUrl, me.AvatarUrl);

        var del = await client.DeleteAsync("/api/auth/avatar");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Null((await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!.AvatarUrl);
    }

    [Fact]
    public async Task Rasm_bolmagan_fayl_rad_etiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);
        var res = await client.PostAsync("/api/auth/avatar", Form("%PDF-1.4"u8.ToArray(), "hujjat.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/auth/avatar", Form(Png, "a.png", "image/png"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/auth/avatar")).StatusCode);
    }

    /// <summary>
    /// Akkaunt saqlanganda ruxsatlar ham qaytadi — aks holda klient xodimni "hamma bo'lim
    /// ochiq" deb saqlab qo'yardi (ilgari javobda `permissions` yo'q edi).
    /// </summary>
    [Fact]
    public async Task Akkaunt_saqlanganda_xodim_ruxsatlari_qaytadi()
    {
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: ["students"]);
        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Staff, user.Id, user.FullName, user.Email));

        var res = await client.PutAsJsonAsync("/api/auth/account", new { email = user.Email, currentPassword = password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = (await res.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.NotNull(dto.Permissions);
        Assert.Contains("students", dto.Permissions!);
    }

    /// <summary>Admin xodimga rasm qo'yadi; faqat o'zimizning <c>/uploads/</c> fayli qabul qilinadi.</summary>
    [Fact]
    public async Task Xodim_rasmi_faqat_ozimizning_yuklangan_fayl()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var bad = await admin.PostAsJsonAsync("/api/admin/staff",
            new { fullName = "Rasm Testov", position = "Kassir", avatarUrl = "https://evil.example/p.png" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await admin.PostAsJsonAsync("/api/admin/staff",
            new { fullName = "Rasm Testov", position = "Kassir", avatarUrl = "/uploads/abc.png" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var staff = (await ok.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Equal("/uploads/abc.png", staff.AvatarUrl);

        // "" — olib tashlash; null — o'zgarmaydi.
        var cleared = await admin.PutAsJsonAsync($"/api/admin/staff/{staff.Id}",
            new { fullName = "Rasm Testov", position = "Kassir", avatarUrl = "" });
        Assert.Null((await cleared.Content.ReadFromJsonAsync<StaffDto>())!.AvatarUrl);
    }
}
