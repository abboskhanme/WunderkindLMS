using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  CHIQIM EKRANI ↔ SERVER SHARTNOMASI — F1.01, F1.02
//  (docs/modules/finance-parity.md §1.1, §2.1.3)
// ===========================================================================
//
//  NEGA BU TESTLAR ALOHIDA TURADI
//  ------------------------------
//  `ExpensesTests` xizmatning o'z qoidalarini tekshiradi va HAR DOIM yashil
//  edi — chunki u so'rovni O'ZI to'g'ri tuzadi. Ekran esa boshqa tana
//  yuborardi: "Yangi chiqim" formasida `method` UMUMAN yo'q edi
//  (`api/services/expenses.ts` dagi `ExpenseInput`), tasdiqlash tugmasi esa
//  tanasiz `POST` qilardi. Natija: UI dan kiritilgan HAR BIR chiqim 400 bilan
//  rad etilardi, tasdiqlash esa umuman ishlamasdi — server tomoni butunlay
//  sog'lom bo'lgani holda.
//
//  Shuning uchun bu yerdagi so'rovlar AYNAN ekran yuboradigan tana bilan
//  yoziladi (maydon nomlari camelCase, ortiqcha maydonsiz). Xizmat darajasidagi
//  test bu nosozlikni hech qachon ushlamasdi.
//
//  QAMROV
//  ------
//   1) forma yuboradigan tana bilan chiqim yoziladi (F1.01);
//   2) tasdiqlash oynasi yuboradigan tana bilan chiqim jurnalga tushadi (F1.02);
//   3) chegara SERVERDAN o'qiladi — klientdagi 5 000 000 konstantasi o'rniga;
//   4) ikki marta bosilgan "Tasdiqlash" jurnalga IKKINCHI partiya qo'ymaydi;
//   5) javobdagi maydon nomlari ekran o'qiydigan nomlar bilan bir xil.
// ===========================================================================

/// <summary>
/// Chiqim ekranining server bilan shartnomasi — HTTP darajasida. Batafsil:
/// fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ExpenseUiContractTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/expenses";

    /// <summary>
    /// Bu testlar 2023-yil sanalarida ishlaydi: davr boshqa testlar bilan
    /// kesishmasin (<c>ExpensesTests</c> 2024-ni band qilgan), aks holda
    /// hisobot tekshiruvlari bir-birining qatorlarini ko'rardi.
    /// </summary>
    private static readonly DateOnly Day = new(2023, 4, 18);

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =================================================================
    //  1. F1.01 — "Yangi chiqim" formasining tanasi
    // =================================================================

    /// <summary>
    /// <b>F1.01.</b> <c>ExpenseFormModal</c> yuboradigan tana: <c>onDate</c>,
    /// <c>category</c>, <c>amount</c>, <c>method</c> va (bo'lsa) <c>note</c>.
    /// Ilgari <c>method</c> yo'q edi va server har safar
    /// <c>400 invalid_method</c> qaytarardi.
    /// </summary>
    [Theory]
    [InlineData(PaymentMethod.Cash, Accounts.Cash)]
    [InlineData(PaymentMethod.Card, Accounts.Bank)]
    [InlineData(PaymentMethod.Transfer, Accounts.Bank)]
    [InlineData(PaymentMethod.Online, Accounts.Bank)]
    public async Task Forma_yuboradigan_tana_bilan_chiqim_yoziladi(
        string method, string expectedAccount)
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(Url, new
        {
            onDate = "2023-04-18",
            category = "utilities",
            amount = 120_000,
            method,
            note = "Aprel — elektr",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
        Assert.Equal(ExpenseStatus.Posted, dto.Status);
        Assert.Equal(expectedAccount, dto.SettlementAccount);
    }

    /// <summary>
    /// Izoh ixtiyoriy: forma bo'sh izohni <c>undefined</c> qilib yuboradi, ya'ni
    /// maydon tanada UMUMAN bo'lmaydi. Server buni ham qabul qilishi kerak.
    /// </summary>
    [Fact]
    public async Task Izohsiz_tana_ham_qabul_qilinadi()
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(Url, new
        {
            onDate = "2023-04-19",
            category = "supplies",
            amount = 45_000,
            method = PaymentMethod.Cash,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
        Assert.Null(dto.Note);
        Assert.Equal(ExpenseStatus.Posted, dto.Status);
    }

    // =================================================================
    //  2. F1.02 — tasdiqlash oynasining tanasi
    // =================================================================

    /// <summary>
    /// <b>F1.02.</b> Tasdiqlash oynasi <c>{ method }</c> yuboradi va chiqim
    /// AYNAN shu usul bilan jurnalga tushadi. Ilgari tugma tanasiz
    /// <c>POST</c> qilardi va so'rov tanani o'qiy olmagani uchun yiqilardi —
    /// ya'ni tasdiq navbatidagi pul hech qachon jurnalga tushmasdi.
    /// </summary>
    [Fact]
    public async Task Tasdiqlash_oynasining_tanasi_bilan_chiqim_jurnalga_tushadi()
    {
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var (director, directorClient) = await ActorAsync(Roles.SuperAdmin);

        var created = await CreateAsync(adminClient, Day, "rent", 6_500_000m, PaymentMethod.Transfer);
        Assert.Equal(ExpenseStatus.Pending, created.Status);

        var response = await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/approve", new { method = PaymentMethod.Cash });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
        Assert.Equal(ExpenseStatus.Posted, approved.Status);
        Assert.Equal(Accounts.Cash, approved.SettlementAccount);
        Assert.Equal(director.Id, approved.ApprovedBy);

        await using var db = NewDb();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == created.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
    }

    // =================================================================
    //  3. F1.02 — chegara SERVERDAN keladi
    // =================================================================

    /// <summary>
    /// Chegara <c>billing_settings</c> dan o'qiladi va endpoint uni O'SHA
    /// zahoti qaytaradi. Klientda takrorlangan konstanta (eski
    /// <c>EXPENSE_APPROVAL_THRESHOLD = 5_000_000</c>) sozlama o'zgargan kuni
    /// yolg'on gapira boshlardi: 5 mln dan past, lekin yangi chegaradan yuqori
    /// chiqim ekranda "yozib olingan" bo'lib ko'rinardi, aslida esa tasdiq
    /// kutayotgan bo'lardi.
    /// </summary>
    [Fact]
    public async Task Chegara_endpointi_sozlamadagi_qiymatni_qaytaradi()
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var policy = await client.GetFromJsonAsync<ExpenseApprovalPolicyDto>($"{Url}/approval-policy");
        Assert.NotNull(policy);
        Assert.Equal(5_000_000m, policy.Threshold);

        await using var db = NewDb();
        var settings = await db.BillingSettings.FirstAsync();
        var original = settings.ExpenseApprovalThreshold;
        try
        {
            settings.ExpenseApprovalThreshold = 250_000m;
            await db.SaveChangesAsync();

            var changed = await client.GetFromJsonAsync<ExpenseApprovalPolicyDto>($"{Url}/approval-policy");
            Assert.Equal(250_000m, changed!.Threshold);

            // Va ekran holatni SHU chegaraga qarab emas, serverning
            // `status` maydoniga qarab ko'rsatadi — ikkalasi bir xil javob
            // berishi shart.
            var dto = await CreateAsync(client, Day, "other", 300_000m, PaymentMethod.Cash);
            Assert.Equal(ExpenseStatus.Pending, dto.Status);
        }
        finally
        {
            settings.ExpenseApprovalThreshold = original;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Chegara ham moliya darvozasi ortida: kassirga yopiq (SPEC §4.3).</summary>
    [Fact]
    public async Task Chegara_endpointi_kassirga_yopiq_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{Url}/approval-policy")).StatusCode);
    }

    // =================================================================
    //  4. F1.02 — ikki marta tasdiqlash
    // =================================================================

    /// <summary>
    /// <b>Ikki marta bosilgan "Tasdiqlash".</b> Sekin javobda direktor tugmani
    /// ikkinchi marta bosadi va bu YANGI so'rov bo'lib ketadi. Qulfsiz ikkala
    /// so'rov ham <c>approved_by is null</c> tekshiruvidan o'tardi va jurnalga
    /// IKKITA partiya tushardi — chiqim P&amp;L da ikki baravar ko'rinardi.
    ///
    /// <para>
    /// Tasdiq: bir vaqtda yuborilgan oltita so'rovdan AYNAN bittasi 200 oladi,
    /// qolganlari 409 <c>already_approved</c>, jurnalda esa AYNAN ikkita satr
    /// (debet + kredit) qoladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ikki_marta_tasdiqlash_jurnalga_ikkinchi_partiya_qoymaydi()
    {
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var (_, directorClient) = await ActorAsync(Roles.SuperAdmin);

        var created = await CreateAsync(
            adminClient, new DateOnly(2023, 4, 20), "repair", 7_200_000m, PaymentMethod.Transfer);
        Assert.Equal(ExpenseStatus.Pending, created.Status);

        var attempts = Enumerable.Range(0, 6).Select(_ => directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/approve", new { method = PaymentMethod.Transfer }));
        var responses = await Task.WhenAll(attempts);

        var ok = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, ok);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var error = await refused.Content.ReadFromJsonAsync<BillingErrorDto>();
            Assert.Equal("already_approved", error!.Code);
        }

        await using var db = NewDb();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == created.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(7_200_000m, entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount));
    }

    // =================================================================
    //  5. Javob maydonlari — ekran AYNAN shu nomlarni o'qiydi
    // =================================================================

    /// <summary>
    /// Javobdagi maydon nomlari (camelCase) ekran kutgani bilan bir xil. Bu
    /// ro'yxat <c>api/services/expenses.ts</c> dagi <c>ExpenseRecord</c> ning
    /// ko'zgusi: nom o'zgarsa yoki tushib qolsa, ekran holatni JIMGINA
    /// noto'g'ri ko'rsatardi (masalan <c>status</c> yo'qolsa hamma chiqim
    /// "yozib olingan" bo'lib qolardi).
    /// </summary>
    [Fact]
    public async Task Javob_maydonlari_ekran_kutgan_nomlar_bilan_keladi()
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var created = await CreateAsync(client, new DateOnly(2023, 4, 21), "other", 33_000m, PaymentMethod.Cash);

        var json = await client.GetStringAsync($"{Url}/{created.Id}");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        foreach (var field in new[]
        {
            "id", "onDate", "category", "amount", "note", "status", "settlementAccount",
            "createdBy", "createdByName", "createdAt", "approvedBy", "approvedByName",
            "reversedBy", "reversedByName", "reversalReason", "teacherId", "teacherName",
        })
        {
            Assert.True(root.TryGetProperty(field, out _), $"Javobda '{field}' maydoni yo'q.");
        }

        Assert.Equal(ExpenseStatus.Posted, root.GetProperty("status").GetString());
    }

    // =================================================================
    //  Yordamchilar
    // =================================================================

    private async Task<(AppUser User, HttpClient Client)> ActorAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }

    private static async Task<ExpenseDto> CreateAsync(
        HttpClient client, DateOnly onDate, string category, decimal amount, string method)
    {
        var response = await client.PostAsJsonAsync(Url, new { onDate, category, amount, method });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
    }
}
