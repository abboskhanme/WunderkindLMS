using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  KASSALAR (cash boxes) — "smena" o'rnini bosadi. Mijoz javobi (2026-09):
//  "bizni tizimda smena degan tushuncha umuman bo'lmasin butunlay olib
//  tashla, shunchaki kassa degan narsa bo'lsin xolos, bizda bir nechta kassa
//  bo'lishi mumkin, ular har bir alohida pul kirim chiqim qilishi va o'zaro
//  o'tkazma qilishi mumkin."
// ===========================================================================
//
//  ENG MUHIM TEST: `Otkazma_parallel_ikki_yonalishda_ham_pul_yoqolmaydi_ham_kopaymaydi`
//  — SPEC talabi: "prove with a concurrency test that two simultaneous
//  transfers cannot conjure or destroy money".
//
//  "TRIAL BALANCE = 0" — bu quyi tizim `ledger_entries` ga YOZMAYDI
//  (`CashBoxService.cs` fayl boshidagi izoh: kassa harakatlari — o'zining
//  ichki, muvozanatlashgan sub-jurnali). Shuning uchun bu yerdagi "trial
//  balance" o'z ma'nosida: BARCHA kassalar bo'yicha kirim − chiqim − (netto
//  ko'chirish, u har doim 0) == barcha kassalar balansi yig'indisi — bu
//  invariantni `Yakunlar_barcha_amallardan_keyin_muvozanatda_qoladi` testi
//  tekshiradi.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class CashBoxTests : IDisposable
{
    private const string Url = "/api/admin/cash-boxes";

    public CashBoxTests(ApiFixture fixture) => this.fixture = fixture;

    private readonly ApiFixture fixture;

    /// <summary>
    /// Konkurrentlik testi o'z, kengaytirilgan pool'li (40+) alohida bazasini
    /// ochadi (<c>ReceiptNumberingTests</c> dagi bilan bir xil sabab) — test
    /// tugagach ularni DARHOL bo'shatamiz, aks holda Npgsql ~5 daqiqa BO'SH
    /// ushlab turadi va shu davrda ishga tushgan boshqa ko'p-ulanishli test
    /// (<c>CashShiftServiceTests</c>) konteynerdagi <c>max_connections</c>
    /// chegarasiga tegib, 53300 bilan yiqiladi.
    /// </summary>
    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    private sealed record ErrorBody(string Code, string Message);

    // =====================================================================
    //  1. Kataloq — ochish, sukut (default) belgisi
    // =====================================================================

    /// <summary>
    /// SPEC: "exactly one box must be the default". Umumiy test bazasida
    /// migratsiya SEED qilgan sukut kassa allaqachon bor — shuning uchun
    /// "birinchi kassa" emas, balki "sukut ko'rsatilmasa yangi kassa sukut
    /// bo'lmaydi" tekshiriladi (chunki bittasi allaqachon bor).
    /// </summary>
    [Fact]
    public async Task Sukut_korsatilmasa_yangi_kassa_sukut_bolmaydi_chunki_bittasi_allaqachon_bor()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Url, new { name = "Yagona kassa " + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var box = (await response.Content.ReadFromJsonAsync<CashBoxDto>())!;

        Assert.False(box.IsDefault);
        Assert.True(box.IsActive);
        Assert.Equal(0m, box.Balance);

        // Baribir aynan BITTA kassa sukut bo'lib qoladi (migratsiya seed'i).
        var list = (await (await admin.GetAsync(Url)).Content.ReadFromJsonAsync<List<CashBoxDto>>())!;
        Assert.Single(list.Where(b => b.IsDefault));
    }

    /// <summary>
    /// Ikkinchi kassa SUKUT bo'lmaydi (ochilishda ko'rsatilmasa) — bittasi
    /// allaqachon sukut, bazadagi qisman unikal indeks buni kafolatlaydi.
    /// </summary>
    [Fact]
    public async Task Ikkinchi_kassa_korsatilmasa_sukut_bolmaydi_va_almashtirsa_eskisi_yechiladi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);

        var first = await CreateBoxAsync(admin, isDefault: true);
        var second = await CreateBoxAsync(admin);
        Assert.False(second.IsDefault);

        // Ikkinchisini sukut qilamiz — birinchisi AVTOMATIK yechiladi.
        var update = await admin.PutAsJsonAsync($"{Url}/{second.Id}", new { isDefault = true });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<CashBoxDto>())!;
        Assert.True(updated.IsDefault);

        var list = (await (await admin.GetAsync(Url)).Content.ReadFromJsonAsync<List<CashBoxDto>>())!;
        Assert.Single(list.Where(b => b.IsDefault));
        Assert.Equal(second.Id, list.Single(b => b.IsDefault).Id);
        Assert.False(list.Single(b => b.Id == first.Id).IsDefault);
    }

    /// <summary>Sukut kassani deaktivatsiya qilib bo'lmaydi — avval boshqasini sukut qilish kerak.</summary>
    [Fact]
    public async Task Sukut_kassani_deaktivatsiya_qilib_bolmaydi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin, isDefault: true);

        var response = await admin.PutAsJsonAsync($"{Url}/{box.Id}", new { isActive = false });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("cannot_deactivate_default", (await Error(response)).Code);
    }

    /// <summary>Sukut belgisini to'g'ridan-to'g'ri (<c>isDefault=false</c>) yechib bo'lmaydi.</summary>
    [Fact]
    public async Task Sukut_belgisini_togridan_togri_yechib_bolmaydi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin, isDefault: true);

        var response = await admin.PutAsJsonAsync($"{Url}/{box.Id}", new { isDefault = false });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("cannot_unset_default", (await Error(response)).Code);
    }

    // =====================================================================
    //  2. Kirim / Chiqim
    // =====================================================================

    [Fact]
    public async Task Kirim_va_chiqim_balansni_togri_hisoblaydi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);

        await PayInAsync(admin, box.Id, 500_000m, PaymentMethod.Cash);
        await PayInAsync(admin, box.Id, 200_000m, PaymentMethod.Card);
        await PayOutAsync(admin, box.Id, 100_000m, PaymentMethod.Cash);

        var reloaded = await GetBoxAsync(admin, box.Id);
        Assert.Equal(600_000m, reloaded.Balance);
        Assert.Equal(400_000m, reloaded.ByMethod[PaymentMethod.Cash]);
        Assert.Equal(200_000m, reloaded.ByMethod[PaymentMethod.Card]);
    }

    /// <summary>Faol bo'lmagan kassaga kirim/chiqim/ko'chirish/ayirboshlash — 409.</summary>
    [Fact]
    public async Task Faol_bolmagan_kassaga_amal_409()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);
        var other = await CreateBoxAsync(admin);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"{Url}/{box.Id}", new { isActive = false })).StatusCode);

        var payIn = await admin.PostAsJsonAsync($"{Url}/{box.Id}/in",
            new { amount = 10_000m, method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.Conflict, payIn.StatusCode);
        Assert.Equal("cash_box_inactive", (await Error(payIn)).Code);

        var transfer = await admin.PostAsJsonAsync($"{Url}/{other.Id}/transfer",
            new { toBoxId = box.Id, amount = 10_000m, method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.Conflict, transfer.StatusCode);
        Assert.Equal("cash_box_inactive", (await Error(transfer)).Code);
    }

    // =====================================================================
    //  3. Ko'chirish (Transfer) — atomar juft
    // =====================================================================

    [Fact]
    public async Task Kochirish_ikkala_kassani_ham_ozgartiradi_yoki_hech_birini()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var from = await CreateBoxAsync(admin);
        var to = await CreateBoxAsync(admin);

        await PayInAsync(admin, from.Id, 1_000_000m, PaymentMethod.Cash);

        var response = await admin.PostAsJsonAsync($"{Url}/{from.Id}/transfer",
            new { toBoxId = to.Id, amount = 300_000m, method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fromReloaded = await GetBoxAsync(admin, from.Id);
        var toReloaded = await GetBoxAsync(admin, to.Id);
        Assert.Equal(700_000m, fromReloaded.Balance);
        Assert.Equal(300_000m, toReloaded.Balance);
    }

    [Fact]
    public async Task Kassa_oziga_otkazma_qila_olmaydi_400()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Url}/{box.Id}/transfer",
            new { toBoxId = box.Id, amount = 10_000m, method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("transfer_same_box", (await Error(response)).Code);
    }

    /// <summary>
    /// <b>SPEC talabi:</b> "prove with a concurrency test that two
    /// simultaneous transfers cannot conjure or destroy money".
    ///
    /// <para>
    /// Ikkita kassa orasida IKKI YO'NALISHDA (A→B va B→A) bir vaqtning o'zida
    /// ko'p sonli o'tkazma yuboriladi. Qulf noto'g'ri (masalan har doim
    /// "manba, keyin manzil" tartibida) bo'lsa, A→B va B→A parallel kelganda
    /// KLASSIK DEADLOCK yuzaga keladi (PostgreSQL uni 40P01 bilan aniqlab,
    /// ikkala tranzaksiyadan birini qaytaradi) — <see cref="Task.WhenAll(Task[])"/>
    /// bu holda istisno bilan yiqiladi. To'g'ri (GUID bo'yicha o'suvchi,
    /// yo'nalishdan qat'i nazar bir xil) tartib bunday holatni FIZIK jihatdan
    /// yo'q qiladi — <see cref="CashBoxService.TransferAsync"/> izohi.
    /// </para>
    /// <para>
    /// Yakunda ikkala kassaning balansi YIG'INDISI boshlang'ich summaga TENG
    /// qolishi SHART — pul na yo'qoldi, na ko'paydi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Otkazma_parallel_ikki_yonalishda_ham_pul_yoqolmaydi_ham_kopaymaydi()
    {
        const int perDirection = 15;
        const decimal unit = 1_000m;

        var database = await fixture.Postgres.CreateDatabaseAsync("cashbox-transfer");
        var connectionString = WithPoolSize(database.OwnerConnectionString, (perDirection * 2) + 10);

        Guid boxAId, boxBId;
        string actorId;
        await using (var db = PostgresFixture.NewContext(connectionString))
        {
            var actor = new AppUser { FullName = "Concurrency admin", Role = Roles.Admin, Email = "cc." + Guid.NewGuid().ToString("N")[..8] };
            db.Users.Add(actor);

            var boxA = new CashBox { Name = "A " + Guid.NewGuid().ToString("N")[..6], IsDefault = false, IsActive = true, CreatedAt = AppClock.NowInstant };
            var boxB = new CashBox { Name = "B " + Guid.NewGuid().ToString("N")[..6], IsDefault = false, IsActive = true, CreatedAt = AppClock.NowInstant };
            db.CashBoxes.Add(boxA);
            db.CashBoxes.Add(boxB);
            await db.SaveChangesAsync();

            actorId = actor.Id;
            boxAId = boxA.Id;
            boxBId = boxB.Id;

            // Boshlang'ich: A da 100 000, B da 0.
            await new CashBoxService(db).PayInAsync(
                boxAId, new CashBoxPayInRequest(perDirection * unit * 2, PaymentMethod.Cash), actorId);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var aToB = Enumerable.Range(0, perDirection).Select(async _ =>
        {
            await start.Task;
            await using var db = PostgresFixture.NewContext(connectionString);
            await new CashBoxService(db).TransferAsync(
                boxAId, new CashBoxTransferRequest(boxBId, unit, PaymentMethod.Cash), actorId);
        });
        var bToA = Enumerable.Range(0, perDirection).Select(async _ =>
        {
            await start.Task;
            await using var db = PostgresFixture.NewContext(connectionString);
            await new CashBoxService(db).TransferAsync(
                boxBId, new CashBoxTransferRequest(boxAId, unit, PaymentMethod.Cash), actorId);
        });

        var tasks = aToB.Concat(bToA).ToList();
        start.SetResult();

        // Deadlock yoki boshqa istisno bo'lsa — shu yerda yiqiladi (test QIZIL).
        await Task.WhenAll(tasks);

        await using var check = PostgresFixture.NewContext(connectionString);
        var service = new CashBoxService(check);
        var boxes = await service.ListAsync();
        var boxAFinal = boxes.Single(b => b.Id == boxAId);
        var boxBFinal = boxes.Single(b => b.Id == boxBId);

        // Teng sonli teskari o'tkazma — ikkala kassa ham boshlang'ich holatga qaytadi.
        Assert.Equal(perDirection * unit * 2, boxAFinal.Balance);
        Assert.Equal(0m, boxBFinal.Balance);
        // ENG MUHIMI: yig'indi o'zgarmagan — pul yo'qolmadi, ko'paymadi.
        Assert.Equal(perDirection * unit * 2, boxAFinal.Balance + boxBFinal.Balance);

        var transactionCount = await check.CashBoxTransactions.AsNoTracking()
            .CountAsync(t => t.CashBoxId == boxAId || t.CashBoxId == boxBId);
        // 1 ta boshlang'ich kirim + (perDirection * 2) ta o'tkazma qatori.
        Assert.Equal(1 + perDirection * 2, transactionCount);
    }

    // =====================================================================
    //  4. Ayirboshlash (Exchange)
    // =====================================================================

    [Fact]
    public async Task Ayirboshlash_umumiy_balansni_ozgartirmaydi_faqat_usul_kesimini()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);
        await PayInAsync(admin, box.Id, 500_000m, PaymentMethod.Cash);

        var response = await admin.PostAsJsonAsync($"{Url}/{box.Id}/exchange", new
        {
            amount = 200_000m, fromMethod = PaymentMethod.Cash, toMethod = PaymentMethod.Card,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await GetBoxAsync(admin, box.Id);
        Assert.Equal(500_000m, reloaded.Balance);          // jami o'zgarmadi
        Assert.Equal(300_000m, reloaded.ByMethod[PaymentMethod.Cash]);
        Assert.Equal(200_000m, reloaded.ByMethod[PaymentMethod.Card]);
    }

    [Fact]
    public async Task Ayirboshlash_bir_xil_usulga_rad_etiladi_400()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Url}/{box.Id}/exchange", new
        {
            amount = 10_000m, fromMethod = PaymentMethod.Cash, toMethod = PaymentMethod.Cash,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("exchange_same_method", (await Error(response)).Code);
    }

    // =====================================================================
    //  5. Bekor qilish (Cancel) — storno, hech qachon UPDATE/DELETE
    // =====================================================================

    [Fact]
    public async Task Bekor_qilish_qarshi_qator_qoshadi_original_tegilmaydi_balans_qaytadi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);

        var payIn = await PayInAsync(admin, box.Id, 400_000m, PaymentMethod.Cash);
        Assert.Equal(400_000m, (await GetBoxAsync(admin, box.Id)).Balance);

        var cancel = await admin.PostAsJsonAsync(
            $"{Url}/transactions/{payIn.Id}/cancel", new { reason = "Xato kiritildi" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var mirror = (await cancel.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
        Assert.Equal("reversal", mirror.Status);

        Assert.Equal(0m, (await GetBoxAsync(admin, box.Id)).Balance);

        // Original QATOR bazada TEGILMAGAN — faqat 42501 emas, mantiqiy tekshiruv:
        // 2 ta qator bor (original + storno), ikkalasi ham APP_RW orqali yozildi.
        await using var db = NewDb();
        var rows = await db.CashBoxTransactions.AsNoTracking()
            .Where(t => t.CashBoxId == box.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        var original = rows.Single(r => r.Id == payIn.Id);
        Assert.Equal(400_000m, original.Amount);
        Assert.Equal(CashBoxTransactionStatus.Posted, original.Status);
        Assert.Null(original.ReversalOf);
    }

    [Fact]
    public async Task Ikki_marta_bekor_qilish_409()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);
        var payIn = await PayInAsync(admin, box.Id, 100_000m, PaymentMethod.Cash);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(
            $"{Url}/transactions/{payIn.Id}/cancel", new { reason = "Birinchi" })).StatusCode);

        var second = await admin.PostAsJsonAsync(
            $"{Url}/transactions/{payIn.Id}/cancel", new { reason = "Ikkinchi" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_cancelled", (await Error(second)).Code);
    }

    // =====================================================================
    //  6. RBAC (SPEC: "RBAC via FinanceRoleAttribute")
    // =====================================================================

    /// <summary>Kassir kunlik amallarni (in/out/transfer/exchange) bajara oladi, lekin kataloqni boshqara olmaydi.</summary>
    [Fact]
    public async Task Kassir_amal_bajaradi_lekin_kataloqni_boshqara_olmaydi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var box = await CreateBoxAsync(admin);
        var other = await CreateBoxAsync(admin);

        var (_, cashier) = await ActorAsync(Roles.Cashier);

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsJsonAsync($"{Url}/{box.Id}/in",
            new { amount = 10_000m, method = PaymentMethod.Cash })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsJsonAsync($"{Url}/{box.Id}/out",
            new { amount = 1_000m, method = PaymentMethod.Cash })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsJsonAsync($"{Url}/{box.Id}/transfer",
            new { toBoxId = other.Id, amount = 1_000m, method = PaymentMethod.Cash })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsJsonAsync($"{Url}/{box.Id}/exchange",
            new { amount = 1_000m, fromMethod = PaymentMethod.Cash, toMethod = PaymentMethod.Card })).StatusCode);

        // Kataloq — YOPIQ.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostAsJsonAsync(Url, new { name = "Kassir kassasi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PutAsJsonAsync($"{Url}/{box.Id}", new { name = "Yangi nom" })).StatusCode);

        // Bekor qilish — YOPIQ (AdminAndDirector).
        var latest = (await (await admin.GetAsync($"{Url}/transactions?boxId={box.Id}"))
            .Content.ReadFromJsonAsync<CashBoxTransactionsPageDto>())!;
        var anyTransactionId = latest.Rows[0].Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(
            $"{Url}/transactions/{anyTransactionId}/cancel", new { reason = "x" })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Oqituvchi_va_xodim_kira_olmaydi_403(string role)
    {
        var (_, client) = await ActorAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
    }

    // =====================================================================
    //  7. Yakunlar — kirim/chiqim/kochirish/ayirboshlash/bekor qilishdan keyin muvozanat
    // =====================================================================

    /// <summary>
    /// "Trial balance = 0" invarianti (SPEC — moliya testlari uchun umumiy
    /// talab), kassalar quyi tizimi uchun: har bir amal usul kesimida
    /// izchil ta'sir qoladi va bekor qilingan amal o'z ta'sirini TO'LIQ
    /// yo'qqa chiqaradi. Aralash amallar ketma-ketligidan so'ng BOSHQA,
    /// mustaqil so'rov (<c>GET transactions</c>) bilan hisoblangan yakunlar
    /// <c>ListAsync</c> orqali hisoblangan balanslar bilan MOS kelishi kerak.
    /// </summary>
    [Fact]
    public async Task Yakunlar_barcha_amallardan_keyin_muvozanatda_qoladi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var boxA = await CreateBoxAsync(admin);
        var boxB = await CreateBoxAsync(admin);

        await PayInAsync(admin, boxA.Id, 1_000_000m, PaymentMethod.Cash);
        await PayInAsync(admin, boxA.Id, 200_000m, PaymentMethod.Card);
        var toCancel = await PayOutAsync(admin, boxA.Id, 50_000m, PaymentMethod.Cash);
        await admin.PostAsJsonAsync($"{Url}/transactions/{toCancel.Id}/cancel", new { reason = "Xato" });

        var transferred = 300_000m;
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{Url}/{boxA.Id}/transfer",
            new { toBoxId = boxB.Id, amount = transferred, method = PaymentMethod.Cash })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{Url}/{boxA.Id}/exchange",
            new { amount = 100_000m, fromMethod = PaymentMethod.Card, toMethod = PaymentMethod.Transfer })).StatusCode);

        var finalA = await GetBoxAsync(admin, boxA.Id);
        var finalB = await GetBoxAsync(admin, boxB.Id);

        // 1 000 000 + 200 000 − 300 000 (chiqdi) = 900 000. Chiqim (50 000)
        // BEKOR QILINGAN, ya'ni uning ta'siri YO'Q — shuning uchun ayirilmaydi.
        Assert.Equal(900_000m, finalA.Balance);
        Assert.Equal(300_000m, finalB.Balance);

        // Umumiy tizimga kirgan-chiqqan pul: 1 200 000 kirdi, 300 000 boshqa
        // kassaga ko'chdi (yo'qolmadi) — YIG'INDI o'zgarmas.
        Assert.Equal(1_200_000m, finalA.Balance + finalB.Balance);

        var page = (await (await admin.GetAsync($"{Url}/transactions?boxId={boxA.Id}"))
            .Content.ReadFromJsonAsync<CashBoxTransactionsPageDto>())!;
        // inTotal — faqat pay_in (2 ta, storno qilinmagan): 1 200 000.
        Assert.Equal(1_200_000m, page.InTotal);
        // outTotal — faqat pay_out; yakka chiqim BEKOR QILINGAN, ya'ni 0.
        Assert.Equal(0m, page.OutTotal);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<(AppUser User, HttpClient Client)> ActorAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }

    private static async Task<CashBoxDto> CreateBoxAsync(HttpClient client, bool isDefault = false)
    {
        var response = await client.PostAsJsonAsync(Url, new
        {
            name = "Test kassa " + Guid.NewGuid().ToString("N")[..8],
            isDefault,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxDto>())!;
    }

    private static async Task<CashBoxDto> GetBoxAsync(HttpClient client, Guid id)
    {
        var list = (await (await client.GetAsync(Url)).Content.ReadFromJsonAsync<List<CashBoxDto>>())!;
        return list.Single(b => b.Id == id);
    }

    private static async Task<CashBoxTransactionRowDto> PayInAsync(
        HttpClient client, Guid boxId, decimal amount, string method)
    {
        var response = await client.PostAsJsonAsync($"{Url}/{boxId}/in", new { amount, method });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
    }

    private static async Task<CashBoxTransactionRowDto> PayOutAsync(
        HttpClient client, Guid boxId, decimal amount, string method)
    {
        var response = await client.PostAsJsonAsync($"{Url}/{boxId}/out", new { amount, method });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxTransactionRowDto>())!;
    }

    private static async Task<ErrorBody> Error(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>())!;

    private static string WithPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;
}
