using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Umumiy qidiruv — <c>GET /api/admin/search</c>: ism (so'z tartibi, tutuq belgisi),
/// telefon (istalgan format) va har bo'limning o'z ruxsati.
///
/// <para>Umumiy baza: har test o'z noyob qo'shimchasi bilan ism yaratadi va aynan
/// shu qo'shimcha bo'yicha qidiradi, qo'shni testlarning yozuvlari aralashmaydi.</para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class GlobalSearchTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/search";

    private static string Tag() => "Qz" + Guid.NewGuid().ToString("N")[..6];

    /// <summary>9 raqamli, bazada takrorlanmaydigan mahalliy raqam.</summary>
    private static string LocalNumber() => "9" + Random.Shared.NextInt64(10_000_000, 99_999_999);

    private async Task<(string StudentId, string Tag, string Phone)> SeedStudentAsync()
    {
        var tag = Tag();
        var local = LocalNumber();
        var student = new Student
        {
            FullName = $"{tag} Go'zal Karimova",
            LastName = "Karimova",
            FirstName = "Go'zal",
            BirthDate = "2014-01-01",
            Gender = "female",
            ClassName = "",
            EnrollmentDate = "2026-09-01",
            ParentPhone = $"+998 {local[..2]} {local[2..5]}-{local[5..7]}-{local[7..]}",
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return (student.Id, tag, local);
    }

    private static async Task<List<GlobalSearchHitDto>> SearchAsync(HttpClient client, string q)
    {
        var res = await client.GetAsync($"{Url}?q={Uri.EscapeDataString(q)}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<List<GlobalSearchHitDto>>())!;
    }

    [Fact]
    public async Task Ism_soz_tartibi_va_tutuq_belgisidan_qatiy_nazar_topiladi()
    {
        var (id, tag, _) = await SeedStudentAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // teskari tartib, kichik harf, boshqa tutuq belgisi (ʻ)
        var hits = await SearchAsync(admin, $"karimova goʻzal {tag.ToLowerInvariant()}");

        var hit = Assert.Single(hits, h => h.Id == id);
        Assert.Equal("student", hit.Kind);
        Assert.Equal($"/admin/students/{id}", hit.Url);
    }

    [Theory]
    [InlineData("full")]     // 998XXXXXXXXX
    [InlineData("local")]    // XXXXXXXXX
    [InlineData("tail")]     // oxirgi 4 raqam
    [InlineData("spaced")]   // "90 123 45 67" ko'rinishida
    public async Task Telefon_istalgan_formatda_topiladi(string form)
    {
        var (id, _, local) = await SeedStudentAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var q = form switch
        {
            "full" => "998" + local,
            "local" => local,
            "tail" => local[^7..],
            _ => $"{local[..2]} {local[2..5]} {local[5..7]} {local[7..]}",
        };

        Assert.Contains(await SearchAsync(admin, q), h => h.Id == id);
    }

    [Fact]
    public async Task Juda_qisqa_sorov_bosh_royxat()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Empty(await SearchAsync(admin, "a"));
    }

    /// <summary>
    /// Xodim oddiy GET bilan hamma bo'limni o'qiy olsa ham, qidiruv har bo'limni o'z
    /// kaliti bilan yopadi: "students" ruxsatisiz o'quvchi ismi ham, telefoni ham chiqmaydi.
    /// </summary>
    [Fact]
    public async Task Xodim_ruxsati_yoq_bolimni_korolmaydi()
    {
        var (id, tag, _) = await SeedStudentAsync();

        using var noPerm = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers");
        Assert.DoesNotContain(await SearchAsync(noPerm, tag), h => h.Id == id);

        using var withPerm = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Contains(await SearchAsync(withPerm, tag), h => h.Id == id);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Boshqa_rollar_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}?q=ali")).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Url}?q=ali")).StatusCode);
    }
}
