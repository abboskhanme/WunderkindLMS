using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Arxivlash (§2.2 va §5.5): sabablar katalogi, sabab MAJBURIYligi, ommaviy arxivlash
/// va qarzdorlik to'sig'i — superadmin chetlab o'tishi bilan.
///
/// <para>
/// <b>Qarzdorlik to'sig'i nega eng ko'p tekshiriladi.</b> Bu pul qoidasi (§9 Q4):
/// arxivlash — qarzning yo'qolishining eng oson yo'li. Shuning uchun bitta va ommaviy
/// yo'lning IKKALASI ham, admin <c>force</c> ni yuborgan holat ham, superadmin
/// <c>force</c> siz holat ham alohida tekshiriladi — ulardan biri teshik bo'lsa, qoida
/// umuman yo'q.
/// </para>
/// <para>
/// Qarz HAQIQIY yo'l bilan tug'iladi: ochiq hisob-faktura (<c>invoices</c>) — balans
/// <c>StudentBalanceQuery</c> dan hisoblanadi, o'quvchi qatorida saqlanmaydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentArchiveTests(ApiFixture fixture)
{
    private const string Reasons = "/api/admin/archive-reasons";
    private const string Students = "/api/admin/students";
    private const string ArchiveMany = "/api/admin/students/archive-many";

    /// <summary>billing_seed.sql dagi barqaror "O'qish" toifasi.</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    /// <summary>parity_wave2_seed.sql dagi "Maktabni bitirdi" va "Boshqa".</summary>
    private static readonly Guid Graduated = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Other = new("00000000-0000-0000-0000-0000000000a9");

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Reasons)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(ArchiveMany, new { studentIds = new[] { "x" }, reason = "x" })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "settings", "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Reasons)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Reasons, new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(ArchiveMany, new { studentIds = new[] { "x" }, reason = "x" })).StatusCode);
    }

    /// <summary>
    /// Xodim: katalogni O'QIYDI (arxivlash oynasi uni "O'quv bo'limi"da ko'rsatadi), lekin
    /// "settings" ruxsatisiz yoza olmaydi; "students" ruxsatisiz arxivlay olmaydi.
    /// </summary>
    [Fact]
    public async Task Xodim_katalogni_oqiydi_lekin_ruxsatsiz_yozmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Reasons)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Reasons, new { name = "Xodim " + Tag() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(ArchiveMany, new { studentIds = new[] { "x" }, reason = "x" })).StatusCode);
    }

    // =====================================================================
    //  2. KATALOG
    // =====================================================================

    /// <summary>Migratsiya seed qilgan yettita sabab; "Boshqa" — 99-o'rinda, ya'ni oxirida.</summary>
    [Fact]
    public async Task Boshlangich_katalog_va_Boshqa_oxirida()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var rows = await RowsAsync(client, $"{Reasons}?includeInactive=true");
        var seeded = rows.Where(r => r.GetProperty("id").GetString()!.StartsWith("00000000-0000-0000-0000-0000000000a")).ToList();

        Assert.Equal(7, seeded.Count);
        var other = Assert.Single(seeded, r => r.GetProperty("id").GetGuid() == Other);
        Assert.Equal("Boshqa", other.GetProperty("name").GetString());
        Assert.Equal(99, other.GetProperty("position").GetInt32());
        Assert.Equal(Other, seeded[^1].GetProperty("id").GetGuid());
    }

    /// <summary>
    /// CRUD: nom unikal; faolsizlantirilgan sabab arxivlash ro'yxatida yo'q, sozlamada bor;
    /// ISHLATILGAN sabab o'chirilmaydi (tushunarli matn bilan), ishlatilmagani o'chiriladi.
    /// </summary>
    [Fact]
    public async Task Katalog_crud_va_ishlatilgan_sabab_ochirilmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var created = await client.PostAsJsonAsync(Reasons, new { name = $"Ko'chib ketdi {tag}", position = 7 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = await IdAsync(created);

        var dup = await client.PostAsJsonAsync(Reasons, new { name = $"Ko'chib ketdi {tag}" });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);

        // Ishlatamiz: shu sabab bilan o'quvchi arxivlanadi.
        var student = await SeedStudentAsync(tag);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"{Students}/{student.Id}/archive",
                new { reason = "Toshkentga", archiveReasonId = id })).StatusCode);

        var row = (await RowsAsync(client, Reasons)).Single(r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal(1, row.GetProperty("usedBy").GetInt32());

        var delete = await client.DeleteAsync($"{Reasons}/{id}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Contains("faolsizlantiring", await MessageAsync(delete));

        // Faolsizlantirish: arxivlash ro'yxatidan chiqadi, sozlamada qoladi.
        var deactivate = await client.PutAsJsonAsync($"{Reasons}/{id}",
            new { name = $"Ko'chib ketdi {tag}", isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.DoesNotContain(await RowsAsync(client, Reasons), r => r.GetProperty("id").GetGuid() == id);
        Assert.Contains(await RowsAsync(client, $"{Reasons}?includeInactive=true"),
            r => r.GetProperty("id").GetGuid() == id);

        // Ishlatilmagan sabab o'chiriladi.
        var spare = await IdAsync(await client.PostAsJsonAsync(Reasons, new { name = $"Keraksiz {tag}" }));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Reasons}/{spare}")).StatusCode);
    }

    // =====================================================================
    //  3. BITTA O'QUVCHI — sabab majburiy
    // =====================================================================

    /// <summary>
    /// Erkin matn MAJBURIY (katalog uni almashtirmaydi); faolsiz katalog qatori rad etiladi;
    /// muvaffaqiyatda ikkala ustun ham yoziladi va login bloklanadi. Arxivdan qaytarish
    /// katalog havolasini ham bo'shatadi.
    /// </summary>
    [Fact]
    public async Task Bitta_arxivlash_sabab_matni_majburiy_va_ikkala_ustun_yoziladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var student = await SeedStudentAsync(tag, withLogin: true);

        var noText = await client.PostAsJsonAsync($"{Students}/{student.Id}/archive",
            new { reason = "  ", archiveReasonId = Graduated });
        Assert.Equal(HttpStatusCode.BadRequest, noText.StatusCode);
        Assert.Equal(StudentArchiveService.ReasonRequiredMessage, await MessageAsync(noText));

        var inactive = await IdAsync(await client.PostAsJsonAsync(Reasons, new { name = $"Eski {tag}", isActive = false }));
        var badReason = await client.PostAsJsonAsync($"{Students}/{student.Id}/archive",
            new { reason = "Bitirdi", archiveReasonId = inactive });
        Assert.Equal(HttpStatusCode.BadRequest, badReason.StatusCode);
        await AssertArchivedAsync(student.Id, false);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"{Students}/{student.Id}/archive",
                new { reason = "9-sinfni bitirdi", archiveReasonId = Graduated })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.Students.AsNoTracking().SingleAsync(x => x.Id == student.Id);
            Assert.True(s.IsArchived);
            Assert.Equal("9-sinfni bitirdi", s.ArchiveReason);
            Assert.Equal(Graduated, s.ArchiveReasonId);
            Assert.Equal("", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == s.UserId)).PasswordHash);
        });

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"{Students}/{student.Id}/restore", new { newPassword = (string?)null })).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.Null((await db.Students.AsNoTracking().SingleAsync(x => x.Id == student.Id)).ArchiveReasonId));
    }

    // =====================================================================
    //  4. QARZDORLIK TO'SIG'I (§5.5 archive_only_non_debtor_students)
    // =====================================================================

    /// <summary>
    /// Bayroq yoqiq: admin qarzdorni arxivlay olmaydi — <c>force</c> yuborsa ham. Javobda kim
    /// va qancha qarz ekani bor. Superadmin <c>force</c> siz ham rad etiladi (lekin
    /// <c>canOverride = true</c>), <c>force</c> bilan esa arxivlaydi.
    /// </summary>
    [Fact]
    public async Task Qarzdor_rad_etiladi_superadmin_ataylab_chetlab_otadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var super = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var debtor = await SeedStudentAsync(Tag(), debt: 450_000m);
        var url = $"{Students}/{debtor.Id}/archive";

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.ArchiveOnlyNonDebtorStudents = true, async () =>
        {
            var refused = await admin.PostAsJsonAsync(url, new { reason = "Ketdi" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            using (var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
            {
                var blocked = Assert.Single(body.RootElement.GetProperty("blocked").EnumerateArray());
                Assert.Equal(debtor.Id, blocked.GetProperty("studentId").GetString());
                Assert.Equal(450_000m, blocked.GetProperty("debt").GetDecimal());
                Assert.False(body.RootElement.GetProperty("canOverride").GetBoolean());
                Assert.Equal(StudentArchiveService.DebtorBlockedMessage, body.RootElement.GetProperty("message").GetString());
            }

            // Admin `force` yuborsa ham — rad.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync(url, new { reason = "Ketdi", force = true })).StatusCode);

            // Superadmin `force` siz — rad, lekin chetlab o'tish mumkinligini biladi.
            var superRefused = await super.PostAsJsonAsync(url, new { reason = "Ketdi" });
            Assert.Equal(HttpStatusCode.BadRequest, superRefused.StatusCode);
            using (var body = JsonDocument.Parse(await superRefused.Content.ReadAsStringAsync()))
                Assert.True(body.RootElement.GetProperty("canOverride").GetBoolean());
            await AssertArchivedAsync(debtor.Id, false);

            // Superadmin, ataylab.
            Assert.Equal(HttpStatusCode.NoContent,
                (await super.PostAsJsonAsync(url, new { reason = "Ketdi", force = true })).StatusCode);
            await AssertArchivedAsync(debtor.Id, true);
        });
    }

    /// <summary>Bayroq o'chiq — bugungi xatti-harakat: admin qarzdorni ham arxivlaydi. Bayroq ishlayotganining isboti.</summary>
    [Fact]
    public async Task Bayroq_ochiq_bolsa_qarzdor_ham_arxivlanadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var debtor = await SeedStudentAsync(Tag(), debt: 300_000m);

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.ArchiveOnlyNonDebtorStudents = false, async () =>
            Assert.Equal(HttpStatusCode.NoContent,
                (await admin.PostAsJsonAsync($"{Students}/{debtor.Id}/archive", new { reason = "Ketdi" })).StatusCode));

        await AssertArchivedAsync(debtor.Id, true);
    }

    // =====================================================================
    //  5. OMMAVIY ARXIVLASH
    // =====================================================================

    /// <summary>Uch o'quvchi bitta sabab bilan: hammasi arxivda, ikkala ustun yozilgan, login bloklangan.</summary>
    [Fact]
    public async Task Ommaviy_arxivlash_hammasini_bir_sabab_bilan_arxivlaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var students = new[]
        {
            await SeedStudentAsync(tag, withLogin: true),
            await SeedStudentAsync(tag, withLogin: true),
            await SeedStudentAsync(tag, withLogin: true),
        };
        var ids = students.Select(s => s.Id).ToArray();

        var response = await admin.PostAsJsonAsync(ArchiveMany,
            new { studentIds = ids, reason = "2026 bitiruvchilari", archiveReasonId = Graduated });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(3, body.RootElement.GetProperty("archived").GetInt32());
            Assert.Empty(body.RootElement.GetProperty("blocked").EnumerateArray());
        }

        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync();
            Assert.All(rows, s =>
            {
                Assert.True(s.IsArchived);
                Assert.Equal("2026 bitiruvchilari", s.ArchiveReason);
                Assert.Equal(Graduated, s.ArchiveReasonId);
                Assert.False(s.ArchivedWithClass);
            });
            var userIds = rows.Select(s => s.UserId).ToList();
            Assert.All(await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync(),
                u => Assert.Equal("", u.PasswordHash));
        });
    }

    /// <summary>
    /// Uchtadan biri qarzdor: amal BUTUNLAY rad etiladi (hech kim arxivlanmaydi), ro'yxatda
    /// faqat qarzdor. Superadmin <c>force</c> bilan — uchalasi arxivlanadi.
    /// </summary>
    [Fact]
    public async Task Ommaviy_arxivlashda_bitta_qarzdor_hammasini_toxtatadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var super = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var tag = Tag();
        var clean1 = await SeedStudentAsync(tag);
        var clean2 = await SeedStudentAsync(tag);
        var debtor = await SeedStudentAsync(tag, debt: 800_000m);
        var ids = new[] { clean1.Id, debtor.Id, clean2.Id };

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.ArchiveOnlyNonDebtorStudents = true, async () =>
        {
            var refused = await admin.PostAsJsonAsync(ArchiveMany, new { studentIds = ids, reason = "Bitirdi" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            using (var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
            {
                Assert.Equal(0, body.RootElement.GetProperty("archived").GetInt32());
                var blocked = Assert.Single(body.RootElement.GetProperty("blocked").EnumerateArray());
                Assert.Equal(debtor.Id, blocked.GetProperty("studentId").GetString());
            }
            foreach (var id in ids) await AssertArchivedAsync(id, false);

            var forced = await super.PostAsJsonAsync(ArchiveMany, new { studentIds = ids, reason = "Bitirdi", force = true });
            Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
            foreach (var id in ids) await AssertArchivedAsync(id, true);
        });
    }

    [Fact]
    public async Task Ommaviy_arxivlash_bosh_royxat_va_bosh_matnni_rad_etadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = await SeedStudentAsync(Tag());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(ArchiveMany, new { studentIds = Array.Empty<string>(), reason = "X" })).StatusCode);

        var noText = await admin.PostAsJsonAsync(ArchiveMany, new { studentIds = new[] { s.Id }, reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noText.StatusCode);
        Assert.Equal(StudentArchiveService.ReasonRequiredMessage, await MessageAsync(noText));
        await AssertArchivedAsync(s.Id, false);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// O'quvchi: ixtiyoriy login (parol bilan — bloklanishini ko'rish uchun) va ixtiyoriy
    /// qarz (ochiq hisob-faktura).
    /// </summary>
    private async Task<Student> SeedStudentAsync(string tag, bool withLogin = false, decimal debt = 0m)
    {
        var student = GeneralSettingsFlagsTests.NewStudent(
            $"Arxiv {Guid.NewGuid().ToString("N")[..4]} {tag}", $"AR-{tag}", "+998900000001");

        if (withLogin)
        {
            var (user, _) = await fixture.Api.SeedUserAsync(Roles.Student);
            student.UserId = user.Id;
        }

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

        return student;
    }

    private async Task AssertArchivedAsync(string studentId, bool expected)
    {
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(expected, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId)).IsArchived));
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
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
