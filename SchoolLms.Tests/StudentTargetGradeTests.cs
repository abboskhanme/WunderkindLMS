using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Sinfsiz o'quvchi — mo'ljaldagi sinf darajasi bilan (docs/modules/students-parity.md
/// §2.3.3, S-9): qabul qilindi, lekin qaysi sinfga tushishi keyin — sinf ro'yxatidan
/// (§2.1.1) — hal qilinadi.
///
/// <para>
/// <b>Qoida — ikkovidan BITTASI shart.</b> <c>ClassName</c> yoki
/// <c>TargetGrade</c> — kamida bittasi bo'lishi kerak, aks holda o'quvchi
/// hech qayerda (na sinf ro'yxatida, na "sinfsizlar" filtrida) topilmaydigan
/// bo'lib qolardi.
/// </para>
/// <para>
/// <b>Qoida — sinf berilsa mo'ljal AVTOMATIK bo'shaydi.</b> Mo'ljal faqat
/// "sinfi hali yo'q" holat uchun — sinfga joylashtirilgan o'quvchida eski
/// mo'ljal osilib qolmasligi kerak (<c>StudentsController.CleanTargetGrade</c>).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentTargetGradeTests(ApiFixture fixture)
{
    private const string Students = "/api/admin/students";

    [Fact]
    public async Task Sinfsiz_oquvchi_moljal_daraja_bilan_yaratiladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var payload = BasePayload(tag);
        payload["className"] = "";
        payload["targetGrade"] = 0; // 0 = maktabgacha tayyorlov

        var response = await admin.PostAsJsonAsync(Students, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await JsonAsync(response);
        Assert.Equal("", row.GetProperty("className").GetString());
        Assert.Equal(0, row.GetProperty("targetGrade").GetInt32());
    }

    [Fact]
    public async Task Sinf_ham_moljal_ham_yoq_bolsa_rad_etiladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var payload = BasePayload(tag);
        payload["className"] = "";
        // targetGrade umuman berilmagan.

        var response = await admin.PostAsJsonAsync(Students, payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(StudentsController.ClassOrTargetGradeMessage, await MessageAsync(response));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public async Task Moljal_diapazondan_tashqari_rad_etiladi(int badGrade)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var payload = BasePayload(tag);
        payload["className"] = "";
        payload["targetGrade"] = badGrade;

        var response = await admin.PostAsJsonAsync(Students, payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(StudentsController.TargetGradeRangeMessage, await MessageAsync(response));
    }

    /// <summary>Sinf VA mo'ljal birga yuborilsa — mo'ljal kerak emas, saqlanmaydi.</summary>
    [Fact]
    public async Task Sinf_berilganda_moljal_saqlanmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var payload = BasePayload(tag);
        payload["className"] = $"TG-{tag[..4]}";
        payload["targetGrade"] = 5;

        var response = await admin.PostAsJsonAsync(Students, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("targetGrade").ValueKind);
    }

    /// <summary>
    /// Tahrirlashda <c>targetGrade</c> umuman yuborilmasa — "tegma" qoidasi
    /// (Phone/Language bilan bir xil): sinfsiz o'quvchining mavjud mo'ljali
    /// saqlanib qoladi, har safar qayta yuborish shart emas.
    /// </summary>
    [Fact]
    public async Task Tahrirlashda_moljal_yuborilmasa_saqlanib_qoladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var created = await admin.PostAsJsonAsync(Students, new Dictionary<string, object?>(BasePayload(tag))
        {
            ["className"] = "",
            ["targetGrade"] = 3,
        });
        var id = (await JsonAsync(created)).GetProperty("id").GetString()!;

        // targetGrade maydoni umuman yo'q — faqat manzil yangilanadi.
        var update = await admin.PutAsJsonAsync($"{Students}/{id}", new
        {
            fullName = $"O'quvchi {tag}",
            birthDate = "2015-05-05",
            address = "Yangi manzil",
            gender = "male",
            parentFullName = $"Ota-ona {tag}",
            parentPhone = (string)BasePayload(tag)["parentPhone"]!,
            className = "",
            enrollmentDate = "2026-09-01",
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var row = await RowAsync(admin, id);
        Assert.Equal(3, row.GetProperty("targetGrade").GetInt32());
    }

    /// <summary>Sinfga joylashtirilsa — eski mo'ljal avtomatik bo'shaydi.</summary>
    [Fact]
    public async Task Sinfga_joylashtirilsa_moljal_bosaladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var className = $"TG2-{tag[..4]}";
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(new SchoolClass { Name = className, Grade = 5 });
            await db.SaveChangesAsync();
        });

        var created = await admin.PostAsJsonAsync(Students, new Dictionary<string, object?>(BasePayload(tag))
        {
            ["className"] = "",
            ["targetGrade"] = 5,
        });
        var id = (await JsonAsync(created)).GetProperty("id").GetString()!;

        var update = await admin.PutAsJsonAsync($"{Students}/{id}", new
        {
            fullName = $"O'quvchi {tag}",
            birthDate = "2015-05-05",
            address = "Toshkent",
            gender = "male",
            parentFullName = $"Ota-ona {tag}",
            parentPhone = (string)BasePayload(tag)["parentPhone"]!,
            className,
            enrollmentDate = "2026-09-01",
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var row = await RowAsync(admin, id);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("targetGrade").ValueKind);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "TG" + Guid.NewGuid().ToString("N")[..8];

    private static Dictionary<string, object?> BasePayload(string tag) => new()
    {
        ["fullName"] = $"O'quvchi {tag}",
        ["birthDate"] = "2015-05-05",
        ["address"] = "Toshkent",
        ["gender"] = "male",
        ["parentFullName"] = $"Ota-ona {tag}",
        ["parentPhone"] = "+998" + Random.Shared.Next(100_000_000, 999_999_999),
        ["enrollmentDate"] = "2026-09-01",
    };

    /// <summary>Bitta o'quvchini <c>GET /api/admin/students</c> ro'yxatidan topadi (StudentDto).</summary>
    private static async Task<JsonElement> RowAsync(HttpClient client, string id)
    {
        var all = await JsonAsync(await client.GetAsync(Students));
        return all.EnumerateArray().First(r => r.GetProperty("id").GetString() == id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }
}
