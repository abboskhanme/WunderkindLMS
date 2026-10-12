using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Groups to Excel, like the classes list (client, 2026-10-12: "guruhlarni ichidan ham xuddi
//  sinflar kabi ro'yxatni yuklab olish mumkin bo'lsin"): the groups list with the screen's
//  filters, and one group's pupils (with leavers when "Tarix bilan" is on).
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class StudyGroupExportTests(ApiFixture fixture)
{
    private const string Groups = "/api/admin/study-groups";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public async Task Guruhlar_royxati_filtr_bilan_excelga_tushadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await CreateAsync(admin, w, $"Ingliz A {w.Tag}", [w.StudentA, w.StudentB]);

        var res = await admin.GetAsync($"{Groups}/export?search={w.Tag}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(XlsxMime, res.Content.Headers.ContentType?.MediaType);

        var rows = ReadSheet(await res.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("O'quv guruhlari", rows[0][0]);
        Assert.Equal(["№", "Guruh", "Fan", "Sinflar", "O'qituvchilar", "O'quvchilar soni", "Holat"], rows[2]);
        var row = Assert.Single(rows.Skip(3));
        Assert.Equal(["1", $"Ingliz A {w.Tag}", $"Ingliz tili {w.Tag}", $"G{w.Tag}-A, G{w.Tag}-B",
            $"Guruh ustozi {w.Tag}", "2", "Faol"], row);
    }

    [Fact]
    public async Task Guruh_ichidagi_oquvchilar_royxati_tarix_bilan_va_tarixsiz()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var group = await CreateAsync(admin, w, $"Ingliz B {w.Tag}", [w.StudentA, w.StudentB]);

        // Bobur leaves the group: the plain list drops him, the history list keeps him with the reason.
        var members = await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}/{group}/members");
        var bobur = members!.Single(m => m.GetProperty("studentId").GetString() == w.StudentB).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Groups}/{group}/members/{bobur}/remove", new { reason = "Boshqa guruhga" })).StatusCode);

        var res = await admin.GetAsync($"{Groups}/{group}/members/export");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(XlsxMime, res.Content.Headers.ContentType?.MediaType);
        var rows = ReadSheet(await res.Content.ReadAsByteArrayAsync());
        Assert.Contains($"Ingliz B {w.Tag}", rows[0][0]);
        Assert.Contains("1 ta o'quvchi", rows[0][0]);
        Assert.Equal(["№", "O'quvchi", "Sinf", "Qo'shilgan"], rows[2]);
        var only = Assert.Single(rows.Skip(3));
        Assert.Equal($"Ali {w.Tag}", only[1]);
        Assert.Equal($"G{w.Tag}-A", only[2]);

        var history = ReadSheet(await (await admin.GetAsync($"{Groups}/{group}/members/export?includeHistory=true"))
            .Content.ReadAsByteArrayAsync());
        Assert.Equal(["№", "O'quvchi", "Sinf", "Qo'shilgan", "Chiqqan", "Sabab"], history[2]);
        Assert.Equal(2, history.Skip(3).Count());
        var left = history.Skip(3).Single(r => r[1] == $"Bobur {w.Tag}");
        Assert.Equal(AppClock.Today.ToString("dd.MM.yyyy"), left[4]);
        Assert.Equal("Boshqa guruhga", left[5]);
    }

    [Fact]
    public async Task Eksport_guruh_bolimi_darvozasidan_otadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var group = await CreateAsync(admin, w, $"Ingliz C {w.Tag}", [w.StudentA]);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher, "classes");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Groups}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Groups}/{group}/members/export")).StatusCode);

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Groups}/export")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Groups}/{Guid.NewGuid()}/members/export")).StatusCode);
    }

    // =====================================================================
    //  Helpers (the StudyGroupTests seed, trimmed)
    // =====================================================================

    private sealed record World(string Tag, string ClassA, string ClassB, string Subject, string Teacher, string StudentA, string StudentB);

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var classA = new SchoolClass { Name = $"G{tag}-A", Grade = 5 };
        var classB = new SchoolClass { Name = $"G{tag}-B", Grade = 5 };
        var subject = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };
        var teacher = new Teacher { FullName = $"Guruh ustozi {tag}" };
        var a = GeneralSettingsFlagsTests.NewStudent($"Ali {tag}", classA.Name, "+99890" + Random.Shared.Next(1_000_000, 9_999_999));
        var b = GeneralSettingsFlagsTests.NewStudent($"Bobur {tag}", classB.Name, "+99890" + Random.Shared.Next(1_000_000, 9_999_999));

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB);
            db.Subjects.Add(subject);
            db.Teachers.Add(teacher);
            db.Students.AddRange(a, b);
            await db.SaveChangesAsync();
        });
        return new World(tag, classA.Id, classB.Id, subject.Id, teacher.Id, a.Id, b.Id);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, World w, string name, IReadOnlyList<string> studentIds)
    {
        var res = await client.PostAsJsonAsync(Groups, new
        {
            name,
            subjectId = w.Subject,
            classIds = new[] { w.ClassA, w.ClassB },
            teacherIds = new[] { w.Teacher },
            studentIds,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Every row of the first sheet as text.</summary>
    private static List<List<string>> ReadSheet(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, false);
        var sheet = doc.WorkbookPart!.WorksheetParts.First().Worksheet;
        return [.. sheet.Descendants<Row>().Select(r => r.Elements<Cell>()
            .Select(c => c.DataType?.Value == CellValues.InlineString ? c.InlineString!.InnerText : c.CellValue?.Text ?? "")
            .ToList())];
    }
}
