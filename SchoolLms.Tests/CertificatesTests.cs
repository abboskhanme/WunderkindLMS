using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Sertifikatlar registri (docs/modules/existing-module-gaps.md §2.3):
/// tur katalogi, hujjatlar CRUD'i, "Natijalar" tab'i va o'quvchilar ro'yxatidagi
/// ikkita filtr.
///
/// <para>
/// <b>To'rtta tekshiruv shu faylning sababi.</b>
/// </para>
/// <list type="number">
///   <item><b>Ruxsat.</b> §2.3: EduSchool bo'limga alohida ruxsat bermaydi, menyuni
///     <c>getStudents</c> ga bog'laydi — bizda <c>AdminPerm("students")</c>. Yozish
///     o'qishdan qattiqroq darvozadan o'tadi.</item>
///   <item><b>Ball qoidasi.</b> Ball faqat <c>is_scored</c> turda qabul qilinadi. Bazada
///     bu shart YO'Q (ikkita jadvalga tegishli), demak uni faqat test ushlab turadi.</item>
///   <item><b>Ishlatilgan turni o'chirish.</b> FK <c>restrict</c> — lekin xodim 23503
///     raqamini emas, "Faol emas qilib qo'ying" jumlasini ko'rishi kerak.</item>
///   <item><b>Ikkita filtr.</b> "IELTS sertifikati bor har bir bolani ko'rsat" —
///     §2.3 ning EduSchool'dan ko'chiriladigan yagona integratsiyasi.</item>
/// </list>
///
/// <para>
/// Testlar UMUMIY bazada yuradi (ApiFixture), shuning uchun har biri O'Z ma'lumotini
/// takrorlanmas qo'shimcha (tag) bilan ajratadi va har doim O'Z tur id'si bo'yicha
/// filtrlaydi — qo'shni testning qatorlari natijaga qo'shilib ketmasligi uchun.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CertificatesTests(ApiFixture fixture)
{
    private const string Certificates = "/api/admin/certificates";
    private const string Types = "/api/admin/certificate-types";
    private const string Students = "/api/admin/students";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    /// <summary>
    /// Registr — o'quvchilar bo'limi ma'lumoti. <c>AdminPerm("students")</c> darvozasi
    /// admin/superadmin/xodimdan boshqasini kiritmaydi: o'qituvchi ham, kassir ham — 403.
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_registrga_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Certificates)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Types)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Types, new { name = "X", isScored = false, isActive = true }))
                .StatusCode);
    }

    /// <summary>Token'siz — 401 (403 emas): kim so'rayotgani umuman noma'lum.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Certificates)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Types)).StatusCode);
    }

    /// <summary>Admin va superadmin — ha, mavjud o'quvchilar endpoint'lari bilan bir xil.</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_registrni_ocha_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        Assert.True((await client.GetAsync(Certificates)).IsSuccessStatusCode);
        Assert.True((await client.GetAsync(Types)).IsSuccessStatusCode);
    }

    /// <summary>
    /// <b>Yozish o'qishdan qattiqroq.</b> <c>students</c> ruxsatisiz xodim registrni
    /// O'QIY oladi (bo'limlararo bog'liqlik uchun — <c>AdminPermAttribute</c>), lekin
    /// sertifikat ham, tur ham QO'SHA olmaydi. Ruxsat berilgach — qo'sha oladi.
    /// </summary>
    [Fact]
    public async Task Xodim_ruxsatsiz_oqiydi_lekin_yoza_olmaydi()
    {
        var tag = Tag();
        using var reader = await fixture.Api.ClientAsAsync(Roles.Staff);
        using var writer = await fixture.Api.ClientAsAsync(Roles.Staff, "students");

        Assert.True((await reader.GetAsync(Certificates)).IsSuccessStatusCode);

        var body = new { name = $"Tur {tag}", isScored = false, isActive = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Types, body)).StatusCode);
        Assert.True((await writer.PostAsJsonAsync(Types, body)).IsSuccessStatusCode);
    }

    // =====================================================================
    //  2. BALL — §2.3 ning yagona mazmunli qoidasi
    // =====================================================================

    /// <summary>
    /// <b>Ball faqat standart testda.</b> Baza faqat <c>score &gt;= 0</c> ni biladi;
    /// "qaysi turda ball bo'lishi mumkin" degan shart ikkita jadvalga tegishli va
    /// CHECK bilan ifodalanmaydi. Demak xizmat qatlami to'sadi — va xato xodim
    /// o'qiy oladigan jumla bo'ladi, Postgres kodi emas.
    /// </summary>
    [Fact]
    public async Task Ballik_bolmagan_turga_ball_yozib_bolmaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var rejected = await client.PostAsJsonAsync(Certificates, new
        {
            studentId = seed.StudentA,
            typeId = seed.PlainTypeId,
            issuedOn = "2026-05-01",
            score = 90m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var message = await MessageAsync(rejected);
        Assert.Contains("standart test emas", message, StringComparison.Ordinal);
        Assert.Contains("Ball qo'yiladi", message, StringComparison.Ordinal);

        // Ballsiz — o'sha tur bemalol qabul qilinadi.
        var accepted = await client.PostAsJsonAsync(Certificates, new
        {
            studentId = seed.StudentA,
            typeId = seed.PlainTypeId,
            issuedOn = "2026-05-01",
            number = $"D-{seed.Tag}",
        });
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
    }

    /// <summary>Ballik turda ball SAQLANADI — o'nlik kasri bilan (IELTS 7.5).</summary>
    [Fact]
    public async Task Ballik_turga_ball_saqlanadi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(Certificates, new
        {
            studentId = seed.StudentA,
            typeId = seed.ScoredTypeId,
            teacherId = seed.TeacherId,
            subjectId = seed.SubjectId,
            issuedOn = "2026-04-10",
            expiresOn = "2028-04-10",
            score = 7.5m,
            number = $"IE-{seed.Tag}",
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = json.RootElement;
        Assert.Equal(7.5m, row.GetProperty("score").GetDecimal());
        Assert.True(row.GetProperty("typeIsScored").GetBoolean());
        // Nomlar javobda darrov keladi — ekran yangi qatorni to'liq ko'rsatishi kerak.
        Assert.Equal(seed.TeacherName, row.GetProperty("teacherName").GetString());
        Assert.Equal(seed.ClassName, row.GetProperty("className").GetString());
        Assert.False(row.GetProperty("isExpired").GetBoolean());
    }

    /// <summary>Manfiy ball — bazadagi CHECK'ka yetmasdan, o'qiladigan xato bilan rad etiladi.</summary>
    [Fact]
    public async Task Manfiy_ball_rad_etiladi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(Certificates, new
        {
            studentId = seed.StudentA,
            typeId = seed.ScoredTypeId,
            issuedOn = "2026-04-10",
            score = -1m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("manfiy", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// TAHRIRLASHDA ham qoida bir xil: ballik turdagi hujjatni ballsiz turga
    /// ko'chirmoqchi bo'lsak va ball qolaversa — rad. Aks holda bazada "ballsiz
    /// turdagi ballik hujjat" paydo bo'lardi va uni hech kim ushlamasdi.
    /// </summary>
    [Fact]
    public async Task Tahrirlashda_ham_ball_ballsiz_turga_otmaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var id = await CreateCertificateAsync(client, new
        {
            studentId = seed.StudentA,
            typeId = seed.ScoredTypeId,
            issuedOn = "2026-04-10",
            score = 6.5m,
        });

        var response = await client.PutAsJsonAsync($"{Certificates}/{id}", new
        {
            studentId = seed.StudentA,
            typeId = seed.PlainTypeId,
            issuedOn = "2026-04-10",
            score = 6.5m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("standart test emas", await MessageAsync(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// Muddat berilgan sanadan oldin bo'la olmaydi. Bazada CHECK bor, lekin xodimga
    /// jumla kerak — tekshiruv shuning uchun oldinroq, xizmat qatlamida.
    /// </summary>
    [Fact]
    public async Task Muddat_berilgan_sanadan_oldin_bola_olmaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(Certificates, new
        {
            studentId = seed.StudentA,
            typeId = seed.PlainTypeId,
            issuedOn = "2026-05-01",
            expiresOn = "2026-04-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("oldin bo'la olmaydi", await MessageAsync(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// Faol bo'lmagan (arxivlangan) tur yangi hujjatda tanlanmaydi — §2.3 dagi
    /// "retire with is_active" yo'lining ikkinchi yarmi.
    /// </summary>
    [Fact]
    public async Task Faol_bolmagan_tur_yangi_sertifikatda_tanlanmaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var retired = await CreateTypeAsync(client, $"Eskirgan {seed.Tag}", isScored: false, isActive: false);

        var response = await client.PostAsJsonAsync(Certificates, new
        {
            studentId = seed.StudentA,
            typeId = retired,
            issuedOn = "2026-05-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("faol emas", await MessageAsync(response), StringComparison.Ordinal);
    }

    // =====================================================================
    //  3. ISHLATILGAN TURNI O'CHIRISH
    // =====================================================================

    /// <summary>
    /// <b>O'chirilgan tur hujjatni yetim qoldirmaydi.</b> FK <c>restrict</c> to'xtatadi,
    /// lekin javob o'qiladigan bo'lishi kerak va NIMA QILISH kerakligini aytishi kerak:
    /// "Faol emas" qilib qo'yish. Hujjat o'chgach — tur ham o'chadi.
    /// </summary>
    [Fact]
    public async Task Ishlatilgan_turni_ochirib_bolmaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var certId = await CreateCertificateAsync(client, new
        {
            studentId = seed.StudentA,
            typeId = seed.PlainTypeId,
            issuedOn = "2026-03-01",
        });

        var blocked = await client.DeleteAsync($"{Types}/{seed.PlainTypeId}");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var message = await MessageAsync(blocked);
        Assert.Contains("o'chirib bo'lmaydi", message, StringComparison.Ordinal);
        Assert.Contains("Faol emas", message, StringComparison.Ordinal);

        // Tur joyida turibdi va hujjatlar soni ko'rinib turibdi.
        Assert.Equal(1, await TypeCountAsync(client, seed.PlainTypeId));

        // Hujjat o'chirilgach — tur ham o'chadi.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Certificates}/{certId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Types}/{seed.PlainTypeId}")).StatusCode);
    }

    /// <summary>
    /// Ishlatilgan turning "ballik" bayrog'i muzlatiladi: aks holda ballik turdagi
    /// hujjatlar bir kechada qoidaga zid bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Ishlatilgan_turning_ballik_belgisi_ozgarmaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        await CreateCertificateAsync(client, new
        {
            studentId = seed.StudentA,
            typeId = seed.ScoredTypeId,
            issuedOn = "2026-03-01",
            score = 7m,
        });

        var response = await client.PutAsJsonAsync($"{Types}/{seed.ScoredTypeId}",
            new { name = $"IELTS {seed.Tag}", isScored = false, isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("o'zgartirib bo'lmaydi", await MessageAsync(response), StringComparison.Ordinal);

        // Nomni o'zgartirish esa mumkin — bayroq o'z holida qolsa.
        var renamed = await client.PutAsJsonAsync($"{Types}/{seed.ScoredTypeId}",
            new { name = $"IELTS Academic {seed.Tag}", isScored = true, isActive = true });
        Assert.True(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());
    }

    // =====================================================================
    //  4. O'QUVCHILAR RO'YXATIDAGI IKKITA FILTR
    // =====================================================================

    /// <summary>
    /// "IELTS sertifikati bor har bir bolani ko'rsat" (§2.3). Ko'p tanlovda turlar
    /// orasidagi bog'lovchi — YOKI.
    /// </summary>
    [Fact]
    public async Task Sertifikat_turi_boyicha_filtr()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        // A — ballik tur, B — oddiy tur, C — hech narsa.
        await CreateCertificateAsync(client, new
        { studentId = seed.StudentA, typeId = seed.ScoredTypeId, issuedOn = "2026-02-01", score = 8m });
        await CreateCertificateAsync(client, new
        { studentId = seed.StudentB, typeId = seed.PlainTypeId, issuedOn = "2026-02-02" });

        var scored = await StudentIdsAsync(client, $"?certificateTypeIds={seed.ScoredTypeId}");
        Assert.Contains(seed.StudentA, scored);
        Assert.DoesNotContain(seed.StudentB, scored);
        Assert.DoesNotContain(seed.StudentC, scored);

        // Ikkala tur — YOKI: A ham, B ham chiqadi.
        var both = await StudentIdsAsync(
            client, $"?certificateTypeIds={seed.ScoredTypeId},{seed.PlainTypeId}");
        Assert.Contains(seed.StudentA, both);
        Assert.Contains(seed.StudentB, both);
        Assert.DoesNotContain(seed.StudentC, both);
    }

    /// <summary>Bergan o'qituvchi bo'yicha filtr + shu filtr uchun o'qituvchilar ro'yxati.</summary>
    [Fact]
    public async Task Oqituvchi_boyicha_filtr()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        await CreateCertificateAsync(client, new
        {
            studentId = seed.StudentB,
            typeId = seed.PlainTypeId,
            teacherId = seed.TeacherId,
            issuedOn = "2026-02-03",
        });
        // O'qituvchisiz hujjat — filtrga TUSHMASLIGI kerak.
        await CreateCertificateAsync(client, new
        { studentId = seed.StudentC, typeId = seed.PlainTypeId, issuedOn = "2026-02-04" });

        var ids = await StudentIdsAsync(client, $"?certificateTeacherId={seed.TeacherId}");
        Assert.Contains(seed.StudentB, ids);
        Assert.DoesNotContain(seed.StudentC, ids);

        // Filtr tanlovi — FAQAT sertifikat bergan o'qituvchilar.
        using var teachers = await JsonAsync(client, $"{Certificates}/teachers");
        Assert.Contains(teachers.RootElement.EnumerateArray(),
            t => t.GetProperty("id").GetString() == seed.TeacherId);
    }

    /// <summary>
    /// <b>Qo'shimcha, o'zgartiruvchi emas.</b> Filtrlar berilmasa o'quvchilar ro'yxati
    /// bugungiday ishlaydi: seed qilingan uchala bola ham joyida, arxiv ro'yxati ham.
    /// </summary>
    [Fact]
    public async Task Filtrsiz_royxat_ozgarmaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var all = await StudentIdsAsync(client, "");
        Assert.Contains(seed.StudentA, all);
        Assert.Contains(seed.StudentB, all);
        Assert.Contains(seed.StudentC, all);

        // Bo'sh parametr ham filtr EMAS — "hamma" degani.
        var empty = await StudentIdsAsync(client, "?certificateTypeIds=&certificateTeacherId=");
        Assert.Contains(seed.StudentA, empty);
        Assert.Contains(seed.StudentC, empty);

        Assert.True((await client.GetAsync($"{Students}/archived")).IsSuccessStatusCode);
    }

    // =====================================================================
    //  5. "NATIJALAR" TAB'I
    // =====================================================================

    /// <summary>
    /// Ballar jadvali: har o'quvchiga ENG YAXSHI va ENG OXIRGI ball. Yig'ma raqamlar
    /// eng yaxshi ballar ustidan olinadi — IELTS'ni ikki marta topshirgan bola
    /// o'rtachani ikki marta pasaytirmaydi. Hammasi SERVERDA hisoblanadi.
    /// </summary>
    [Fact]
    public async Task Natijalar_eng_yaxshi_va_oxirgi_ballni_beradi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        // A: ikki marta topshirgan — eskisi kuchliroq.
        await CreateCertificateAsync(client, new
        { studentId = seed.StudentA, typeId = seed.ScoredTypeId, issuedOn = "2025-06-01", score = 7.5m });
        await CreateCertificateAsync(client, new
        { studentId = seed.StudentA, typeId = seed.ScoredTypeId, issuedOn = "2026-06-01", score = 6.5m });
        // B: bir marta.
        await CreateCertificateAsync(client, new
        { studentId = seed.StudentB, typeId = seed.ScoredTypeId, issuedOn = "2026-01-15", score = 5.5m });

        using var json = await JsonAsync(client, $"{Certificates}/results?typeId={seed.ScoredTypeId}");
        var root = json.RootElement;

        Assert.Equal(2, root.GetProperty("studentCount").GetInt32());
        Assert.Equal(3, root.GetProperty("certificateCount").GetInt32());
        Assert.Equal(7.5m, root.GetProperty("maxScore").GetDecimal());
        Assert.Equal(5.5m, root.GetProperty("minScore").GetDecimal());
        // (7.5 + 5.5) / 2 — eng yaxshi ballar ustidan, uchala hujjat ustidan emas.
        Assert.Equal(6.5m, root.GetProperty("averageScore").GetDecimal());

        var rows = root.GetProperty("rows").EnumerateArray().Select(e => e.Clone()).ToList();
        // Eng yuqori ball tepada.
        Assert.Equal(seed.StudentA, rows[0].GetProperty("studentId").GetString());
        Assert.Equal(7.5m, rows[0].GetProperty("bestScore").GetDecimal());
        Assert.Equal(6.5m, rows[0].GetProperty("latestScore").GetDecimal());
        Assert.Equal("2026-06-01", rows[0].GetProperty("latestIssuedOn").GetString());
        Assert.Equal(2, rows[0].GetProperty("count").GetInt32());
    }

    /// <summary>Yo'q tur — 404, bo'sh jadval emas: ekran "tanlov eskirgan" deb aytsin.</summary>
    [Fact]
    public async Task Natijalar_yoq_tur_uchun_404()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{Certificates}/results?typeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =====================================================================
    //  6. RO'YXAT FILTRLARI (registr ekrani)
    // =====================================================================

    /// <summary>
    /// Registr ekranining filtrlari: o'quvchi, tur va "muddati tugayapti".
    /// Muddati ALLAQACHON o'tgan hujjat ham "tugayapti" filtriga tushadi — u ham
    /// diqqat talab qiladi.
    /// </summary>
    [Fact]
    public async Task Registr_filtrlari_ishlaydi()
    {
        var seed = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var expired = AppClock.Today.AddDays(-10).ToString("yyyy-MM-dd");
        var longLived = AppClock.Today.AddYears(5).ToString("yyyy-MM-dd");

        await CreateCertificateAsync(client, new
        {
            studentId = seed.StudentA,
            typeId = seed.PlainTypeId,
            issuedOn = "2024-01-01",
            expiresOn = expired,
        });
        await CreateCertificateAsync(client, new
        {
            studentId = seed.StudentB,
            typeId = seed.PlainTypeId,
            issuedOn = "2024-01-01",
            expiresOn = longLived,
        });

        var mine = $"{Certificates}?typeId={seed.PlainTypeId}";
        Assert.Equal(2, (await RowsAsync(client, mine)).Count);

        var byStudent = await RowsAsync(client, $"{mine}&studentId={seed.StudentA}");
        var only = Assert.Single(byStudent);
        Assert.True(only.GetProperty("isExpired").GetBoolean());

        var expiring = await RowsAsync(client, $"{mine}&expiringInDays=30");
        Assert.Equal(seed.StudentA, Assert.Single(expiring).GetProperty("studentId").GetString());
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record Seed(
        string Tag, string ClassName,
        string StudentA, string StudentB, string StudentC,
        string TeacherId, string TeacherName, string SubjectId,
        Guid ScoredTypeId, Guid PlainTypeId);

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Bitta sinf: uchta o'quvchi, bitta o'qituvchi, bitta fan va ikkita tur —
    /// ballik ("IELTS") va oddiy ("Diplom").
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        var tag = Tag();
        var className = $"SR-{tag}";
        var teacher = new Teacher { FullName = $"Sertifikat Oqituvchi {tag}", Phone = "+998901112233" };
        var subject = new Subject { Name = $"Ingliz tili {tag}" };
        var a = NewStudent($"Sertifikat A {tag}", className);
        var b = NewStudent($"Sertifikat B {tag}", className);
        var c = NewStudent($"Sertifikat C {tag}", className);

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Teachers.Add(teacher);
            db.Subjects.Add(subject);
            db.Students.AddRange(a, b, c);
            await db.SaveChangesAsync();
        });

        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var scored = await CreateTypeAsync(client, $"IELTS {tag}", isScored: true, isActive: true);
        var plain = await CreateTypeAsync(client, $"Diplom {tag}", isScored: false, isActive: true);

        return new Seed(tag, className, a.Id, b.Id, c.Id,
            teacher.Id, teacher.FullName, subject.Id, scored, plain);
    }

    private static async Task<Guid> CreateTypeAsync(
        HttpClient client, string name, bool isScored, bool isActive)
    {
        var response = await client.PostAsJsonAsync("/api/admin/certificate-types",
            new { name, isScored, isActive });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCertificateAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/admin/certificates", payload);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<int> TypeCountAsync(HttpClient client, Guid typeId)
    {
        using var json = await JsonAsync(client, "/api/admin/certificate-types");
        var row = json.RootElement.EnumerateArray()
            .First(t => t.GetProperty("id").GetGuid() == typeId);
        return row.GetProperty("certificateCount").GetInt32();
    }

    /// <summary>O'quvchilar ro'yxatidagi id'lar — filtr tekshiruvlari uchun.</summary>
    private static async Task<List<string>> StudentIdsAsync(HttpClient client, string query)
    {
        using var json = await JsonAsync(client, $"/api/admin/students{query}");
        return [.. json.RootElement.EnumerateArray()
            .Select(s => s.GetProperty("id").GetString() ?? "")];
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url)
    {
        using var json = await JsonAsync(client, url);
        return [.. json.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>400 javobidagi <c>message</c> — ekranga chiqadigan aynan o'sha jumla.</summary>
    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2010-01-01",
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
}
