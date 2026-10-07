using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// 2026-10-07: 148 distinct Mini App sign-ins failed in a month — people type "Rasulov Muhammadiso",
/// the phone keyboard capitalises the first letter, the patronymic gets added. The login is found
/// regardless of case, spaces and apostrophes, and from the first two words of a full name — only
/// when exactly one account matches.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class LoginLookupTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("Rasulov Muhammadiso")]
    [InlineData("RasulovMuhammadiso")]
    [InlineData("  rasulovmuhammadiso ")]
    [InlineData("Rasulov Muhammadiso Ma'rufjon o'g'li")]
    public async Task Ism_familiya_va_katta_harf_bilan_ham_topiladi(string typed)
    {
        var tag = Suffix();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Teacher, email: $"rasulovmuhammadiso{tag}");
        var input = typed.Replace("Muhammadiso", "Muhammadiso" + tag, StringComparison.OrdinalIgnoreCase)
            .Replace("muhammadiso", "muhammadiso" + tag, StringComparison.Ordinal);

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(user.Id, (await LoginLookup.FindAsync(db, input))?.Id));

        using var anonymous = fixture.Api.AnonymousClient();
        var web = await LoginAsync(anonymous, input, password);
        Assert.True(web.StatusCode == HttpStatusCode.OK, $"{input} → {(int)web.StatusCode} {await web.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Ikki_akkaunt_mos_kelsa_hech_biri_tanlanmaydi()
    {
        var tag = Suffix();
        await fixture.Api.SeedUserAsync(Roles.Teacher, email: $"aliyevvali{tag}");
        await fixture.Api.SeedUserAsync(Roles.Teacher, email: $"aliyev vali{tag}");

        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.Null(await LoginLookup.FindAsync(db, $"Aliyev Vali{tag}"));
            // The exact login still wins.
            Assert.NotNull(await LoginLookup.FindAsync(db, $"aliyevvali{tag}"));
        });
    }

    [Fact]
    public async Task Notogri_parol_baribir_rad_etiladi()
    {
        var tag = Suffix();
        await fixture.Api.SeedUserAsync(Roles.Teacher, email: $"karimovsardor{tag}");
        using var anonymous = fixture.Api.AnonymousClient();
        var response = await LoginAsync(anonymous, $"Karimov Sardor{tag}", "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Normalize_harf_bosh_joy_va_tutuq_belgisini_olib_tashlaydi()
    {
        Assert.Equal("shagayevshohjahonmarufjonogli", LoginLookup.Normalize("Shagayev SHohjahon Ma'rufjon o‘g‘li"));
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..6];

    /// <summary>Each sign-in from its own TEST-NET address — the login rate limit is per IP.</summary>
    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-Forwarded-For", SalesMarketingKit.NextIp());
        return await client.SendAsync(request);
    }
}
