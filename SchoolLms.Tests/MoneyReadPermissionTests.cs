using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// PUL o'qiladigan ikkita joyning ruxsat darvozasi.
///
/// <para>
/// <b>Nega bu testlar bor.</b> <c>AdminPermAttribute</c> sinf darajasida ishlaydi
/// va xodimning (staff) HAR QANDAY <c>GET</c> so'rovini ataylab ochiq qoldiradi —
/// bo'limlararo o'qish buzilmasligi uchun. Oqibati shu ediki:
/// </para>
/// <list type="number">
///   <item><c>GET /api/admin/messages/telegram/registrations</c> — `messages`
///     ruxsatiga ega xodim butun maktabning QOLDIG'INI o'qiy olardi;</item>
///   <item><c>GET /api/admin/academic-year/archives/{id}/download</c> —
///     `academicYear` ruxsatiga ega xodim bir yilning BARCHA hisob-fakturasi,
///     to'lovi va chiqimini ZIP qilib yuklab olardi.</item>
/// </list>
/// <para>
/// Ikkalasi ham endi moliya ruxsatini alohida so'raydi. Bu <c>X-2</c> (amal
/// darajasidagi ruxsat kalitlari) ning o'rnini bosmaydi — u kelguncha faqat
/// pulni yopadi. Shuning uchun testlar "pul ko'rinmasin" degan TALABNI
/// tekshiradi, controllerning ichki tuzilishini emas.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class MoneyReadPermissionTests(ApiFixture fixture)
{
    private const string Registrations = "/api/admin/messages/telegram/registrations";

    /// <summary>Migratsiya seed qilgan barqaror toifa id'i (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    // =====================================================================
    //  1. Telegram ro'yxati — qoldiq ustuni
    // =====================================================================

    /// <summary>
    /// Moliya ruxsati YO'Q xodim ro'yxatni ko'radi (ekran ishlashda davom etadi),
    /// lekin qoldiq <c>null</c> — NOL EMAS. Nol "qarzi yo'q" degan yolg'on ma'no
    /// berardi va xodim shunga qarab qaror qabul qilardi.
    /// </summary>
    [Fact]
    public async Task Moliya_ruxsatisiz_xodim_qoldiqni_kormaydi()
    {
        var studentId = await SeedRegistrationAsync(debt: 250_000m);

        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "messages");
        var rows = await GetRowsAsync(client);

        var row = rows.Single(r => r.GetProperty("studentId").GetString() == studentId);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("balance").ValueKind);

        // Ro'yxatning o'zi kelishi SHART — bu ekran Telegram ro'yxati uchun,
        // uni butunlay yopish xodimning ishini to'xtatardi.
        Assert.False(string.IsNullOrEmpty(row.GetProperty("parentName").GetString()));
    }

    /// <summary>Moliya ruxsati BOR xodim haqiqiy qoldiqni ko'radi (manfiy = qarz).</summary>
    [Fact]
    public async Task Moliya_ruxsatli_xodim_qoldiqni_koradi()
    {
        var studentId = await SeedRegistrationAsync(debt: 310_000m);

        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "messages", "finance");
        var rows = await GetRowsAsync(client);

        var balance = rows.Single(r => r.GetProperty("studentId").GetString() == studentId)
            .GetProperty("balance");
        Assert.NotEqual(JsonValueKind.Null, balance.ValueKind);
        Assert.True(balance.GetDecimal() < 0, "Hisob-faktura yozilgan o'quvchining qoldig'i manfiy bo'lishi kerak");
    }

    /// <summary>Admin ruxsat ro'yxati bilan cheklanmaydi.</summary>
    [Fact]
    public async Task Admin_qoldiqni_koradi()
    {
        var studentId = await SeedRegistrationAsync(debt: 120_000m);

        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var rows = await GetRowsAsync(client);

        var balance = rows.Single(r => r.GetProperty("studentId").GetString() == studentId)
            .GetProperty("balance");
        Assert.NotEqual(JsonValueKind.Null, balance.ValueKind);
    }

    // =====================================================================
    //  2. O'quv yili arxivi — ZIP
    // =====================================================================

    /// <summary>
    /// Arxivni moliyadan ajratib bo'lmaydi: <c>Moliya/</c> papkasi, o'quvchi
    /// qatoridagi qoldiq ustuni va <c>malumotlar.json</c> ning o'zi. Shuning
    /// uchun ruxsatsiz — 403; faqat papkani olib qo'yish soxta himoya bo'lardi.
    /// </summary>
    [Fact]
    public async Task Moliya_ruxsatisiz_arxiv_yuklanmaydi()
    {
        var id = await SeedArchiveAsync();

        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "academicYear");
        var res = await client.GetAsync($"/api/admin/academic-year/archives/{id}/download");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Moliya_ruxsatli_xodim_arxivni_yuklaydi()
    {
        var id = await SeedArchiveAsync();

        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "academicYear", "finance");
        var res = await client.GetAsync($"/api/admin/academic-year/archives/{id}/download");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotEmpty(await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Admin_arxivni_yuklaydi()
    {
        var id = await SeedArchiveAsync();

        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await client.GetAsync($"/api/admin/academic-year/archives/{id}/download");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    /// <summary>Ro'yxat amali (JSON'siz) moliyaga bog'liq emas — u ochiq qoladi.</summary>
    [Fact]
    public async Task Arxivlar_royxati_moliyasiz_ham_ochiq()
    {
        await SeedArchiveAsync();

        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "academicYear");
        var res = await client.GetAsync("/api/admin/academic-year/archives");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // =====================================================================
    //  Yordamchi qism
    // =====================================================================

    private static async Task<JsonElement[]> GetRowsAsync(HttpClient client)
    {
        var res = await client.GetAsync(Registrations);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement[]>())!;
    }

    /// <summary>O'quvchi + Telegram yozuvi + to'lanmagan hisob-faktura (qoldiq manfiy bo'lsin).</summary>
    private async Task<string> SeedRegistrationAsync(decimal debt)
    {
        var studentId = $"st-mrp-{Guid.NewGuid():N}"[..20];
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(new Student
            {
                Id = studentId,
                FullName = "Qoldiqli O'quvchi",
                ClassName = "5-A",
            });
            db.TelegramRegistrations.Add(new TelegramRegistration
            {
                StudentId = studentId,
                ChatId = Random.Shared.NextInt64(1, long.MaxValue),
                ParentName = "Ota-ona",
                Phone = "998900000001",
            });
            var month = new DateOnly(2026, 9, 1);
            db.Invoices.Add(new Invoice
            {
                StudentId = studentId,
                CategoryId = TuitionCategory,
                PeriodMonth = month,
                Amount = debt,
                Discount = 0m,
                DueOn = month.AddDays(9),
                Status = InvoiceStatus.Open,
                CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
        });
        return studentId;
    }

    private async Task<string> SeedArchiveAsync()
    {
        var id = Guid.NewGuid().ToString();
        await fixture.Api.WithDbAsync(async db =>
        {
            db.SchoolYearArchives.Add(new SchoolYearArchive
            {
                Id = id,
                Year = "2025/2026",
                CreatedAt = "2026-06-01T00:00:00",
                Data = "{}",
            });
            await db.SaveChangesAsync();
        });
        return id;
    }
}
