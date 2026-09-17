using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Ikki bosqichli import va eksport (docs/modules/students-parity.md §2.3, S-2, S-3).
///
/// <para>
/// <b>Eng muhim tekshiruv:</b> bitta yomon qatorli fayl HECH NARSA yozmaydi.
/// Yarim import — administrator uchun eng yomon natija: qaysi bola tushgani
/// noma'lum bo'lib qoladi va faylni qayta yuklash dublikat yaratadi.
/// </para>
/// <para>
/// <b>Aylanish (round-trip):</b> eksport qilingan faylni import'ga qaytarish
/// birorta yangi o'quvchi YARATMAYDI — hammasi "yangilandi" bo'lib o'tadi.
/// Bu ikkita narsani bir vaqtda isbotlaydi: eksport ustunlari import shabloni
/// bilan mos, va takrorni topish (upsert) ishlaydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentImportTests(ApiFixture fixture)
{
    private const string Template = "/api/admin/students/import/shablon";
    private const string Validate = "/api/admin/students/import/tekshirish";
    private const string Commit = "/api/admin/students/import/tasdiqlash";
    private const string Export = "/api/admin/students/search/export";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Template)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync(Validate, Upload(Sheet([])))).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Template)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync(Commit, Upload(Sheet([])))).StatusCode);
    }

    /// <summary>Xodim shablonni yuklab oladi, lekin "students" ruxsatisiz import qila olmaydi.</summary>
    [Fact]
    public async Task Xodim_shablonni_oladi_lekin_import_qilmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Template)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync(Validate, Upload(Sheet([])))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync(Commit, Upload(Sheet([])))).StatusCode);
    }

    // =====================================================================
    //  2. SHABLON
    // =====================================================================

    [Fact]
    public async Task Shablon_ustunlari_import_bilan_bir_xil()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync(Template);
        response.EnsureSuccessStatusCode();
        var rows = StudentListTests.XlsxRows(
            await response.Content.ReadAsByteArrayAsync(), StudentImportSheet.Headers.Length);

        Assert.NotEmpty(rows);
        Assert.Equal(StudentImportSheet.Headers, rows[0]);
    }

    // =====================================================================
    //  3. TEKSHIRISH — BUTUN FAYL
    // =====================================================================

    [Fact]
    public async Task Toza_fayl_tekshiruvdan_otadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);

        var preview = await PreviewAsync(client, Validate, Sheet([
            Row($"Aliyev Vali {tag}", cls, "2015-03-21", "o'g'il", "Toshkent", "Aliyev Ota", "+998901112233", "2025-09-01"),
            Row($"Karimova Zilola {tag}", cls, "2016-07-02", "qiz", "Toshkent", "Karimov Ota", "+998901112244", "2025-09-01"),
        ]));

        Assert.True(preview.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(2, preview.RootElement.GetProperty("created").GetInt32());
        Assert.Equal(0, preview.RootElement.GetProperty("updated").GetInt32());
        Assert.Empty(preview.RootElement.GetProperty("errors").EnumerateArray());

        // Tekshirish HECH NARSA yozmaydi.
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(0, await db.Students.CountAsync(s => s.ClassName == cls)));
    }

    /// <summary>
    /// Har xato o'z QATOR raqami va USTUN nomi bilan qaytadi — administrator
    /// faylni ochib, aynan o'sha katakni tuzatadi.
    /// </summary>
    [Fact]
    public async Task Har_xato_qator_raqami_va_ustun_nomi_bilan_qaytadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);

        var preview = await PreviewAsync(client, Validate, Sheet([
            Row($"Toza qator {tag}", cls, "2015-03-21", "o'g'il", "", "", "", ""),
            Row($"Sinfsiz {tag}", $"YO'Q-{tag}", "2015-03-21", "o'g'il", "", "", "", ""),
            Row("", cls, "", "", "", "", "", ""),
            Row($"Sana buzuq {tag}", cls, "kecha", "", "", "", "", ""),
            Row($"Til buzuq {tag}", cls, "", "", "", "", "", "", "", "chinese"),
        ]));

        Assert.False(preview.RootElement.GetProperty("ok").GetBoolean());
        var errors = preview.RootElement.GetProperty("errors").EnumerateArray().ToList();

        // Excel qator raqamlari: sarlavha 1-qator, ma'lumot 2-qatordan.
        Assert.Contains(errors, e => e.GetProperty("row").GetInt32() == 3
                                     && e.GetProperty("column").GetString() == StudentImportSheet.Headers[1]);
        Assert.Contains(errors, e => e.GetProperty("row").GetInt32() == 4
                                     && e.GetProperty("column").GetString() == StudentImportSheet.Headers[0]);
        Assert.Contains(errors, e => e.GetProperty("row").GetInt32() == 5
                                     && e.GetProperty("column").GetString() == StudentImportSheet.Headers[2]);
        Assert.Contains(errors, e => e.GetProperty("row").GetInt32() == 6
                                     && e.GetProperty("column").GetString() == StudentImportSheet.Headers[9]);
    }

    /// <summary>Bitta faylda bir xil o'quvchi ikki marta — ikkinchisi xato (jimgina dublikat emas).</summary>
    [Fact]
    public async Task Fayl_ichidagi_takror_xato()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);
        var row = Row($"Takror {tag}", cls, "2015-03-21", "o'g'il", "", "", "", "");

        var preview = await PreviewAsync(client, Validate, Sheet([row, row]));

        Assert.False(preview.RootElement.GetProperty("ok").GetBoolean());
        var error = Assert.Single(preview.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal(3, error.GetProperty("row").GetInt32());
        Assert.Contains("takrorlanyapti", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Notogri_sarlavha_va_notogri_fayl_rad_etiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var wrongHeader = ExcelExport.Build("O'quvchilar",
            new[] { "Ism", "Familiya", "Telefon" },
            new[] { (IReadOnlyList<string>)new[] { "Vali", "Aliyev", "+998901112233" } });
        var refused = await client.PostAsync(Validate, Upload(wrongHeader));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        using (var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(StudentImportService.BadHeaderMessage,
                body.RootElement.GetProperty("message").GetString());
        }

        var notXlsx = await client.PostAsync(Validate, Upload(Encoding.UTF8.GetBytes("salom"), "royxat.csv"));
        Assert.Equal(HttpStatusCode.BadRequest, notXlsx.StatusCode);
    }

    // =====================================================================
    //  4. TASDIQLASH — HAMMASI YOKI HECH NIMA
    // =====================================================================

    /// <summary>
    /// BITTA yomon qator butun faylni to'xtatadi: javobda o'sha qator
    /// ko'rsatiladi va bazaga HECH NARSA yozilmaydi — to'g'ri qatorlar ham.
    /// </summary>
    [Fact]
    public async Task Bitta_yomon_qator_hech_narsa_yozilmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);

        var response = await client.PostAsync(Commit, Upload(Sheet([
            Row($"Yaxshi bir {tag}", cls, "2015-03-21", "o'g'il", "", "", "", ""),
            Row($"Yomon {tag}", $"YO'Q-{tag}", "2015-03-21", "o'g'il", "", "", "", ""),
            Row($"Yaxshi ikki {tag}", cls, "2015-03-22", "qiz", "", "", "", ""),
        ])));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
            var error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal(3, error.GetProperty("row").GetInt32());
            Assert.Contains($"YO'Q-{tag}", error.GetProperty("message").GetString());
        }

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(0, await db.Students.CountAsync(s => s.ClassName == cls)));
    }

    /// <summary>
    /// Toza fayl yoziladi: yangi maydonlar (telefon, til, holat) ham tushadi,
    /// tizim akkaunti ochiladi, vasiy qatori chiqariladi.
    /// </summary>
    [Fact]
    public async Task Toza_fayl_yoziladi_va_yangi_maydonlar_tushadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);
        var statusName = $"VIP {tag}";
        await client.PostAsJsonAsync("/api/admin/student-statuses", new { name = statusName });

        var response = await client.PostAsync(Commit, Upload(Sheet([
            Row($"Aliyev Vali Aliyevich {tag}", cls, "2015-03-21", "o'g'il", "Toshkent",
                "Aliyev Ota Valiyevich", "+998901112233", "2025-09-01",
                "+998931112233", "ru", statusName),
        ])));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, body.RootElement.GetProperty("created").GetInt32());
            Assert.Equal(0, body.RootElement.GetProperty("updated").GetInt32());
        }

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.ClassName == cls);
            Assert.Equal("+998931112233", student.Phone);
            Assert.Equal("ru", student.Language);
            Assert.NotNull(student.StatusId);
            Assert.NotNull(student.UserId);
            // F.I.SH bo'laklarga ajraladi (eski import buni qilmasdi).
            Assert.Equal("Aliyev", student.LastName);
            Assert.Equal("Vali", student.FirstName);
            // Vasiy ota-ona raqamidan chiqariladi (SPEC §3.2).
            Assert.True(await db.StudentGuardians.AnyAsync(g => g.StudentId == student.Id));
        });
    }

    /// <summary>
    /// Ikkinchi marta yuklash DUBLIKAT yaratmaydi — F.I.SH + tug'ilgan sana +
    /// sinf bo'yicha topilgan o'quvchi YANGILANADI. Bo'sh katak esa mavjud
    /// qiymatni tozalamaydi.
    /// </summary>
    [Fact]
    public async Task Ikkinchi_yuklash_yangilaydi_va_bosh_katak_tozalamaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);

        var first = Sheet([
            Row($"Karimov Botir {tag}", cls, "2014-02-02", "o'g'il", "Chilonzor",
                "Karimov Ota", "+998901112255", "2025-09-01", "+998931112255", "uz"),
        ]);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Commit, Upload(first))).StatusCode);

        // Ikkinchi fayl: manzil va telefon ustunlari BO'SH, jinsi o'zgargan.
        var second = Sheet([
            Row($"Karimov Botir {tag}", cls, "2014-02-02", "qiz", "", "", "", ""),
        ]);
        var response = await client.PostAsync(Commit, Upload(second));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, body.RootElement.GetProperty("created").GetInt32());
            Assert.Equal(1, body.RootElement.GetProperty("updated").GetInt32());
        }

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.ClassName == cls);
            Assert.Equal("female", student.Gender);          // to'ldirilgan ustun yozildi
            Assert.Equal("Chilonzor", student.Address);      // bo'sh katak tozalamadi
            Assert.Equal("+998931112255", student.Phone);    // bo'sh katak tozalamadi
        });
    }

    // =====================================================================
    //  5. EKSPORT → IMPORT AYLANISHI
    // =====================================================================

    [Fact]
    public async Task Eksport_importga_qaytadi_va_dublikat_yaratmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Commit, Upload(Sheet([
            Row($"Aylanish bir {tag}", cls, "2013-04-04", "o'g'il", "Yunusobod",
                "Ota Bir", "+998901112266", "2025-09-01", "+998931112266", "uz"),
            Row($"Aylanish ikki {tag}", cls, "2013-05-05", "qiz", "Mirzo Ulug'bek",
                "Ota Ikki", "+998901112277", "2025-09-02"),
        ])))).StatusCode);

        var export = await client.GetAsync($"{Export}?search={tag}&pageSize=1000");
        export.EnsureSuccessStatusCode();
        var bytes = await export.Content.ReadAsByteArrayAsync();

        var response = await client.PostAsync(Commit, Upload(bytes));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, body.RootElement.GetProperty("created").GetInt32());
            Assert.Equal(2, body.RootElement.GetProperty("updated").GetInt32());
        }

        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.Students.AsNoTracking().Where(s => s.ClassName == cls).ToListAsync();
            Assert.Equal(2, rows.Count);
            var first = rows.Single(s => s.FullName.StartsWith("Aylanish bir", StringComparison.Ordinal));
            Assert.Equal("Yunusobod", first.Address);
            Assert.Equal("+998931112266", first.Phone);
            Assert.Equal("uz", first.Language);
            Assert.Equal("male", first.Gender);
            Assert.Equal("2013-04-04", first.BirthDate);
        });
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "I" + Guid.NewGuid().ToString("N")[..8];

    private async Task<string> SeedClassAsync(string tag)
    {
        var name = $"IM-{tag}";
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(new SchoolClass { Name = name, Grade = 7 });
            await db.SaveChangesAsync();
        });
        return name;
    }

    /// <summary>Shablon ustunlari tartibida bitta qator (yetishmagan ustunlar bo'sh).</summary>
    private static IReadOnlyList<string> Row(params string[] cells)
    {
        var row = new string[StudentImportSheet.Headers.Length];
        for (var i = 0; i < row.Length; i++) row[i] = i < cells.Length ? cells[i] : "";
        return row;
    }

    private static byte[] Sheet(IReadOnlyList<IReadOnlyList<string>> rows) =>
        ExcelExport.Build(StudentImportSheet.SheetName, StudentImportSheet.Headers, rows);

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName = "oquvchilar.xlsx")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<JsonDocument> PreviewAsync(
        HttpClient client, string url, byte[] bytes)
    {
        var response = await client.PostAsync(url, Upload(bytes));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
