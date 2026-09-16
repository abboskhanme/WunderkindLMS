using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Xulq-atvor: maktab bo'ylab "Harakatlar" lentasi (<c>GET /api/admin/discipline/feed</c>) va
/// ballar nazoratining Excel eksporti (<c>GET /api/admin/discipline/scores/export</c>).
///
/// <para>
/// Testlar UMUMIY bazada yuradi, shuning uchun har biri O'Z ma'lumotini TAKRORLANMAS sinf nomi
/// bilan ajratadi va har so'rovda <c>?className=</c> beradi — qo'shni testning yozuvlari
/// jamlamaga qo'shilib ketmasligi uchun. Yagona istisno — <c>authors</c> ro'yxati: u ataylab
/// butun bazadan yig'iladi, shuning uchun unga faqat "ichida bor" tekshiruvi qo'llanadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DisciplineFeedTests(ApiFixture fixture)
{
    private const string Feed = "/api/admin/discipline/feed";
    private const string ScoresExport = "/api/admin/discipline/scores/export";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    /// <summary>
    /// Lenta butun maktabning intizomiy tarixini ochadi — bu admin bo'limi ma'lumoti.
    /// <c>AdminPerm("discipline")</c> darvozasi o'qishni faqat admin/superadmin/xodimga beradi;
    /// o'qituvchi, kassir va o'quvchi — rad (403).
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_lentaga_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "discipline");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Feed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ScoresExport)).StatusCode);
    }

    /// <summary>Token'siz so'rov — 401 (403 emas): kim so'rayotgani umuman noma'lum.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Feed)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ScoresExport)).StatusCode);
    }

    /// <summary>Admin va superadmin — ha, mavjud endpoint'lar bilan bir xil darvoza.</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_lentani_ocha_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        var response = await client.GetAsync(Feed);

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}");
    }

    // =====================================================================
    //  2. Lenta — ikki manba, filtrlar, sahifalash
    // =====================================================================

    /// <summary>
    /// Lentaning asosiy va'dasi: bitta ro'yxatda IKKI manba — qo'lda kiritilgan ballar va
    /// jurnal davomati (<see cref="AbsenceReason.Points"/>) — va har qatorda manba KO'RINADI.
    /// Arxivlangan o'quvchining yozuvi ham qoladi: u maktabdan ketgani bilan o'sha kuni bo'lgan
    /// voqea bo'lmagan bo'lib qolmaydi.
    /// </summary>
    [Fact]
    public async Task Lenta_qolda_kiritilgan_va_jurnal_yozuvlarini_birga_beradi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var rows = await RowsAsync(client, $"{Feed}?className={seed.ClassName}&{seed.Period}");

        Assert.Equal(4, rows.Count);
        Assert.Equal(3, rows.Count(r => Str(r, "source") == "manual"));
        var attendance = Assert.Single(rows, r => Str(r, "source") == "attendance");
        Assert.Equal("Sababsiz", Str(attendance, "reasonName"));
        Assert.Equal(-2, Int(attendance, "points"));
        Assert.Equal("Jurnal davomati", Str(attendance, "note"));
        Assert.Equal("", Str(attendance, "createdBy"));

        // Arxivlangan o'quvchi ham lentada.
        Assert.Contains(rows, r => Str(r, "studentId") == seed.ArchivedStudentId);

        // Eng yangisi tepada (qo'lda kiritilgani bugun 23:59 da).
        Assert.Equal(seed.TodayPointId, Str(rows[0], "id"));
    }

    /// <summary>
    /// <b>Nusxa qoidasi.</b> <see cref="DisciplinePoint"/> sabab NOMINI va BALLINI yozuv paytida
    /// nusxalab oladi. Demak sabab keyin qayta nomlansa ham lentada ESKI nom turishi kerak,
    /// sabab bo'yicha filtr esa <c>ReasonId</c> ustidan ishlashi kerak — aks holda tahrirlangan
    /// sababning tarixi filtrdan yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Sabab_boyicha_filtr_id_ustidan_ishlaydi_ekranda_esa_nusxa_korinadi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var rows = await RowsAsync(
            client, $"{Feed}?className={seed.ClassName}&{seed.Period}&reasonId={seed.ReasonId}");

        // Uchala qo'lda kiritilgan yozuv ham shu sabab bilan — nomi o'zgargani bilan topildi.
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => Str(r, "reasonName") == "Eski nom");
        Assert.DoesNotContain(rows, r => Str(r, "source") == "attendance");
    }

    /// <summary>Rag'bat/jazo filtri ikkala manbaga ham qo'llanadi.</summary>
    [Fact]
    public async Task Musbat_va_manfiy_filtri()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var plus = await RowsAsync(client, $"{Feed}?className={seed.ClassName}&{seed.Period}&sign=positive");
        var minus = await RowsAsync(client, $"{Feed}?className={seed.ClassName}&{seed.Period}&sign=negative");

        Assert.Equal(3, Int(Assert.Single(plus), "points"));
        Assert.Equal(3, minus.Count);
        Assert.All(minus, r => Assert.True(Int(r, "points") < 0));
    }

    /// <summary>
    /// Xodim bo'yicha filtr tanlanganda jurnal yozuvlari CHIQMAYDI: jurnal belgisida muallif
    /// saqlanmaydi, ya'ni "shu xodim qo'ygan" degan savolga u javob bera olmaydi.
    /// </summary>
    [Fact]
    public async Task Xodim_boyicha_filtr_faqat_qolda_kiritilganlarni_beradi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        using var json = await JsonAsync(
            client, $"{Feed}?className={seed.ClassName}&{seed.Period}&author={Uri.EscapeDataString(seed.Author)}");
        var rows = json.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("manual", Str(r, "source")));
        // Xodimlar ro'yxati butun bazadan — filtr uni qisqartirmaydi.
        Assert.Contains(seed.Author,
            json.RootElement.GetProperty("authors").EnumerateArray().Select(a => a.GetString()));
    }

    /// <summary>Faqat jurnal manbasini so'rash — "bu ballar qayerdan keldi" savoliga javob.</summary>
    [Fact]
    public async Task Manba_boyicha_filtr()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var attendance = await RowsAsync(client, $"{Feed}?className={seed.ClassName}&{seed.Period}&source=attendance");
        var manual = await RowsAsync(client, $"{Feed}?className={seed.ClassName}&{seed.Period}&source=manual");

        Assert.Equal("attendance", Str(Assert.Single(attendance), "source"));
        Assert.Equal(3, manual.Count);
    }

    /// <summary>
    /// Sahifalash: <c>total</c> — FILTRLANGAN to'plamning to'liq hajmi, <c>items</c> — bitta sahifa.
    /// Jamlama (plus/minus/sum) ham butun to'plam bo'yicha, sahifa bo'yicha emas: aks holda
    /// sarlavhadagi raqam sahifani varaqlaganda o'zgarib turardi.
    /// </summary>
    [Fact]
    public async Task Sahifalash_va_jamlama()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        using var json = await JsonAsync(client, $"{Feed}?className={seed.ClassName}&{seed.Period}&page=2&pageSize=3");
        var root = json.RootElement;

        Assert.Equal(4, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(3, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("items").GetArrayLength());
        Assert.Equal(1, root.GetProperty("plusCount").GetInt32());
        Assert.Equal(3, root.GetProperty("minusCount").GetInt32());
        // −5 −5 −2 +3
        Assert.Equal(-9, root.GetProperty("pointsSum").GetInt32());
    }

    /// <summary>
    /// Davr chegarasi. Qo'lda kiritilgan yozuvda VAQT ham bor ("...T23:59:59"), jurnalda esa
    /// faqat sana. Yuqori chegara oddiy <c>&lt;=</c> bilan solishtirilsa, oxirgi kunning
    /// yozuvlari jimgina tushib qolardi — shuning uchun chegara ertangi kundan QAT'IY kichik.
    /// </summary>
    [Fact]
    public async Task Davr_oxirgi_kunni_ham_oz_ichiga_oladi()
    {
        var seed = await SeedAsync();
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var rows = await RowsAsync(client, $"{Feed}?className={seed.ClassName}&from={today}&to={today}");

        Assert.Equal(seed.TodayPointId, Str(Assert.Single(rows), "id"));
    }

    /// <summary>Davr teskari yoki noto'g'ri formatda — 400, bo'sh ro'yxat yoki 500 emas.</summary>
    [Theory]
    [InlineData("?from=2026-05-01&to=2026-04-30")]
    [InlineData("?from=01.05.2026")]
    public async Task Notogri_davr_400_qaytaradi(string query)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Feed + query)).StatusCode);
    }

    // =====================================================================
    //  3. Ballar nazorati — Excel eksporti
    // =====================================================================

    /// <summary>
    /// Eksport ekrandagi filtrlarni TAKRORLAYDI (sahifa ro'yxatni mijoz tarafida filtrlaydi).
    /// Bu yerda tekshiriladigan narsa — yuklangan faylda aynan filtrga tushgan o'quvchilar
    /// bo'lishi: <c>minPoints=100</c> da faqat qoldisi 100 dan katta o'quvchi qoladi.
    /// </summary>
    [Fact]
    public async Task Eksport_min_max_filtrini_hurmat_qiladi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{ScoresExport}?className={seed.ClassName}&minPoints=100");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("ballar_nazorati_", response.Content.Headers.ContentDisposition?.FileName ?? "",
            StringComparison.Ordinal);

        var sheet = await FirstSheetTextAsync(response);
        // Qoldi: rag'bat olgan o'quvchi 103, jazo olgani 93 (100 − 5 − 2).
        Assert.Contains(seed.PlusStudentName, sheet, StringComparison.Ordinal);
        Assert.DoesNotContain(seed.MinusStudentName, sheet, StringComparison.Ordinal);
        Assert.Contains("103", sheet, StringComparison.Ordinal);
    }

    /// <summary>Filtrsiz eksport — sinfning ikkala faol o'quvchisi ham tushadi (arxivlangani yo'q).</summary>
    [Fact]
    public async Task Eksport_filtrsiz_sinfning_faol_oquvchilarini_beradi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{ScoresExport}?className={seed.ClassName}");
        response.EnsureSuccessStatusCode();

        var sheet = await FirstSheetTextAsync(response);
        Assert.Contains(seed.PlusStudentName, sheet, StringComparison.Ordinal);
        Assert.Contains(seed.MinusStudentName, sheet, StringComparison.Ordinal);
        Assert.DoesNotContain(seed.ArchivedStudentName, sheet, StringComparison.Ordinal);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record Seed(
        string ClassName, string ReasonId, string Author, string Period, string TodayPointId,
        string ArchivedStudentId, string ArchivedStudentName, string PlusStudentName, string MinusStudentName);

    /// <summary>
    /// Bitta sinf: jazo olgan o'quvchi (qo'lda −5 va jurnaldan −2), rag'bat olgan o'quvchi (+3,
    /// sababning ESKI nomi bilan) va arxivlangan o'quvchi (−5). Sabab jo'natilgandan keyin
    /// QAYTA NOMLANADI — nusxa qoidasi shu bilan tekshiriladi.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var className = $"XA-{tag}";
        var author = $"Test Nazoratchi {tag}";
        var reason = new DisciplineReason { Name = $"Yangi nom {tag}", Points = -5 };
        var absenceReason = new AbsenceReason { Name = "Sababsiz", Short = "S", Points = -2 };

        // Ismlarda APOSTROF yo'q: eksport tekshiruvi varaq XML'idan matn qidiradi, XML
        // esa apostrofni belgilashi mumkin — nom shu sababli soddalashtirilgan.
        var minus = NewStudent($"Jazo Oquvchi {tag}", className);
        var plus = NewStudent($"Ragbat Oquvchi {tag}", className);
        var archived = NewStudent($"Arxiv Oquvchi {tag}", className);
        archived.IsArchived = true;
        archived.ArchivedAt = AppClock.Today.ToString("yyyy-MM-dd");

        var today = AppClock.Today;
        var todayPoint = new DisciplinePoint
        {
            StudentId = minus.Id, ReasonId = reason.Id, ReasonName = reason.Name, Points = -5,
            Note = "3-darsda", CreatedAt = $"{today:yyyy-MM-dd}T23:59:59.0000000", CreatedBy = author,
        };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.DisciplineReasons.Add(reason);
            db.AbsenceReasons.Add(absenceReason);
            db.Students.AddRange(minus, plus, archived);
            db.DisciplinePoints.AddRange(
                todayPoint,
                new DisciplinePoint
                {
                    StudentId = plus.Id, ReasonId = reason.Id, ReasonName = "Eski nom", Points = 3,
                    CreatedAt = $"{today.AddDays(-2):yyyy-MM-dd}T09:00:00.0000000", CreatedBy = author,
                },
                new DisciplinePoint
                {
                    StudentId = archived.Id, ReasonId = reason.Id, ReasonName = reason.Name, Points = -5,
                    CreatedAt = $"{today.AddDays(-1):yyyy-MM-dd}T09:00:00.0000000", CreatedBy = author,
                });
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = className, SubjectId = "matematika", Quarter = 1, StudentId = minus.Id,
                Date = today.AddDays(-1).ToString("yyyy-MM-dd"), Period = 2, ReasonId = absenceReason.Id,
            });
            await db.SaveChangesAsync();

            // Sabab qayta nomlandi — nusxalangan yozuvlar o'zgarmasligi kerak.
            reason.Name = $"Qayta nomlangan {tag}";
            await db.SaveChangesAsync();
        });

        var period = $"from={today.AddDays(-7):yyyy-MM-dd}&to={today:yyyy-MM-dd}";
        return new Seed(className, reason.Id, author, period, todayPoint.Id,
            archived.Id, archived.FullName, plus.FullName, minus.FullName);
    }

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = className,
        EnrollmentDate = "2025-09-01",
    };

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Bitta sahifaning qatorlari. <c>Clone()</c> SHART: <see cref="JsonDocument"/> shu yerda
    /// yopiladi, klonlanmagan <see cref="JsonElement"/> esa undan keyin o'qib bo'lmaydi.
    /// </summary>
    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url)
    {
        using var json = await JsonAsync(client, url);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static string Str(JsonElement row, string name) => row.GetProperty(name).GetString() ?? "";
    private static int Int(JsonElement row, string name) => row.GetProperty(name).GetInt32();

    /// <summary>
    /// .xlsx — bu zip. <c>ExcelExport</c> kataklarni INLINE matn sifatida yozadi, shuning uchun
    /// varaq XML'ida qiymatlar o'z ko'rinishida turadi va qo'shimcha kutubxonasiz tekshiriladi.
    /// </summary>
    private static async Task<string> FirstSheetTextAsync(HttpResponseMessage response)
    {
        await using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.Entries.First(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));
        await using var sheet = entry.Open();
        using var reader = new StreamReader(sheet, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
