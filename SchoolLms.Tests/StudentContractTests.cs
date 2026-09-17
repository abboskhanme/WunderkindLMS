using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quvchi shartnomalari reyestri — docs/modules/students-parity.md §2.10
/// (K-1, K-2, K-3).
///
/// <para>
/// <b>Eng muhim tekshiruv — MAVJUD GENERATOR O'ZGARMAGANI.</b>
/// <c>ContractService.FillTemplate</c> ota-ona andozasidagi beshta eski
/// tokenni avvalgidek to'ldiradi va noma'lum tokenni avvalgidek TEGMASDAN
/// qoldiradi. Agar biror kun kimdir "tokenlarni tartibga solaman" desa, shu
/// test birinchi bo'lib qizaradi.
/// </para>
/// <para>
/// <b>Raqam</b> — qisman unikal indeks (<c>ux_student_contracts_number</c>).
/// Controller uni OLDINDAN tekshiradi: aks holda foydalanuvchi tushunarsiz
/// 500 (23505) ko'rardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentContractTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/student-contracts";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Url, new { studentId = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/students/x/contracts")).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "contracts");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Url, new { studentId = "x" })).StatusCode);
    }

    /// <summary>Xodim reyestrni o'qiydi; "contracts" ruxsatisiz yoza olmaydi.</summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);
        var student = await SeedStudentAsync(Tag());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/admin/students/{student}/contracts")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Url, new { studentId = student })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{Url}/generate", new { studentId = student })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { studentId = student })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"{Url}/{Guid.NewGuid()}")).StatusCode);
    }

    /// <summary>"contracts" ruxsatli xodim yozadi — darvoza aynan shu kalitga bog'langan.</summary>
    [Fact]
    public async Task Contracts_ruxsatli_xodim_yozadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "contracts");
        var tag = Tag();
        var student = await SeedStudentAsync(tag);

        var created = await client.PostAsJsonAsync(Url,
            new { studentId = student, number = $"{tag}-1", signedOn = "2026-09-01" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    // =====================================================================
    //  2. YOZUV
    // =====================================================================

    [Fact]
    public async Task Crud_va_takroriy_raqam_rad_etiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var student = await SeedStudentAsync(tag);
        var other = await SeedStudentAsync(Tag());

        var created = await client.PostAsJsonAsync(Url, new
        {
            studentId = student,
            number = $"{tag}-1",
            signedOn = "2026-09-01",
            endsOn = "2027-05-31",
            fileUrl = "/uploads/imzolangan.pdf",
            source = "uploaded",
            comment = "Qog'oz nusxa seyfda",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var row = await JsonAsync(created);
        var id = row.GetProperty("id").GetGuid();
        Assert.Equal($"{tag}-1", row.GetProperty("number").GetString());
        Assert.Equal("uploaded", row.GetProperty("source").GetString());
        Assert.Equal("active", row.GetProperty("status").GetString());
        Assert.Equal("2026-09-01", row.GetProperty("signedOn").GetString());

        // Raqam butun maktab bo'yicha unikal — boshqa bolaga ham berilmaydi.
        var duplicate = await client.PostAsJsonAsync(Url,
            new { studentId = other, number = $"{tag}-1" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(StudentContractsController.NumberTakenMessage, await MessageAsync(duplicate));

        // Tahrir: raqamni bo'shatsa — "qoralama" bo'ladi.
        var updated = await client.PutAsJsonAsync($"{Url}/{id}", new { number = "" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("draft", (await JsonAsync(updated)).GetProperty("status").GetString());

        // Bo'shagan raqamni endi boshqa bola olishi mumkin.
        var reused = await client.PostAsJsonAsync(Url, new { studentId = other, number = $"{tag}-1" });
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.Empty(await StudentRowsAsync(client, student));
    }

    /// <summary>
    /// Sanalar: buzuq shakl va teskari oraliq — ikkalasi ham 400.
    /// <c>ck_student_contracts_period</c> baza darajasida ham bor, lekin
    /// foydalanuvchi 500 emas, tushunarli xabar ko'rishi kerak.
    /// </summary>
    [Fact]
    public async Task Sana_tekshiruvi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync(Tag());

        var broken = await client.PostAsJsonAsync(Url, new { studentId = student, signedOn = "01.09.2026" });
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.Equal(StudentContractsController.DateFormatMessage, await MessageAsync(broken));

        var reversed = await client.PostAsJsonAsync(Url,
            new { studentId = student, signedOn = "2026-09-01", endsOn = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(StudentContractsController.PeriodMessage, await MessageAsync(reversed));

        var badSource = await client.PostAsJsonAsync(Url, new { studentId = student, source = "printed" });
        Assert.Equal(HttpStatusCode.BadRequest, badSource.StatusCode);
        Assert.Equal(StudentContractsController.SourceMessage, await MessageAsync(badSource));

        var noStudent = await client.PostAsJsonAsync(Url, new { studentId = "yo-q" });
        Assert.Equal(HttpStatusCode.BadRequest, noStudent.StatusCode);
        Assert.Equal(StudentContractsController.StudentNotFoundMessage, await MessageAsync(noStudent));
    }

    // =====================================================================
    //  3. REYESTR FILTRLARI
    // =====================================================================

    [Fact]
    public async Task Reyestr_filtrlari()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var className = $"9-{tag[..4]}";
        var student = await SeedStudentAsync(tag, className);
        var outsider = await SeedStudentAsync(Tag());

        // 1) faylli, amaldagi; 2) faylsiz qoralama; 3) muddati o'tgan.
        await client.PostAsJsonAsync(Url, new
        {
            studentId = student, number = $"{tag}-A", signedOn = "2026-03-01",
            endsOn = "2030-01-01", fileUrl = "/uploads/a.pdf", source = "uploaded",
        });
        await client.PostAsJsonAsync(Url, new { studentId = student, signedOn = "2026-04-01" });
        await client.PostAsJsonAsync(Url, new
        {
            studentId = student, number = $"{tag}-C", signedOn = "2020-01-01", endsOn = "2021-01-01",
        });
        await client.PostAsJsonAsync(Url, new { studentId = outsider, number = $"{tag}-D" });

        // Sinf bo'yicha — begona bola chiqmaydi.
        var byClass = await ItemsAsync(client, $"{Url}?className={className}");
        Assert.Equal(3, byClass.Count);
        Assert.All(byClass, r => Assert.Equal(student, r.GetProperty("studentId").GetString()));

        // Holat.
        Assert.Single(await ItemsAsync(client, $"{Url}?studentId={student}&status=draft"));
        Assert.Single(await ItemsAsync(client, $"{Url}?studentId={student}&status=expired"));
        Assert.Single(await ItemsAsync(client, $"{Url}?studentId={student}&status=active"));

        // Fayl bor / yo'q.
        Assert.Single(await ItemsAsync(client, $"{Url}?studentId={student}&hasFile=true"));
        Assert.Equal(2, (await ItemsAsync(client, $"{Url}?studentId={student}&hasFile=false")).Count);

        // Imzo sanasi oralig'i.
        var range = await ItemsAsync(client, $"{Url}?studentId={student}&from=2026-01-01&to=2026-12-31");
        Assert.Equal(2, range.Count);

        // Raqam bo'yicha qidiruv (registrga bog'liq emas).
        var byNumber = await ItemsAsync(client, $"{Url}?search={tag.ToLowerInvariant()}-a");
        Assert.Single(byNumber);
        Assert.Equal($"{tag}-A", byNumber[0].GetProperty("number").GetString());

        // Manba. Qo'lda kiritilgan yozuv sukut bo'yicha "uploaded" — uchchalasi ham
        // shunday; "generated" esa faqat andozadan hosil qilinganda paydo bo'ladi.
        Assert.Equal(3, (await ItemsAsync(client, $"{Url}?studentId={student}&source=uploaded")).Count);
        Assert.Empty(await ItemsAsync(client, $"{Url}?studentId={student}&source=generated"));

        // Sahifalash.
        var paged = await JsonAsync(await client.GetAsync($"{Url}?studentId={student}&page=1&pageSize=2"));
        Assert.Equal(3, paged.GetProperty("total").GetInt32());
        Assert.Equal(2, paged.GetProperty("items").GetArrayLength());
    }

    /// <summary>Kartochkadagi tab — eng yangisi tepada, raqamsizi oxirida.</summary>
    [Fact]
    public async Task Oquvchi_tarixi_tartibi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var student = await SeedStudentAsync(tag);

        await client.PostAsJsonAsync(Url, new { studentId = student, number = $"{tag}-eski", signedOn = "2024-09-01" });
        await client.PostAsJsonAsync(Url, new { studentId = student });
        await client.PostAsJsonAsync(Url, new { studentId = student, number = $"{tag}-yangi", signedOn = "2026-09-01" });

        var rows = await StudentRowsAsync(client, student);
        Assert.Equal(3, rows.Count);
        Assert.Equal($"{tag}-yangi", rows[0].GetProperty("number").GetString());
        Assert.Equal($"{tag}-eski", rows[1].GetProperty("number").GetString());
        Assert.Equal(JsonValueKind.Null, rows[2].GetProperty("number").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/admin/students/yo-q/contracts")).StatusCode);
    }

    // =====================================================================
    //  4. ANDOZADAN HOSIL QILISH (K-2)
    // =====================================================================

    [Fact]
    public async Task Andozadan_hosil_qilinadi_va_raqam_avtomatik_beriladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var student = await SeedStudentAsync(tag);
        var template = await SeedTemplateAsync(tag, "Shartnoma № @raqam, @oquvchi (@sinf), @sana");

        // Ko'rish: keyingi bo'sh raqam va tayyor qiymatlar.
        var preview = await JsonAsync(await client.GetAsync($"{Url}/preview?studentId={student}"));
        var nextNumber = preview.GetProperty("nextNumber").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(nextNumber));
        Assert.Contains(preview.GetProperty("tokens").EnumerateArray(),
            t => t.GetProperty("token").GetString() == "@oquvchi");

        var response = await client.PostAsJsonAsync($"{Url}/generate", new
        {
            studentId = student,
            templateId = template,
            signedOn = "2026-09-15",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await JsonAsync(response);
        Assert.Equal("generated", row.GetProperty("source").GetString());
        Assert.Equal(nextNumber, row.GetProperty("number").GetString());
        Assert.Equal(template, row.GetProperty("templateId").GetString());

        // Fayl haqiqatan yozilgan va tokenlar to'ldirilgan.
        var fileUrl = row.GetProperty("fileUrl").GetString()!;
        Assert.StartsWith("/uploads/", fileUrl);
        var text = ReadDocxText(await File.ReadAllBytesAsync(UploadPath(fileUrl)));
        Assert.Contains($"Shartnoma № {nextNumber}", text);
        Assert.Contains($"Shartnoma {tag}", text);
        Assert.Contains("15.09.2026", text);
        Assert.DoesNotContain("@", text);

        // Keyingi hosil qilish yangi raqam oladi.
        var second = await client.PostAsJsonAsync($"{Url}/generate",
            new { studentId = student, templateId = template });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(nextNumber, (await JsonAsync(second)).GetProperty("number").GetString());

        // Band raqam — 400.
        var taken = await client.PostAsJsonAsync($"{Url}/generate",
            new { studentId = student, templateId = template, number = nextNumber });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal(StudentContractsController.NumberTakenMessage, await MessageAsync(taken));

        // Andozasiz — 400 (fayl hosil qilinmaydi).
        var noTemplate = await client.PostAsJsonAsync($"{Url}/generate", new { studentId = student });
        Assert.Equal(HttpStatusCode.BadRequest, noTemplate.StatusCode);
        Assert.Equal(StudentContractsController.TemplateNotFoundMessage, await MessageAsync(noTemplate));
    }

    /// <summary>
    /// MAVJUD GENERATOR REGRESSIYASI: ota-ona andozasidagi beshta eski token
    /// avvalgidek almashadi, noma'lum token esa avvalgidek TEGILMAYDI.
    /// </summary>
    [Fact]
    public void Generator_avvalgidek_toldiradi()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ContractService>();

        var docx = BuildDocx("@ota_ona / @telefon / @farzandlar / @sana / @raqam / @nomalum");
        var filled = service.FillTemplate(docx, new Dictionary<string, string>
        {
            ["@ota_ona"] = "Olimov Olim",
            ["@telefon"] = "+998901112233",
            ["@farzandlar"] = "Olimov Ali (5-A)",
            ["@sana"] = "15.09.2026",
            ["@raqam"] = "42",
        });

        Assert.Equal(
            "Olimov Olim / +998901112233 / Olimov Ali (5-A) / 15.09.2026 / 42 / @nomalum",
            ReadDocxText(filled));
    }

    /// <summary>
    /// Tokenlar o'quvchi va VASIYLARDAN to'ldiriladi; ota/ona alohida chiqadi,
    /// vasiy umuman bo'lmasa — o'quvchi qatoridagi eski `parent_*` maydonlari.
    /// </summary>
    [Fact]
    public void Tokenlar_vasiylardan_toldiriladi()
    {
        var student = GeneralSettingsFlagsTests.NewStudent("Olimov Ali", "5-A", "+998901112233");
        student.Phone = "+998907778899";

        var withGuardians = StudentContractTokens.Build(
            student,
            [
                (GuardianRelation.Father, "Olimov Olim", "+998901111111"),
                (GuardianRelation.Mother, "Karimova Dilnoza", "+998902222222"),
            ],
            "42", new DateOnly(2026, 9, 15), new DateOnly(2027, 5, 31));

        Assert.Equal("Olimov Ali", withGuardians["@oquvchi"]);
        Assert.Equal("5-A", withGuardians["@sinf"]);
        Assert.Equal("+998907778899", withGuardians["@oquvchi_telefon"]);
        Assert.Equal("Olimov Olim", withGuardians["@ota"]);
        Assert.Equal("Karimova Dilnoza", withGuardians["@ona"]);
        Assert.Equal("Olimov Olim", withGuardians["@ota_ona"]);
        Assert.Equal("15.09.2026", withGuardians["@sana"]);
        Assert.Equal("31.05.2027", withGuardians["@tugash_sana"]);
        Assert.Equal("01.01.2012", withGuardians["@tugilgan_kun"]);

        var without = StudentContractTokens.Build(student, [], "43", new DateOnly(2026, 9, 15), null);
        Assert.Equal("Ota-ona", without["@ota_ona"]);
        Assert.Equal("+998901112233", without["@telefon"]);
        Assert.Equal("", without["@ota"]);
        Assert.Equal("", without["@tugash_sana"]);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "K" + Guid.NewGuid().ToString("N")[..8];

    private async Task<string> SeedStudentAsync(string tag, string? className = null)
    {
        var student = GeneralSettingsFlagsTests.NewStudent(
            $"Shartnoma {tag}", className ?? $"SH-{tag[..4]}", "+998900000007");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return student.Id;
    }

    /// <summary>
    /// Haqiqiy .docx andozani <c>uploads/</c> ga yozadi va uning yozuvini
    /// qo'shadi — hosil qilish yo'li mavjud <c>ContractService.ReadTemplate</c>
    /// orqali o'qishini tekshirish uchun.
    /// </summary>
    private async Task<string> SeedTemplateAsync(string tag, string text)
    {
        var fileName = $"tpl-{tag}.docx";
        var dir = Path.Combine(ContentRoot(), "uploads");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), BuildDocx(text));

        var template = new ContractTemplate
        {
            Target = "parent",
            Name = $"Sinov andoza {tag}",
            FileUrl = $"/uploads/{fileName}",
            FileName = fileName,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.ContractTemplates.Add(template);
            await db.SaveChangesAsync();
        });
        return template.Id;
    }

    private string ContentRoot() =>
        fixture.Api.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;

    private string UploadPath(string fileUrl) =>
        Path.Combine(ContentRoot(), "uploads", Path.GetFileName(fileUrl));

    private static byte[] BuildDocx(string text)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static string ReadDocxText(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        return string.Concat(doc.MainDocumentPart!.Document.Body!.Descendants<Text>().Select(t => t.Text));
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> ItemsAsync(HttpClient client, string url)
    {
        var page = await JsonAsync(await client.GetAsync(url));
        return [.. page.GetProperty("items").EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<List<JsonElement>> StudentRowsAsync(HttpClient client, string studentId)
    {
        var response = await client.GetAsync($"/api/admin/students/{studentId}/contracts");
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
