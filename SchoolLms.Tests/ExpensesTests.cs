using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Chiqim kiritish, tasdiqlash va storno — SPEC §3.7, §4.3, §4.5.
///
/// <para>
/// ENG MUHIM TEST: <see cref="Chiqim_kiritilsa_jurnalga_ikki_satr_tushadi_va_pnl_uni_koradi"/>.
/// Modulning sababi shu: chiqim JURNALGA tushmasa, P&amp;L faqat daromadni
/// ko'radi va "sof foyda" butun aylanmaga teng bo'lib chiqadi.
/// </para>
/// <para>
/// Testlar ikki qavatda: HTTP orqali (marshrut, rol darvozasi, status kodlari,
/// §4.4 shaxs tekshiruvi) va baza orqali (tranzaksiya natijasi, jurnal
/// satrlari, check constraint). Ikkalasi ham kerak — HTTP'siz rol darvozasi
/// sinalmaydi, bazasiz esa "aynan nima yozildi" degan savol ochiq qoladi.
/// </para>
/// <para>
/// <b>Sanalar 2024-yildan olingan</b> (boshqa testlar 2025–2026 bilan ishlaydi):
/// umumiy bazada davr bo'yicha hisobotni tekshirish uchun davr BAND
/// BO'LMASLIGI kerak, aks holda test bugun yashil, ertaga qo'shni vazifa
/// yozganda qizil bo'lardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ExpensesTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/expenses";
    private const string Pnl = "/api/admin/finance/pnl";

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =================================================================
    //  1. ENG MUHIM TEST — chiqim jurnalga tushadi
    // =================================================================

    /// <summary>
    /// Chiqim kiritilgach: <c>expenses</c> da bitta qator, jurnalda AYNAN
    /// ikkita satr (<c>debit expense:utilities</c> / <c>credit cash</c>) va
    /// P&amp;L o'sha davrda shu chiqimni ko'radi.
    /// </summary>
    [Fact]
    public async Task Chiqim_kiritilsa_jurnalga_ikki_satr_tushadi_va_pnl_uni_koradi()
    {
        var (admin, client) = await ActorAsync(Roles.Admin);
        var day = new DateOnly(2024, 3, 5);
        const decimal amount = 1_250_000m;

        var response = await client.PostAsJsonAsync(Url, new
        {
            onDate = day,
            category = "utilities",
            amount,
            method = PaymentMethod.Cash,
            note = "Mart — elektr va suv",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ExpenseDto>();
        Assert.NotNull(dto);

        // --- Javob: darhol jurnalga tushgan (chegaradan past) ---
        Assert.Equal(ExpenseStatus.Posted, dto.Status);
        Assert.Equal(Accounts.ExpenseUtilities, dto.Account);
        Assert.Equal(Accounts.Cash, dto.SettlementAccount);
        Assert.Equal(day, dto.PostedOn);
        Assert.Equal(admin.Id, dto.CreatedBy);          // §4.4 — JWT'dan
        Assert.Null(dto.ApprovedBy);                    // chegaradan past — tasdiq talab qilinmaydi
        Assert.Equal(amount, dto.Amount);

        await using var db = NewDb();

        // --- Bazada bitta chiqim qatori ---
        var expense = Assert.Single(await db.Expenses.AsNoTracking()
            .Where(e => e.Id == dto.Id).ToListAsync());
        Assert.Equal("utilities", expense.Category);
        Assert.Equal(admin.Id, expense.CreatedBy);
        Assert.Equal(TimeSpan.Zero, expense.CreatedAt.Offset);   // timestamptz — lahza

        // --- Jurnal: AYNAN ikki satr, balanslashgan ---
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == dto.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(LedgerRefType.Expense, e.RefType));
        Assert.All(entries, e => Assert.Equal(day, e.EntryDate));
        Assert.All(entries, e => Assert.Equal(admin.Id, e.CreatedBy));

        var debit = Assert.Single(entries, e => e.Direction == LedgerDirection.Debit);
        Assert.Equal(Accounts.ExpenseUtilities, debit.Account);
        Assert.Equal(amount, debit.Amount);

        var credit = Assert.Single(entries, e => e.Direction == LedgerDirection.Credit);
        Assert.Equal(Accounts.Cash, credit.Account);
        Assert.Equal(amount, credit.Amount);

        // --- P&L shu chiqimni KO'RADI (moduldan kutilgan asosiy natija) ---
        var pnl = await GetPnlAsync(client, day, day);
        Assert.Equal(amount, pnl.ExpenseTotal);
        Assert.Equal(amount, pnl.Expense.Single(l => l.Account == Accounts.ExpenseUtilities).Amount);
        Assert.Equal(-amount, pnl.Net);
    }

    /// <summary>
    /// Naqd bo'lmagan usul bankdan chiqadi (SPEC §8.1 Q13 mantig'ining ko'zgusi).
    /// </summary>
    [Theory]
    [InlineData(PaymentMethod.Cash, Accounts.Cash)]
    [InlineData(PaymentMethod.Card, Accounts.Bank)]
    [InlineData(PaymentMethod.Transfer, Accounts.Bank)]
    [InlineData(PaymentMethod.Online, Accounts.Bank)]
    public async Task Tolov_usuli_pul_qaysi_hisobdan_chiqishini_belgilaydi(
        string method, string expectedAccount)
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var dto = await CreateAsync(client, new DateOnly(2024, 5, 7), "supplies", 90_000m, method);

        Assert.Equal(expectedAccount, dto.SettlementAccount);

        await using var db = NewDb();
        var credit = Assert.Single(await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == dto.Id && e.Direction == LedgerDirection.Credit).ToListAsync());
        Assert.Equal(expectedAccount, credit.Account);
    }

    // =================================================================
    //  2. Pul aylanmasi halqasi — balans saqlanadi
    // =================================================================

    /// <summary>
    /// "Kirim = markaz = chiqim" o'zgarmasi. Bitta kunga daromad va ikkita
    /// chiqim yoziladi, so'ng jurnaldan AYNAN pul aylanmasi halqasi
    /// hisoblaydigan yig'indilar olinadi:
    /// <list type="bullet">
    ///   <item>chiqim tugunlari (<c>expense:*</c>) yig'indisi = pul hisoblaridan
    ///   (<c>cash</c> + <c>bank</c>) chiqqan summa;</item>
    ///   <item>davrdagi hamma debet = hamma kredit (halqa yopiladi);</item>
    ///   <item>butun jurnal (davrsiz) ham balansda.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Eslatma:</b> <c>GET /api/admin/finance/money-flow</c> endpoint'i hali
    /// mavjud emas — u P1-26 ning ishi (docs/TASKS.md). Shu sababli bu test
    /// o'sha endpoint javob berishi kerak bo'lgan O'ZGARMASNI jurnal darajasida
    /// tekshiradi: endpoint yozilganda uning raqamlari aynan shu yig'indilardan
    /// chiqadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Pul_aylanmasi_halqasi_balansda_qoladi()
    {
        var (admin, client) = await ActorAsync(Roles.Admin);
        var day = new DateOnly(2024, 4, 16);
        const decimal revenue = 3_000_000m;
        const decimal salary = 800_000m;
        const decimal rent = 450_000m;

        // Kirim tomoni: hisob-faktura yozildi (qarz ↑, daromad ↑). Hisob-faktura
        // xizmati (P1-09) bu yerda kerak emas — halqa JURNALDAN hisoblanadi.
        await using (var seed = NewDb())
        {
            var refId = Guid.NewGuid();
            await new LedgerService(seed).PostAsync(
            [
                new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, revenue,
                    LedgerRefType.Invoice, refId, day),
                new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, revenue,
                    LedgerRefType.Invoice, refId, day),
            ], admin.Id);
        }

        // Chiqim tomoni: biri kassadan, biri bankdan.
        await CreateAsync(client, day, "salary", salary, PaymentMethod.Cash);
        await CreateAsync(client, day, "rent", rent, PaymentMethod.Transfer);

        await using var db = NewDb();
        var rows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate == day)
            .Select(e => new { e.Account, e.Direction, e.Amount })
            .ToListAsync();

        decimal Net(Func<string, bool> account) =>
            rows.Where(r => account(r.Account) && r.Direction == LedgerDirection.Debit).Sum(r => r.Amount)
            - rows.Where(r => account(r.Account) && r.Direction == LedgerDirection.Credit).Sum(r => r.Amount);

        // Chiqim tugunlari — pul hisoblaridan chiqqan summa (halqaning o'ng yarmi).
        Assert.Equal(salary + rent, Net(a => a.StartsWith("expense:", StringComparison.Ordinal)));
        Assert.Equal(-(salary + rent), Net(a => a is Accounts.Cash or Accounts.Bank));

        // Kirim tomoni (halqaning chap yarmi).
        Assert.Equal(-revenue, Net(a => a.StartsWith("revenue:", StringComparison.Ordinal)));

        // Halqa yopiladi: davrdagi hamma debet = hamma kredit.
        Assert.Equal(
            rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount),
            rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount));

        // Va butun jurnal (davrdan qat'i nazar) ham balansda.
        var trial = await new LedgerService(db).TrialBalanceAsync();
        Assert.Equal(trial.Sum(t => t.Debit), trial.Sum(t => t.Credit));
    }

    // =================================================================
    //  3. Ikki qavatli nazorat — SPEC §4.5
    // =================================================================

    /// <summary>
    /// Chegaradan (5 000 000 so'm) YUQORI chiqim jurnalga TUSHMAYDI: u
    /// <c>pending</c> bo'lib turadi va ikkinchi shaxs tasdiqlagandan keyingina
    /// pul hisobotga kiradi.
    /// </summary>
    [Fact]
    public async Task Chegaradan_yuqori_chiqim_tasdiqsiz_jurnalga_tushmaydi()
    {
        var (admin, adminClient) = await ActorAsync(Roles.Admin);
        var (director, directorClient) = await ActorAsync(Roles.SuperAdmin);
        var day = new DateOnly(2024, 6, 11);
        const decimal amount = 6_000_000m;

        var created = await CreateAsync(adminClient, day, "salary", amount, PaymentMethod.Transfer);

        // --- Hali jurnalda yo'q ---
        Assert.Equal(ExpenseStatus.Pending, created.Status);
        Assert.Null(created.SettlementAccount);
        Assert.Null(created.PostedOn);

        await using (var db = NewDb())
            Assert.Empty(await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefId == created.Id).ToListAsync());

        // --- Direktorning "tasdiq kutmoqda" ro'yxatida ko'rinadi ---
        var pending = await directorClient
            .GetFromJsonAsync<List<ExpenseDto>>($"{Url}?status={ExpenseStatus.Pending}");
        Assert.Contains(pending!, e => e.Id == created.Id);

        // --- Tasdiqlangach jurnalga tushadi ---
        var approved = await ApproveAsync(directorClient, created.Id, PaymentMethod.Transfer);
        Assert.Equal(ExpenseStatus.Posted, approved.Status);
        Assert.Equal(Accounts.Bank, approved.SettlementAccount);
        Assert.Equal(director.Id, approved.ApprovedBy);
        Assert.Equal(admin.Id, approved.CreatedBy);
        Assert.Equal(day, approved.PostedOn);

        await using (var db = NewDb())
        {
            var entries = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefId == created.Id).ToListAsync();
            Assert.Equal(2, entries.Count);
            // Jurnal satrining muallifi — pul chiqishiga ruxsat bergan odam.
            Assert.All(entries, e => Assert.Equal(director.Id, e.CreatedBy));
            Assert.Equal(amount, Assert.Single(entries, e => e.Account == Accounts.ExpenseSalary).Amount);
        }
    }

    /// <summary>O'zi kiritgan chiqimni o'zi tasdiqlay olmaydi (SPEC §4.5) — 403.</summary>
    [Fact]
    public async Task Ozi_kiritgan_chiqimni_ozi_tasdiqlay_olmaydi_403()
    {
        var (_, directorClient) = await ActorAsync(Roles.SuperAdmin);

        var created = await CreateAsync(
            directorClient, new DateOnly(2024, 6, 12), "rent", 7_500_000m, PaymentMethod.Transfer);
        Assert.Equal(ExpenseStatus.Pending, created.Status);

        var response = await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/approve", new { method = PaymentMethod.Transfer });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("self_approval", (await ErrorAsync(response)).Code);

        await using var db = NewDb();
        Assert.Empty(await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == created.Id).ToListAsync());
    }

    /// <summary>
    /// Ilova chetlab o'tilsa ham baza to'xtatadi: <c>ck_expenses_approver_differs</c>
    /// (SPEC §4.5 — "check constraint, not a convention").
    /// </summary>
    [Fact]
    public async Task Baza_oz_ozini_tasdiqlashni_rad_etadi()
    {
        var (admin, client) = await ActorAsync(Roles.Admin);
        var created = await CreateAsync(
            client, new DateOnly(2024, 6, 13), "other", 8_000_000m, PaymentMethod.Transfer);

        await using var db = NewDb();
        var expense = await db.Expenses.FirstAsync(e => e.Id == created.Id);
        expense.ApprovedBy = admin.Id;   // = created_by

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ck_expenses_approver_differs", Flatten(ex), StringComparison.Ordinal);
    }

    /// <summary>
    /// Chegara KODDA emas, <c>billing_settings</c> da: uni o'zgartirish darhol
    /// ta'sir qiladi. Test sozlamani o'zgartiradi va MAJBURAN qaytaradi —
    /// u butun test bazasi uchun yagona qator.
    /// </summary>
    [Fact]
    public async Task Chegara_sozlamadan_oqiladi()
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        await using var db = NewDb();
        var settings = await db.BillingSettings.FirstAsync();
        var original = settings.ExpenseApprovalThreshold;

        // Migratsiya sukut qiymati mijoz javobidan (docs/TASKS.md §8 Q16).
        Assert.Equal(5_000_000m, original);

        try
        {
            settings.ExpenseApprovalThreshold = 100m;
            await db.SaveChangesAsync();

            var dto = await CreateAsync(
                client, new DateOnly(2024, 7, 2), "supplies", 200m, PaymentMethod.Cash);

            Assert.Equal(ExpenseStatus.Pending, dto.Status);
        }
        finally
        {
            settings.ExpenseApprovalThreshold = original;
            await db.SaveChangesAsync();
        }
    }

    // =================================================================
    //  4. Storno — xato chiqimni tuzatishning yagona yo'li
    // =================================================================

    /// <summary>
    /// Storno: jurnalga ko'zgu satrlar qo'yiladi, original satrlar TEGILMAYDI,
    /// <c>expenses</c> ga yangi qator YOZILMAYDI va chiqim davrning P&amp;L
    /// sidan chiqib ketadi.
    /// </summary>
    [Fact]
    public async Task Storno_kozgu_satr_qoshadi_original_tegilmaydi()
    {
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var (director, directorClient) = await ActorAsync(Roles.SuperAdmin);
        var day = new DateOnly(2024, 8, 20);
        const decimal amount = 300_000m;

        var created = await CreateAsync(adminClient, day, "repair", amount, PaymentMethod.Cash);
        var expensesBefore = await CountExpensesAsync();

        var response = await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/reverse", new { reason = "Ikki marta kiritilgan" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ExpenseDto>();
        Assert.NotNull(dto);
        Assert.Equal(ExpenseStatus.Reversed, dto.Status);
        Assert.Equal(director.Id, dto.ReversedBy);
        Assert.Equal("Ikki marta kiritilgan", dto.ReversalReason);

        // `expenses` ga YANGI QATOR QO'SHILMAYDI — storno jurnalda yashaydi.
        Assert.Equal(expensesBefore, await CountExpensesAsync());

        await using var db = NewDb();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == created.Id).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(4, entries.Count);

        var original = entries.Where(e => e.RefType == LedgerRefType.Expense).ToList();
        var mirror = entries.Where(e => e.RefType == LedgerRefType.Reversal).ToList();
        Assert.Equal(2, original.Count);
        Assert.Equal(2, mirror.Count);

        // Original TEGILMAGAN: sanasi ham, yo'nalishi ham o'sha.
        Assert.All(original, e => Assert.Equal(day, e.EntryDate));
        Assert.All(original, e => Assert.Null(e.ReversalOf));

        // Ko'zgu: qarshi yo'nalish, BUGUNGI sana, tasdiqlovchi nomidan.
        Assert.All(mirror, e => Assert.Equal(AppClock.Today, e.EntryDate));
        Assert.All(mirror, e => Assert.Equal(director.Id, e.CreatedBy));
        Assert.All(mirror, e => Assert.NotNull(e.ReversalOf));
        Assert.Equal(Accounts.ExpenseRepair,
            Assert.Single(mirror, e => e.Direction == LedgerDirection.Credit).Account);
        Assert.Equal(Accounts.Cash,
            Assert.Single(mirror, e => e.Direction == LedgerDirection.Debit).Account);

        // P&L: sanadan bugungacha bo'lgan davrda chiqim NOLGA qaytadi.
        var pnl = await GetPnlAsync(directorClient, day, AppClock.Today);
        Assert.Equal(0m, pnl.Expense.Where(l => l.Account == Accounts.ExpenseRepair).Sum(l => l.Amount));
    }

    /// <summary>Ikki marta storno = pulni ikki marta "qaytarish" — 409.</summary>
    [Fact]
    public async Task Ikkinchi_storno_rad_etiladi_409()
    {
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var (_, directorClient) = await ActorAsync(Roles.SuperAdmin);

        var created = await CreateAsync(
            adminClient, new DateOnly(2024, 8, 21), "other", 55_000m, PaymentMethod.Cash);

        Assert.Equal(HttpStatusCode.OK, (await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/reverse", new { reason = "birinchi" })).StatusCode);

        var second = await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/reverse", new { reason = "ikkinchi" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_reversed", (await ErrorAsync(second)).Code);
    }

    /// <summary>
    /// Jurnalga o'zi qo'ygan chiqimni o'zi storno qila olmaydi (SPEC §4.5).
    /// </summary>
    [Fact]
    public async Task Ozi_jurnalga_qoygan_chiqimni_ozi_storno_qila_olmaydi_403()
    {
        var (_, directorClient) = await ActorAsync(Roles.SuperAdmin);

        var created = await CreateAsync(
            directorClient, new DateOnly(2024, 8, 22), "other", 40_000m, PaymentMethod.Cash);

        var response = await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/reverse", new { reason = "o'zim yozdim, o'zim bekor qilaman" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("self_reversal", (await ErrorAsync(response)).Code);
    }

    /// <summary>
    /// Tasdiq kutayotgan chiqimda storno qiladigan pul harakati yo'q — 409.
    /// </summary>
    [Fact]
    public async Task Jurnalga_tushmagan_chiqimni_storno_qilib_bolmaydi_409()
    {
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var (_, directorClient) = await ActorAsync(Roles.SuperAdmin);

        var created = await CreateAsync(
            adminClient, new DateOnly(2024, 8, 23), "salary", 9_000_000m, PaymentMethod.Transfer);
        Assert.Equal(ExpenseStatus.Pending, created.Status);

        var response = await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/reverse", new { reason = "xato summa" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_posted", (await ErrorAsync(response)).Code);
    }

    /// <summary>Sababsiz storno bo'lmaydi — u jurnal satrida qoladi (SPEC §4.3).</summary>
    [Fact]
    public async Task Sababsiz_storno_400()
    {
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var (_, directorClient) = await ActorAsync(Roles.SuperAdmin);

        var created = await CreateAsync(
            adminClient, new DateOnly(2024, 8, 24), "other", 12_000m, PaymentMethod.Cash);

        var response = await directorClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/reverse", new { reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("reason_required", (await ErrorAsync(response)).Code);
    }

    // =================================================================
    //  5. Ruxsat — SPEC §4.3 "Record an expense"
    // =================================================================

    /// <summary>
    /// <b>Asosiy ruxsat mezoni.</b> SPEC §4.3 jadvalining "Record an expense"
    /// qatorida kassir ustuni ⛔ — u chiqim yoza ham, ko'ra ham olmaydi.
    /// </summary>
    [Fact]
    public async Task Kassir_chiqimga_umuman_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);

        var create = await client.PostAsJsonAsync(Url, new
        {
            onDate = new DateOnly(2024, 9, 1),
            category = "other",
            amount = 10_000m,
            method = PaymentMethod.Cash,
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{Url}/{id}/reverse", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{Url}/{id}/approve", new { method = PaymentMethod.Cash })).StatusCode);
    }

    /// <summary>
    /// O'qituvchi va xodim ham yopiq — §4.3 jadvalida ular uchun ustun yo'q.
    /// Xodimga "finance" ruxsat kaliti berilgan bo'lsa ham (eski yo'l) yangi
    /// moliya yuzasi ochilmaydi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Oqituvchi_va_xodim_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    /// <summary>Token'siz so'rov — 401 (403 emas): kim so'rayotgani noma'lum.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
    }

    /// <summary>
    /// Tasdiqlash va storno — FAQAT direktor (<c>ApproveExpense</c>, §4.5 dagi
    /// "ikkinchi shaxs"). Admin chiqim YOZA oladi, lekin tasdiqlay olmaydi.
    /// </summary>
    [Fact]
    public async Task Adminga_tasdiqlash_va_storno_yopiq_403()
    {
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var (_, otherAdminClient) = await ActorAsync(Roles.Admin);

        var created = await CreateAsync(
            adminClient, new DateOnly(2024, 9, 2), "other", 30_000m, PaymentMethod.Cash);

        Assert.Equal(HttpStatusCode.Forbidden, (await otherAdminClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/approve", new { method = PaymentMethod.Cash })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherAdminClient.PostAsJsonAsync(
            $"{Url}/{created.Id}/reverse", new { reason = "xato" })).StatusCode);
    }

    // =================================================================
    //  6. Controllerning o'zi — SPEC §4.1, §4.4
    // =================================================================

    /// <summary>
    /// Qabul mezoni: <c>ExpensesController</c> da tahrirlash va o'chirish HTTP
    /// fe'llari NOL marta uchraydi. Grep o'rniga refleksiya — u qurilgan
    /// assembly ustida ishlaydi va izohdagi so'zga aldanmaydi.
    /// </summary>
    [Fact]
    public void Controllerda_tahrirlash_va_ochirish_amallari_yoq()
    {
        foreach (var action in ControllerActions())
        {
            Assert.Empty(action.GetCustomAttributes<HttpPutAttribute>(inherit: true));
            Assert.Empty(action.GetCustomAttributes<HttpDeleteAttribute>(inherit: true));
            Assert.Empty(action.GetCustomAttributes<HttpPatchAttribute>(inherit: true));
        }
    }

    /// <summary>
    /// HAR BIR endpoint rol darvozasi ortida turadi — unutilgan endpoint
    /// jimgina ochiq qolmasin (SPEC §4.3).
    /// </summary>
    [Fact]
    public void Har_bir_endpoint_rol_darvozasi_ortida()
    {
        var actions = ControllerActions()
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .ToList();

        Assert.NotEmpty(actions);
        foreach (var action in actions)
            Assert.NotNull(action.GetCustomAttribute<FinanceRoleAttribute>(inherit: true));
    }

    /// <summary>
    /// SPEC §4.4 — <c>created_by</c> so'rov tanasida kelsa 400. Jimgina
    /// e'tiborsiz qoldirish yomonroq: yuboruvchi chiqimni boshqa odam nomiga
    /// yozdim deb o'ylab qolardi.
    /// </summary>
    [Theory]
    [InlineData("createdBy")]
    [InlineData("approvedBy")]
    [InlineData("status")]
    public async Task Shaxs_va_holat_tanada_kelsa_400(string field)
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var body = new Dictionary<string, object?>
        {
            ["onDate"] = new DateOnly(2024, 9, 3),
            ["category"] = "other",
            ["amount"] = 10_000m,
            ["method"] = PaymentMethod.Cash,
            [field] = "boshqa-odam",
        };

        var response = await client.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("identity_in_body", (await ErrorAsync(response)).Code);
    }

    // =================================================================
    //  7. So'rovni tekshirish
    // =================================================================

    [Theory]
    // Noma'lum toifa: jurnal hisobi yopiq ro'yxat, "utilites" ni qabul qilish
    // pulni jimgina boshqa hisobga yuborardi.
    [InlineData("utilites", 10_000, PaymentMethod.Cash, "invalid_category")]
    [InlineData("", 10_000, PaymentMethod.Cash, "invalid_category")]
    [InlineData("other", 0, PaymentMethod.Cash, "invalid_amount")]
    [InlineData("other", -5_000, PaymentMethod.Cash, "invalid_amount")]
    [InlineData("other", 10_000, "naqd", "invalid_method")]
    public async Task Notogri_sorov_400(string category, int amount, string method, string code)
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(Url, new
        {
            onDate = new DateOnly(2024, 9, 4),
            category,
            amount = (decimal)amount,
            method,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await ErrorAsync(response)).Code);
    }

    /// <summary>
    /// Kelajakdagi sana — hali bo'lmagan pul harakati; sanasiz so'rov esa
    /// <c>default(DateOnly)</c> = 0001-01-01 bo'lib bog'lanadi va hech qanday
    /// hisobotga tushmaydigan davrga yozilardi.
    /// </summary>
    [Fact]
    public async Task Kelajak_va_bosh_sana_400()
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var future = await client.PostAsJsonAsync(Url, new
        {
            onDate = AppClock.Today.AddDays(1),
            category = "other",
            amount = 10_000m,
            method = PaymentMethod.Cash,
        });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal("future_date", (await ErrorAsync(future)).Code);

        var missing = await client.PostAsJsonAsync(Url, new
        {
            category = "other",
            amount = 10_000m,
            method = PaymentMethod.Cash,
        });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("invalid_date", (await ErrorAsync(missing)).Code);
    }

    /// <summary>Yo'q chiqim — 404, ya'ni "bor, lekin ko'rsatmayman" emas.</summary>
    [Fact]
    public async Task Yoq_chiqim_404()
    {
        var (_, client) = await ActorAsync(Roles.SuperAdmin);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{id}")).StatusCode);

        var reverse = await client.PostAsJsonAsync($"{Url}/{id}/reverse", new { reason = "x" });
        Assert.Equal(HttpStatusCode.NotFound, reverse.StatusCode);
        Assert.Equal("expense_not_found", (await ErrorAsync(reverse)).Code);
    }

    // =================================================================
    //  8. Filtrlar
    // =================================================================

    /// <summary>Davr va toifa bo'yicha filtr — chegaradan tashqaridagi qator tushmaydi.</summary>
    [Fact]
    public async Task Davr_va_toifa_boyicha_filtr()
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var inside = await CreateAsync(client, new DateOnly(2024, 10, 10), "rent", 70_000m, PaymentMethod.Cash);
        var otherCategory = await CreateAsync(client, new DateOnly(2024, 10, 11), "supplies", 20_000m, PaymentMethod.Cash);
        var outside = await CreateAsync(client, new DateOnly(2024, 11, 1), "rent", 30_000m, PaymentMethod.Cash);

        var list = await client.GetFromJsonAsync<List<ExpenseDto>>(
            $"{Url}?from=2024-10-01&to=2024-10-31&category=rent");

        Assert.NotNull(list);
        Assert.Contains(list, e => e.Id == inside.Id);
        Assert.DoesNotContain(list, e => e.Id == otherCategory.Id);
        Assert.DoesNotContain(list, e => e.Id == outside.Id);
    }

    /// <summary>Noma'lum holat filtri jimgina hammasi bo'lib qolmaydi — 400.</summary>
    [Fact]
    public async Task Nomalum_holat_filtri_400()
    {
        var (_, client) = await ActorAsync(Roles.Admin);

        var response = await client.GetAsync($"{Url}?status=approved");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_status", (await ErrorAsync(response)).Code);
    }

    // =================================================================
    //  9. Toifa → hisob xaritasi
    // =================================================================

    /// <summary>
    /// Har chiqim toifasining AYNAN bitta jurnal hisobi bor va u yopiq
    /// ro'yxatda. Xato yozilgan toifa "boshqa" ga tushib ketmaydi — u umuman
    /// qabul qilinmaydi.
    /// </summary>
    [Fact]
    public void Har_chiqim_toifasi_yopiq_royxatdagi_hisobga_xaritalanadi()
    {
        Assert.Equal(
            ["other", "rent", "repair", "salary", "supplies", "utilities"],
            Accounts.ExpenseCategories.OrderBy(c => c, StringComparer.Ordinal));

        foreach (var category in Accounts.ExpenseCategories)
        {
            var account = Accounts.ExpenseFor(category);
            Assert.Equal($"expense:{category}", account);
            Assert.True(Accounts.IsKnown(account), $"{account} yopiq ro'yxatda yo'q.");
        }

        Assert.False(Accounts.IsExpenseCategory("utilites"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Accounts.ExpenseFor("utilites"));
    }

    // =================================================================
    //  Yordamchilar
    // =================================================================

    private static MethodInfo[] ControllerActions() =>
        typeof(ExpensesController).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

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

    private static async Task<ExpenseDto> ApproveAsync(HttpClient client, Guid id, string method)
    {
        var response = await client.PostAsJsonAsync($"{Url}/{id}/approve", new { method });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
    }

    private static async Task<ProfitLossDto> GetPnlAsync(HttpClient client, DateOnly from, DateOnly to) =>
        (await client.GetFromJsonAsync<ProfitLossDto>(
            $"{Pnl}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"))!;

    private static async Task<BillingErrorDto> ErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<BillingErrorDto>())!;

    private async Task<int> CountExpensesAsync()
    {
        await using var db = NewDb();
        return await db.Expenses.CountAsync();
    }

    private static string Flatten(Exception? ex)
    {
        var text = new System.Text.StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException) text.Append(e.Message).Append(' ');
        return text.ToString();
    }
}
