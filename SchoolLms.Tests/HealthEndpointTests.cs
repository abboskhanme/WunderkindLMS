using System.Net;
using System.Text.Json;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// HTTP pipeline'i (TestServer) haqiqatan ishlayotganini isbotlaydi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class HealthEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Health_200_va_healthy_qaytaradi()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", body.RootElement.GetProperty("status").GetString());
        // "unhealthy" javobida `error` maydoni bo'ladi — u yerda BO'LMASLIGI kerak.
        Assert.False(body.RootElement.TryGetProperty("error", out _));
    }

    /// <summary>
    /// <c>/api</c> ilova muhitini qaytaradi. Bu bilvosita, lekin muhim narsani tekshiradi:
    /// muhit "Testing" — ya'ni Development EMAS. Development'da SpaProxy `npm run dev` ni
    /// ishga tushirishga urinardi va CSP/HSTS middleware'lari umuman sinovdan o'tmasdi.
    /// </summary>
    [Fact]
    public async Task Api_root_ok_va_Testing_muhitini_qaytaradi()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SchoolLms API", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Testing", body.RootElement.GetProperty("environment").GetString());
    }

    /// <summary>Noma'lum <c>/api/*</c> yo'li SPA HTML emas, JSON 404 qaytarsin
    /// (mobil klientlar shunga tayanadi).</summary>
    [Fact]
    public async Task Nomalum_api_yoli_404_json_qaytaradi()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api/bunday-endpoint-yoq");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("API endpoint topilmadi", body.RootElement.GetProperty("message").GetString());
    }

    /// <summary>
    /// Xavfsizlik sarlavhalari middleware'i pipeline'da — SPEC §7 (Security).
    /// Harness prod pipeline'ini aynan takrorlayotganini ham shu isbotlaydi.
    /// </summary>
    [Fact]
    public async Task Xavfsizlik_sarlavhalari_qoyiladi()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal("nosniff", string.Join("", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", string.Join("", response.Headers.GetValues("Referrer-Policy")));
        var csp = string.Join("", response.Headers.GetValues("Content-Security-Policy"));
        // Freymga faqat o'zi va Telegram (Mini App, 2026-09-25) — boshqa sayt clickjacking qila olmaydi.
        Assert.Contains("frame-ancestors 'self' https://web.telegram.org https://*.telegram.org;", csp);
        Assert.DoesNotContain("frame-ancestors *", csp);
        // Mini App SDK yuklanadi — usiz Telegram imzosi yo'q.
        Assert.Contains("https://telegram.org", csp);
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }
}
