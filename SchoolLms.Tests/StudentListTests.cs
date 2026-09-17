using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quvchilar ro'yxati — server tarafdagi filtr, tartib, sahifa, eksport va
/// ommaviy o'chirish (docs/modules/students-parity.md §2.3 S-1..S-4, S-7, K-4;
/// §2.4 A-1).
///
/// <para>
/// <b>Umumiy bazada qanday izolyatsiya.</b> Testlar bitta bazani bo'lishadi,
/// shuning uchun har test o'z o'quvchilarini TAKRORLANMAS teg bilan yaratadi
/// va har so'rovga <c>search=&lt;teg&gt;</c> qo'shadi. Natijada tekshiruv
/// faqat o'z qatorlari ustida boradi — va shu bilan birga qidiruv filtri ham
/// har testda sinaladi.
/// </para>
/// <para>
/// <b>Har filtr nimani va'da qilsa, shuni toraytirishi</b> alohida
/// tekshiriladi: filtr qo'yilgach qaytgan to'plam kutilgan to'plamning AYNAN
/// o'zi bo'lishi shart (kamida emas, ko'pida emas).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentListTests(ApiFixture fixture)
{
    private const string Search = "/api/admin/students/search";
    private const string Export = "/api/admin/students/search/export";
    private const string DeleteMany = "/api/admin/students/delete-many";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Search)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Export)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(DeleteMany, new { studentIds = new[] { "x" } })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Search)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(DeleteMany, new { studentIds = new[] { "x" } })).StatusCode);
    }

    /// <summary>
    /// Xodim ro'yxatni O'QIYDI (boshqa bo'lim sahifasi ham unga tayanadi), lekin
    /// "students" ruxsatisiz ommaviy o'chira olmaydi.
    /// </summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_ochirmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Search)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Export)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(DeleteMany, new { studentIds = new[] { "x" } })).StatusCode);
    }

    // =====================================================================
    //  2. FILTRSIZ SO'ROV — BUGUNGI XATTI-HARAKAT
    // =====================================================================

    /// <summary>
    /// §4.2 qoidasi: filtrsiz ro'yxat bugungi <c>GET /api/admin/students</c>
    /// bilan AYNAN bir xil to'plamni, AYNAN bir xil tartibda beradi.
    /// </summary>
    [Fact]
    public async Task Filtrsiz_royxat_eski_endpoint_bilan_bir_xil()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        await SeedAsync(tag, "Bavvv", archived: false);
        await SeedAsync(tag, "Aaaaa", archived: false);
        await SeedAsync(tag, "Caaaa", archived: true);

        var old = await client.GetFromJsonAsync<List<JsonElement>>("/api/admin/students");
        var oldNames = old!.Select(x => x.GetProperty("fullName").GetString()!)
            .Where(n => n.Contains(tag, StringComparison.Ordinal)).ToList();

        var page = await PageAsync(client, $"{Search}?search={tag}&pageSize=1000");
        var newNames = page.Items.Select(i => i.GetProperty("fullName").GetString()!).ToList();

        Assert.Equal(oldNames, newNames);
        // Arxivlangani faol ro'yxatda YO'Q — eski endpoint ham shunday.
        Assert.DoesNotContain(newNames, n => n.Contains("Caaaa", StringComparison.Ordinal));
    }

    /// <summary>Arxiv tab'i: eski <c>archived</c> endpoint'i bilan bir xil tartib.</summary>
    [Fact]
    public async Task Arxiv_royxati_eski_endpoint_bilan_bir_xil()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        await SeedAsync(tag, "Bir", archived: true, archivedAt: "2026-01-10");
        await SeedAsync(tag, "Ikki", archived: true, archivedAt: "2026-05-20");

        var old = await client.GetFromJsonAsync<List<JsonElement>>("/api/admin/students/archived");
        var oldNames = old!.Select(x => x.GetProperty("fullName").GetString()!)
            .Where(n => n.Contains(tag, StringComparison.Ordinal)).ToList();

        var page = await PageAsync(client, $"{Search}?state=archived&search={tag}&pageSize=1000");
        Assert.Equal(oldNames, page.Items.Select(i => i.GetProperty("fullName").GetString()!).ToList());
        // Eng yangi arxiv tepada.
        Assert.Contains("Ikki", page.Items[0].GetProperty("fullName").GetString());
    }

    // =====================================================================
    //  3. HAR FILTR NIMANI VA'DA QILSA — SHUNI TORAYTIRADI
    // =====================================================================

    [Fact]
    public async Task Sinf_daraja_jins_va_til_filtrlari_toraytiradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var (five, nine) = await SeedClassesAsync(tag);

        var a = await SeedAsync(tag, "Besh qiz", className: five, gender: "female", language: "uz");
        var b = await SeedAsync(tag, "Besh ogil", className: five, gender: "male", language: "ru");
        var c = await SeedAsync(tag, "Toqqiz ogil", className: nine, gender: "male", language: "uz");

        await AssertIdsAsync(client, $"{Search}?search={tag}&className={five}", a, b);
        await AssertIdsAsync(client, $"{Search}?search={tag}&grades=9", c);
        await AssertIdsAsync(client, $"{Search}?search={tag}&grades=5,9", a, b, c);
        await AssertIdsAsync(client, $"{Search}?search={tag}&gender=female", a);
        await AssertIdsAsync(client, $"{Search}?search={tag}&language=ru", b);
        await AssertIdsAsync(client, $"{Search}?search={tag}&grades=5&gender=male", b);
    }

    [Fact]
    public async Task Yosh_qabul_sanasi_va_holat_filtrlari_toraytiradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var today = AppClock.Today;

        var young = await SeedAsync(tag, "Kichik",
            birthDate: today.AddYears(-8).ToString("yyyy-MM-dd"), enrollment: "2024-09-01");
        var old = await SeedAsync(tag, "Katta",
            birthDate: today.AddYears(-16).ToString("yyyy-MM-dd"), enrollment: "2026-02-01");

        await AssertIdsAsync(client, $"{Search}?search={tag}&ageTo=10", young);
        await AssertIdsAsync(client, $"{Search}?search={tag}&ageFrom=12", old);
        await AssertIdsAsync(client, $"{Search}?search={tag}&ageFrom=7&ageTo=17", young, old);
        await AssertIdsAsync(client, $"{Search}?search={tag}&enrolledFrom=2026-01-01", old);
        await AssertIdsAsync(client, $"{Search}?search={tag}&enrolledTo=2025-01-01", young);

        // Holat: qo'yilgan / qo'yilmagan / aniq id.
        var status = await CreateStatusAsync(client, $"VIP {tag}");
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/admin/students/{young}/status", new { statusId = status })).StatusCode);

        await AssertIdsAsync(client, $"{Search}?search={tag}&statusId={status}", young);
        await AssertIdsAsync(client, $"{Search}?search={tag}&hasStatus=false", old);
        await AssertIdsAsync(client, $"{Search}?search={tag}&hasStatus=true", young);
    }

    /// <summary>
    /// Pulga oid filtrlar: qarzdorlar, eng kam qarz va qoldiq oralig'i. Qarz
    /// HAQIQIY yo'l bilan tug'iladi — ochiq hisob-faktura (qoldiq ustun emas).
    /// </summary>
    [Fact]
    public async Task Balans_filtrlari_va_yakunlar()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var clean = await SeedAsync(tag, "Qarzsiz");
        var small = await SeedAsync(tag, "Kichik qarz", debt: 100_000m);
        var big = await SeedAsync(tag, "Katta qarz", debt: 900_000m);

        await AssertIdsAsync(client, $"{Search}?search={tag}&balanceState=debt", small, big);
        await AssertIdsAsync(client, $"{Search}?search={tag}&balanceState=paid", clean);
        await AssertIdsAsync(client, $"{Search}?search={tag}&minDebt=500000", big);
        await AssertIdsAsync(client, $"{Search}?search={tag}&balanceFrom=-200000", clean, small);
        await AssertIdsAsync(client, $"{Search}?search={tag}&balanceTo=-500000", big);

        var page = await PageAsync(client, $"{Search}?search={tag}&pageSize=1000");
        Assert.Equal(1_000_000m, page.TotalDebt);
        Assert.Equal(0m, page.TotalCredit);
    }

    /// <summary>K-4 — shartnomasi bor / yo'q filtri va shartnoma raqami ustuni.</summary>
    [Fact]
    public async Task Shartnoma_filtri_va_raqam_ustuni()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var withContract = await SeedAsync(tag, "Shartnomali");
        var without = await SeedAsync(tag, "Shartnomasiz");

        var (author, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.StudentContracts.Add(new StudentContract
            {
                StudentId = withContract,
                Number = $"SH-{tag}",
                SignedOn = new DateOnly(2026, 1, 15),
                Source = StudentContractSource.Uploaded,
                CreatedBy = author.Id,
                CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
        });

        await AssertIdsAsync(client, $"{Search}?search={tag}&hasContract=true", withContract);
        await AssertIdsAsync(client, $"{Search}?search={tag}&hasContract=false", without);

        var page = await PageAsync(client, $"{Search}?search={tag}&hasContract=true");
        Assert.Equal($"SH-{tag}", page.Items[0].GetProperty("contractNumber").GetString());
        Assert.True(page.Items[0].GetProperty("hasContract").GetBoolean());
    }

    /// <summary>A-1 — arxiv sanasi oralig'i va arxiv sababi bo'yicha filtr.</summary>
    [Fact]
    public async Task Arxiv_sana_va_sabab_filtrlari()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var graduated = new Guid("00000000-0000-0000-0000-0000000000a1");

        var early = await SeedAsync(tag, "Erta", archived: true, archivedAt: "2026-01-05");
        var late = await SeedAsync(tag, "Kech", archived: true, archivedAt: "2026-06-05",
            archiveReasonId: graduated);

        await AssertIdsAsync(client, $"{Search}?state=archived&search={tag}&archivedFrom=2026-03-01", late);
        await AssertIdsAsync(client, $"{Search}?state=archived&search={tag}&archivedTo=2026-03-01", early);
        await AssertIdsAsync(client, $"{Search}?state=archived&search={tag}&archiveReasonId={graduated}", late);
    }

    // =====================================================================
    //  4. TARTIB VA SAHIFA
    // =====================================================================

    [Fact]
    public async Task Tartib_va_sahifalash()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        await SeedAsync(tag, "Aaa", debt: 50_000m);
        await SeedAsync(tag, "Bbb", debt: 10_000m);
        await SeedAsync(tag, "Ccc");

        var byName = await PageAsync(client, $"{Search}?search={tag}&sortBy=fullName&sortOrder=desc");
        Assert.Equal(3, byName.Total);
        Assert.Contains("Ccc", byName.Items[0].GetProperty("fullName").GetString());

        var byBalance = await PageAsync(client, $"{Search}?search={tag}&sortBy=balance");
        // Eng manfiy (eng qarzdor) tepada.
        Assert.Contains("Aaa", byBalance.Items[0].GetProperty("fullName").GetString());

        var first = await PageAsync(client, $"{Search}?search={tag}&pageSize=2&page=1");
        var second = await PageAsync(client, $"{Search}?search={tag}&pageSize=2&page=2");
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(Id).Intersect(second.Items.Select(Id)));
    }

    // =====================================================================
    //  5. EKSPORT
    // =====================================================================

    /// <summary>
    /// Eksport FILTRLANGAN to'plamni beradi va uning ustunlari import
    /// shabloni bilan mos (aylanish testi <c>StudentImportTests</c> da).
    /// </summary>
    [Fact]
    public async Task Eksport_filtrlangan_toplamni_beradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        await SeedAsync(tag, "Eksport bir", gender: "male");
        await SeedAsync(tag, "Eksport ikki", gender: "female");

        var response = await client.GetAsync($"{Export}?search={tag}&gender=female");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var rows = XlsxRows(await response.Content.ReadAsByteArrayAsync());
        // 1-qator sarlavha + bitta ma'lumot qatori.
        Assert.Equal(2, rows.Count);
        Assert.Contains("Eksport ikki", rows[1][0]);
        Assert.Equal("qiz", rows[1][3]);
    }

    // =====================================================================
    //  6. OMMAVIY BUTUNLAY O'CHIRISH (S-7)
    // =====================================================================

    /// <summary>
    /// Tanlanganlar AYNAN o'chiriladi — boshqalari tegilmaydi.
    /// </summary>
    [Fact]
    public async Task Ommaviy_ochirish_faqat_tanlanganlarga_tegadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var a = await SeedAsync(tag, "O'chadi bir", archived: true);
        var b = await SeedAsync(tag, "O'chadi ikki", archived: true);
        var keep = await SeedAsync(tag, "Qoladi", archived: true);

        var response = await client.PostAsJsonAsync(DeleteMany, new { studentIds = new[] { a, b } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, body.RootElement.GetProperty("deleted").GetInt32());
            Assert.Empty(body.RootElement.GetProperty("blocked").EnumerateArray());
        }

        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.Students.AnyAsync(s => s.Id == a || s.Id == b));
            Assert.True(await db.Students.AnyAsync(s => s.Id == keep));
        });
    }

    /// <summary>
    /// Moliyaviy yozuvi bor bitta o'quvchi BUTUN amalni to'xtatadi: hech kim
    /// o'chmaydi va javobda aynan kim to'sganini ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task Moliyaviy_yozuvi_bori_hammasini_toxtatadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var clean = await SeedAsync(tag, "Toza", archived: true);
        var moneyed = await SeedAsync(tag, "Pulli", archived: true, debt: 250_000m);

        var response = await client.PostAsJsonAsync(DeleteMany, new { studentIds = new[] { clean, moneyed } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, body.RootElement.GetProperty("deleted").GetInt32());
            var blocked = Assert.Single(body.RootElement.GetProperty("blocked").EnumerateArray());
            Assert.Equal(moneyed, blocked.GetProperty("studentId").GetString());
        }

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(2, await db.Students.CountAsync(s => s.Id == clean || s.Id == moneyed)));
    }

    [Fact]
    public async Task Bosh_royxat_rad_etiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(DeleteMany, new { studentIds = Array.Empty<string>() })).StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    internal static string Tag() => "L" + Guid.NewGuid().ToString("N")[..8];

    private static string Id(JsonElement row) => row.GetProperty("id").GetString()!;

    internal sealed record Page(List<JsonElement> Items, int Total, decimal TotalDebt, decimal TotalCredit);

    internal static async Task<Page> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return new Page(
            [.. root.GetProperty("items").EnumerateArray().Select(e => e.Clone())],
            root.GetProperty("total").GetInt32(),
            root.GetProperty("totalDebt").GetDecimal(),
            root.GetProperty("totalCredit").GetDecimal());
    }

    /// <summary>Filtr AYNAN shu o'quvchilarni qaytarishini tekshiradi (kam ham, ko'p ham emas).</summary>
    private static async Task AssertIdsAsync(HttpClient client, string url, params string[] expected)
    {
        var page = await PageAsync(client, url + "&pageSize=1000");
        Assert.Equal(
            expected.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            page.Items.Select(Id).OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    private async Task<Guid> CreateStatusAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/admin/student-statuses", new { name });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Task.CompletedTask;
        return json.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>5- va 9-sinf — daraja filtri uchun haqiqiy sinf qatorlari.</summary>
    private async Task<(string Five, string Nine)> SeedClassesAsync(string tag)
    {
        var five = $"5-{tag}";
        var nine = $"9-{tag}";
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(new SchoolClass { Name = five, Grade = 5 });
            db.Classes.Add(new SchoolClass { Name = nine, Grade = 9 });
            await db.SaveChangesAsync();
        });
        return (five, nine);
    }

    /// <summary>billing_seed.sql dagi barqaror "O'qish" toifasi.</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    private async Task<string> SeedAsync(
        string tag,
        string name,
        string className = "",
        string gender = "male",
        string? language = null,
        string birthDate = "2012-01-01",
        string enrollment = "2025-09-01",
        bool archived = false,
        string? archivedAt = null,
        Guid? archiveReasonId = null,
        decimal debt = 0m)
    {
        var student = GeneralSettingsFlagsTests.NewStudent(
            $"{name} {tag}", className.Length == 0 ? $"K-{tag}" : className, "+998900000001");
        student.Gender = gender;
        student.Language = language;
        student.BirthDate = birthDate;
        student.EnrollmentDate = enrollment;
        student.IsArchived = archived;
        student.ArchivedAt = archived ? (archivedAt ?? "2026-03-01") : null;
        student.ArchiveReason = archived ? "Test" : null;
        student.ArchiveReasonId = archiveReasonId;

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            if (debt > 0)
            {
                var month = new DateOnly(2026, 9, 1);
                db.Invoices.Add(new Invoice
                {
                    StudentId = student.Id,
                    CategoryId = TuitionCategory,
                    PeriodMonth = month,
                    Amount = debt,
                    Discount = 0m,
                    DueOn = month.AddDays(9),
                    Status = InvoiceStatus.Open,
                    CreatedAt = AppClock.NowInstant,
                });
            }
            await db.SaveChangesAsync();
        });
        return student.Id;
    }

    /// <summary>.xlsx baytlaridan 1-varaqning qatorlarini o'qiydi (test uchun).</summary>
    internal static List<string[]> XlsxRows(byte[] bytes, int columns = 15)
    {
        using var stream = new MemoryStream(bytes);
        return SchoolLms.Application.Services.ExcelImport.ReadRows(stream, columns);
    }
}
