using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  JAVONDAN CHIQQAN NAQD — F1.03 (naqd chiqim) va F1.04 (topshiriq).
//  Manba: docs/modules/finance-parity.md §1.1, §2.1.3, §4 (slice S2).
// ===========================================================================
//
//  NEGA BU TESTLAR BOR
//  -------------------
//  Ilgari `CashShiftService.ExpectedCashAsync` FAQAT to'lovlarni sanardi.
//  Naqd chiqim jurnalga `credit cash` bo'lib tushardi, lekin hech bir
//  smenaga bog'lanmagani uchun kutilgan naqdga TA'SIR QILMASDI: kassadan pul
//  chiqadi, kutilgan summa o'zgarmaydi, smena aynan o'sha summaga kam pul
//  bilan yopiladi va `shift_variance` bayrog'i aybsiz kassirning ustiga
//  tushadi. Har naqd chiqim uchun, har kuni. Xuddi shu holat bankka
//  topshirilgan pulda ham bor edi, faqat u yerda yozuvning O'ZI yo'q edi.
//
//  ENG MUHIM TEST: `Zhisobot_va_kutilgan_naqd_bir_xil_javob_beradi` — u
//  Z-hisobotdagi qatorlar bilan `expected_cash` AYNAN bir manbadan kelishini
//  tekshiradi. Ikkita "haqiqat" paydo bo'lsa, tekshiruvchi qaysi biriga
//  ishonishni bilmasdi.
//
//  SANALAR VA SUMMALAR
//  -------------------
//  Chiqimlar 2022-yil sanalari bilan yoziladi: `ExpensesTests` 2024-ni,
//  `ExpenseUiContractTests` 2023-ni band qilgan va P&L testlari davr bo'yicha
//  yig'indi hisoblaydi. Smena arifmetikasi sanaga bog'liq emas (u
//  `cash_shift_id` bo'yicha ishlaydi), shuning uchun bu xavfsiz.
//  Summalar tasdiq chegarasidan (5 000 000) PAST — aks holda chiqim jurnalga
//  darhol tushmasdi va test boshqa narsani o'lchardi.
// ===========================================================================

/// <summary>
/// Naqd chiqim, kassadan topshiriq va ularning smena arifmetikasiga ta'siri
/// (F1.03, F1.04). Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashDeskOutflowTests(ApiFixture fixture)
{
    private const string Expenses = "/api/admin/expenses";
    private const string Handovers = "/api/cash/handovers";
    private const string Shifts = "/api/cash/shifts";

    /// <summary>Boshqa moliya testlari band qilmagan yil.</summary>
    private static readonly DateOnly Day = new(2022, 6, 14);

    /// <summary>Huquq rad etilganda PostgreSQL qaytaradigan kod.</summary>
    private const string PermissionDenied = "42501";

    private sealed record ErrorBody(string Code, string Message);

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. F1.03 — naqd chiqim kutilgan naqdni KAMAYTIRADI
    // =====================================================================

    /// <summary>
    /// <b>Modulning asosiy natijasi — endi KASSA orqali (mijoz javobi,
    /// 2026-09: "smena" tushunchasi olib tashlandi).</b> Naqd chiqim
    /// yozuvchining SMENASIGA emas, SUKUT (yoki so'ralgan) KASSAGA
    /// biriktiriladi — smena ochiq bo'lishi shart emas va biriktirish
    /// ustuni endi <c>cash_box_id</c>.
    ///
    /// <para>
    /// Ilgari bu yerda smena ochilib, uning "farqsiz yopilishi" tekshirilardi
    /// — bu qoidaning o'zi olib tashlandi (<c>CashShiftService</c> endi
    /// PaymentService/ExpenseService yo'liga umuman ulanmaydi). Endi shu
    /// o'rinda tekshiriladigan qoida: naqd chiqim SMENASIZ ham qabul qilinadi
    /// va kassa (<c>cash_box_id</c>) bilan belgilanadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Naqd_chiqim_smenasiz_qabul_qilinadi_va_sukut_kassaga_biriktiriladi()
    {
        var (admin, client) = await ClientAsync(Roles.Admin);
        // Smena ATAYLAB ochilmaydi — kassalar modelida bu endi shart emas.

        var expense = await CreateExpenseAsync(client, "utilities", 300_000m, PaymentMethod.Cash);

        Assert.Equal(ExpenseStatus.Posted, expense.Status);
        Assert.Equal(Accounts.Cash, expense.SettlementAccount);
        // "Smena" endi yo'q — har doim null.
        Assert.Null(expense.CashShiftId);

        await using var db = NewDb();
        var stored = await db.Expenses.AsNoTracking().FirstAsync(e => e.Id == expense.Id);
        Assert.Null(stored.CashShiftId);
        // Kassa ko'rsatilmagan — SUKUT (default) kassaga tushdi.
        var defaultBoxId = await CashBoxService.DefaultBoxIdAsync(db);
        Assert.Equal(defaultBoxId, stored.CashBoxId);
        Assert.Equal(admin.Id, stored.CreatedBy);
    }

    /// <summary>
    /// Naqd BO'LMAGAN chiqim smenaga umuman tegmaydi: pul bank hisobidan
    /// chiqadi, javondagi naqd o'zgarmaydi.
    ///
    /// <para>
    /// Nazorat tekshiruvi sifatida ham qimmatli: yuqoridagi test "har qanday
    /// chiqim kutilgan naqdni kamaytiradi" degan NOTO'G'RI implementatsiya
    /// bilan ham yashil bo'lardi.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Transfer)]
    [InlineData(PaymentMethod.Online)]
    public async Task Bank_chiqimi_kutilgan_naqdga_tegmaydi(string method)
    {
        var (_, client) = await ClientAsync(Roles.Admin);
        var shift = await OpenShiftAsync(client, openingFloat: 1_000_000m);

        var expense = await CreateExpenseAsync(client, "supplies", 300_000m, method);

        Assert.Equal(Accounts.Bank, expense.SettlementAccount);
        Assert.Null(expense.CashShiftId);

        var closed = await CloseShiftAsync(client, shift.Id, countedCash: 1_000_000m);

        Assert.Equal(1_000_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);
    }

    /// <summary>
    /// Kassalar modeli (2026-09): "smena" endi yo'q, shuning uchun smenasiz
    /// naqd chiqim endi ODDIY holat (yuqoridagi test). Buning o'rniga qoladigan
    /// qoida — FAOL BO'LMAGAN kassaga naqd chiqim — <b>409
    /// <c>cash_box_inactive</c></b>, va bazada HECH NARSA qolmaydi.
    ///
    /// <para>
    /// Status kodining o'zi yetarli emas: agar endpoint 409 qaytarib, chiqim
    /// qatorini baribir yozib qo'ysa, faqat kodni tekshiradigan test buni
    /// ko'rmasdi — va o'sha pul hech qaysi kassaga tushmagan holda P&L da
    /// paydo bo'lardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Faol_bolmagan_kassaga_naqd_chiqim_409_va_bazada_iz_qoldirmaydi()
    {
        var (admin, client) = await ClientAsync(Roles.Admin);

        var inactiveBoxId = Guid.Empty;
        await fixture.Api.WithDbAsync(async db =>
        {
            var box = new CashBox
            {
                Name = "Faol emas " + Guid.NewGuid().ToString("N")[..6],
                IsDefault = false,
                IsActive = false,
                CreatedAt = AppClock.NowInstant,
            };
            db.CashBoxes.Add(box);
            await db.SaveChangesAsync();
            inactiveBoxId = box.Id;
        });

        var response = await client.PostAsJsonAsync(Expenses, new
        {
            onDate = Day,
            category = "rent",
            amount = 400_000m,
            method = PaymentMethod.Cash,
            note = "Faol bo'lmagan kassaga urinish",
            cashBoxId = inactiveBoxId,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<ErrorBody>())!;
        Assert.Equal("cash_box_inactive", error.Code);

        await using var db = NewDb();
        Assert.Empty(await db.Expenses.AsNoTracking().Where(e => e.CreatedBy == admin.Id).ToListAsync());
    }

    /// <summary>
    /// Chegaradan yuqori chiqim TASDIQ kutadi — o'sha lahzada kassa talab
    /// qilinmaydi (jurnalga hali tushmagan, pul hali chiqmagan). Kassa
    /// TASDIQLOVCHI so'rovida ko'rsatiladi (yoki SUKUT) va chiqim AYNAN
    /// o'sha kassaga biriktiriladi.
    ///
    /// <para>
    /// Sabab: usulni ham, pulni ham tasdiqlovchi beradi (<c>ExpenseService</c>
    /// fayl boshidagi izoh) — ya'ni pul aynan uning ko'rsatgan kassasidan chiqadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tasdiq_kutgan_naqd_chiqim_tasdiqlovchi_korsatgan_kassaga_tushadi()
    {
        var (_, author) = await ClientAsync(Roles.Admin);
        var (_, director) = await ClientAsync(Roles.SuperAdmin);

        const decimal amount = 6_000_000m;   // chegaradan (5 000 000) YUQORI

        // 1) Yozuvchida smena YO'Q — baribir qabul qilinadi, chunki pul hali chiqmadi.
        var created = await CreateExpenseAsync(author, "repair", amount, PaymentMethod.Cash);
        Assert.Equal(ExpenseStatus.Pending, created.Status);
        Assert.Null(created.CashShiftId);
        Assert.Null(created.SettlementAccount);

        // 2) Yangi kassa ochiladi (direktor ham ManageCashBoxes ga kira oladi —
        // AdminAndDirector) va tasdiqlovchi shu kassani ko'rsatib tasdiqlaydi.
        var box = await CreateBoxAsync(director);

        var approveResponse = await director.PostAsJsonAsync(
            $"{Expenses}/{created.Id}/approve", new { method = PaymentMethod.Cash, cashBoxId = box.Id });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var approved = (await approveResponse.Content.ReadFromJsonAsync<ExpenseDto>())!;
        Assert.Equal(ExpenseStatus.Posted, approved.Status);
        Assert.Null(approved.CashShiftId);
        Assert.Equal(box.Id, approved.CashBoxId);
    }

    /// <summary>
    /// Naqd chiqimning STORNOSI endi HECH QANDAY kassa/smena talab qilmaydi
    /// (kassalar modeli, 2026-09) — <c>expenses</c> ga bu qadamda hech qanday
    /// ustun yozilmaydi (fayl boshidagi izoh: storno faqat jurnalga ko'zgu
    /// satr qo'shadi), ya'ni biriktiriladigan maydon ham yo'q.
    /// </summary>
    [Fact]
    public async Task Naqd_chiqim_stornosi_smenasiz_ham_otadi()
    {
        var (_, author) = await ClientAsync(Roles.Admin);
        var (_, director) = await ClientAsync(Roles.SuperAdmin);

        var expense = await CreateExpenseAsync(author, "other", 200_000m, PaymentMethod.Cash);
        Assert.Null(expense.CashShiftId);

        // Direktorda ochiq smena YO'Q va u ATAYLAB ochilmaydi — storno baribir o'tadi.
        var reverseResponse = await director.PostAsJsonAsync(
            $"{Expenses}/{expense.Id}/reverse", new { reason = "Xato yozuv" });
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);
        Assert.Equal(ExpenseStatus.Reversed,
            (await reverseResponse.Content.ReadFromJsonAsync<ExpenseDto>())!.Status);
    }

    // =====================================================================
    //  2. F1.04 — kassadan topshirish
    // =====================================================================

    /// <summary>
    /// Bankka topshirilgan pul javondan chiqadi VA jurnalga tushadi:
    /// <c>debit bank / credit cash</c>, <c>ref_type = cash_handover</c>.
    /// </summary>
    [Fact]
    public async Task Bankka_topshiriq_javonni_kamaytiradi_va_jurnalga_tushadi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 500_000m);

        var handover = await RecordHandoverAsync(client, 200_000m, "bank", "Inkassatsiya №145");

        Assert.Equal(shift.Id, handover.CashShiftId);
        Assert.Equal(cashier.Id, handover.CashierId);
        Assert.Equal("bank", handover.Destination);
        Assert.Null(handover.ReversalOf);
        Assert.False(handover.Reversed);

        // --- Jurnal: AYNAN ikki satr, balanslashgan ---
        await using (var db = NewDb())
        {
            var entries = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefId == handover.Id).ToListAsync();

            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal(LedgerRefType.CashHandover, e.RefType));

            var debit = Assert.Single(entries, e => e.Direction == LedgerDirection.Debit);
            Assert.Equal(Accounts.Bank, debit.Account);
            Assert.Equal(200_000m, debit.Amount);

            var credit = Assert.Single(entries, e => e.Direction == LedgerDirection.Credit);
            Assert.Equal(Accounts.Cash, credit.Account);
            Assert.Equal(200_000m, credit.Amount);
        }

        var closed = await CloseShiftAsync(client, shift.Id, countedCash: 300_000m);
        Assert.Equal(300_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);
    }

    /// <summary>
    /// Direktorning seyfiga berilgan pul JURNALGA YOZILMAYDI (u ham
    /// <c>cash</c> hisobida qolaveradi), lekin smenaning kutilgan naqdini
    /// baribir kamaytiradi.
    ///
    /// <para>
    /// Bu farq ataylab: hisoblar rejasida seyf uchun alohida hisob yo'q va
    /// <c>debit cash / credit cash</c> nolga teng, ma'nosiz yozuv bo'lardi.
    /// Shuning uchun topshiriq jurnaldan emas, <c>cash_handovers</c> dan
    /// o'qiladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Seyfga_topshiriq_jurnalga_yozilmaydi_lekin_javonni_kamaytiradi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 500_000m);

        var handover = await RecordHandoverAsync(client, 100_000m, "safe", null);

        await using (var db = NewDb())
        {
            Assert.Empty(await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefId == handover.Id).ToListAsync());
        }

        var closed = await CloseShiftAsync(client, shift.Id, countedCash: 400_000m);
        Assert.Equal(400_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);
    }

    /// <summary>
    /// STORNO pulni javonga QAYTARADI: qarshi qator qo'shiladi (original
    /// tegilmaydi) va jurnalga teskari yo'nalishdagi partiya tushadi.
    ///
    /// <para>
    /// E'tibor bering: storno kutilgan naqdni OSHIRADI, ya'ni kassirdan
    /// KO'PROQ pul talab qiladi. Aynan shuning uchun uni kassirning o'zi
    /// qila oladi — to'lov stornosidan farqli o'laroq, undan o'z foydasiga
    /// foydalanib bo'lmaydi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Topshiriq_stornosi_pulni_javonga_qaytaradi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 500_000m);

        var handover = await RecordHandoverAsync(client, 200_000m, "bank", null);

        var reverseResponse = await client.PostAsJsonAsync(
            $"{Handovers}/{handover.Id}/reverse", new { reason = "Bankka yetkazilmadi" });
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);

        var mirror = (await reverseResponse.Content.ReadFromJsonAsync<CashHandoverDto>())!;
        Assert.Equal(handover.Id, mirror.ReversalOf);
        Assert.Equal(200_000m, mirror.Amount);
        Assert.Equal(shift.Id, mirror.CashShiftId);

        await using (var db = NewDb())
        {
            // ORIGINAL TEGILMAGAN — bu jadval faqat qo'shiladi (SPEC §4.1).
            var original = await db.CashHandovers.AsNoTracking().FirstAsync(h => h.Id == handover.Id);
            Assert.Null(original.ReversalOf);
            Assert.Equal(200_000m, original.Amount);
            Assert.Equal("bank", original.Destination);

            // Qarshi qator — ALOHIDA satr, originalning ustiga yozilmagan.
            var stored = await db.CashHandovers.AsNoTracking().FirstAsync(h => h.Id == mirror.Id);
            Assert.Equal(handover.Id, stored.ReversalOf);
            Assert.Equal("Bankka yetkazilmadi", stored.Note);

            // Qarshi partiya: pul bankdan kassaga qaytdi.
            var entries = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefId == mirror.Id).ToListAsync();
            Assert.Equal(2, entries.Count);
            var debit = Assert.Single(entries, e => e.Direction == LedgerDirection.Debit);
            Assert.Equal(Accounts.Cash, debit.Account);
            var credit = Assert.Single(entries, e => e.Direction == LedgerDirection.Credit);
            Assert.Equal(Accounts.Bank, credit.Account);
        }

        // Javon to'liq: 500 000 chiqdi-qaytdi.
        var closed = await CloseShiftAsync(client, shift.Id, countedCash: 500_000m);
        Assert.Equal(500_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);
    }

    /// <summary>Bitta topshiriqni ikki marta storno qilib bo'lmaydi — 409.</summary>
    [Fact]
    public async Task Ikki_marta_storno_409()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        await OpenShiftAsync(client, openingFloat: 500_000m);
        var handover = await RecordHandoverAsync(client, 50_000m, "safe", null);

        var first = await client.PostAsJsonAsync(
            $"{Handovers}/{handover.Id}/reverse", new { reason = "Birinchi" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"{Handovers}/{handover.Id}/reverse", new { reason = "Ikkinchi" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_reversed",
            (await second.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>
    /// <c>cash_handovers</c> — FAQAT QO'SHILADI (finance-parity §3.1 A2).
    /// Ilova roli (<c>app_rw</c>) bilan UPDATE va DELETE <b>42501</b> bilan
    /// rad etiladi.
    /// </summary>
    [Fact]
    public async Task Topshiriq_qatorini_app_rw_ozgartira_ham_ochira_ham_olmaydi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        await OpenShiftAsync(client, openingFloat: 300_000m);
        var handover = await RecordHandoverAsync(client, 120_000m, "safe", "Dalil");

        await using var connection = new NpgsqlConnection(fixture.Database.AppRwConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
                 {
                     "UPDATE cash_handovers SET amount = 1 WHERE id = @id",
                     "DELETE FROM cash_handovers WHERE id = @id",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", handover.Id);

            var pg = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PermissionDenied, pg.SqlState);
        }

        // Qator o'sha-o'sha.
        await using var db = NewDb();
        var stored = await db.CashHandovers.AsNoTracking().FirstAsync(h => h.Id == handover.Id);
        Assert.Equal(120_000m, stored.Amount);
    }

    /// <summary>Ochiq smenasiz topshiriq — 409 <c>no_open_shift</c>.</summary>
    [Fact]
    public async Task Ochiq_smenasiz_topshiriq_409()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);

        var response = await client.PostAsJsonAsync(
            Handovers, new { amount = 100_000m, destination = "bank" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_open_shift", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>
    /// SPEC §4.4 — smenani so'rov tanasidan berib bo'lmaydi: server uni
    /// kassirning ochiq smenasidan oladi. Tanada kelsa 400
    /// <c>identity_in_body</c>.
    /// </summary>
    [Fact]
    public async Task Tanadagi_cashShiftId_400_beradi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        await OpenShiftAsync(client, openingFloat: 100_000m);

        var response = await client.PostAsJsonAsync(Handovers, new
        {
            amount = 50_000m,
            destination = "bank",
            cashShiftId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("identity_in_body",
            (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>Manzil yopiq ro'yxatdan: uchinchi qiymat qabul qilinmaydi.</summary>
    [Fact]
    public async Task Notogri_manzil_400()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        await OpenShiftAsync(client, openingFloat: 100_000m);

        var response = await client.PostAsJsonAsync(
            Handovers, new { amount = 10_000m, destination = "pocket" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_destination",
            (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  3. ENG MUHIM — Z-hisobot va kutilgan naqd BIR XIL javob beradi
    // =====================================================================

    /// <summary>
    /// Kassalar modeli (2026-09): <b>agar kimdir baribir smena ochsa</b>
    /// (endi majburiy emas, lekin taqiqlanmagan ham — <c>CashShiftsController</c>
    /// tegilmagan), uning <c>expected_cash</c> i endi FAQAT topshiriqlarga
    /// (F1.04) bog'liq: naqd to'lov va naqd chiqim shu smenaga UMUMAN
    /// TA'SIR QILMAYDI (<c>PaymentService</c>/<c>ExpenseService</c> endi
    /// <c>ICashShiftService</c> ga bog'lanmaydi — "stop wiring into live
    /// paths").
    ///
    /// <para>
    /// <b>Invariant endi:</b> <c>ochilish qoldig'i − topshiriqlar ==
    /// kutilgan naqd</c> — to'lov va chiqim hadlari YO'QOLDI. Bu test AYNAN
    /// shu yo'qolishni tekshiradi: agar kimdir bu wiring'ni tasodifan
    /// qaytarib qo'ysa (regressiya), <c>expected_cash</c> naqd to'lov/chiqim
    /// summasiga "sirg'alib" ketadi va quyidagi tenglik buziladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Zhisobot_endi_faqat_topshiriqqa_bogliq_tolov_va_chiqim_smenaga_tegmaydi()
    {
        var (_, client) = await ClientAsync(Roles.Admin);
        var shift = await OpenShiftAsync(client, openingFloat: 1_000_000m);

        // Naqd to'lov — HAQIQIY `PaymentService` orqali. Endi SUKUT kassaga
        // tushadi, shu smenaga EMAS.
        var scene = await SceneAsync(400_000m);
        var payment = await client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = scene.StudentId,
            amount = 400_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId = scene.InvoiceId, amount = 400_000m } },
        });
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
        Assert.Null((await payment.Content.ReadFromJsonAsync<PaymentDto>())!.CashShiftId);

        var expense = await CreateExpenseAsync(client, "supplies", 150_000m, PaymentMethod.Cash);
        Assert.Null(expense.CashShiftId);
        await CreateExpenseAsync(client, "rent", 900_000m, PaymentMethod.Transfer);   // bankdan — ta'sirsiz

        await RecordHandoverAsync(client, 300_000m, "bank", null);
        await RecordHandoverAsync(client, 100_000m, "safe", null);

        var undone = await RecordHandoverAsync(client, 50_000m, "safe", null);
        var reverse = await client.PostAsJsonAsync(
            $"{Handovers}/{undone.Id}/reverse", new { reason = "Noto'g'ri yozildi" });
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);

        // 1 000 000 − 300 000 − 100 000 = 600 000. Naqd to'lov (+400 000) va
        // naqd chiqim (−150 000) ENDI BU YERDA YO'Q — ular boshqa kassaga tegishli.
        var closed = await CloseShiftAsync(client, shift.Id, countedCash: 600_000m);
        Assert.Equal(600_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);

        var report = await GetZReportAsync(client, shift.Id);

        // Naqd chiqim shu smenaga umuman biriktirilmadi.
        Assert.Equal(0m, report.CashExpensesTotal);
        Assert.Equal(0, report.CashExpensesCount);
        // 300 000 + 100 000 + 50 000 − 50 000 (storno)
        Assert.Equal(400_000m, report.CashHandoversTotal);
        Assert.Equal(4, report.CashHandoversCount);

        // Z-hisobotning "naqd to'lovlar" qatori ham BO'SH — to'lov shu smenaga
        // yozilmadi (smena hech qanday `payments.cash_shift_id` ga ega emas).
        var cashRow = Assert.Single(report.ByMethod, m => m.Method == PaymentMethod.Cash);
        Assert.Equal(0m, cashRow.Amount);

        // ---- INVARIANT: endi FAQAT ochilish qoldig'i va topshiriqlar ----
        var fromReport = report.Shift.OpeningFloat - report.CashHandoversTotal;

        Assert.Equal(fromReport, report.Shift.ExpectedCash!.Value);
        Assert.Equal(600_000m, fromReport);
    }

    // =====================================================================
    //  4. F1.08 — chiqim hujjatlari
    // =====================================================================

    /// <summary>
    /// Hujjat biriktiriladi, ro'yxatda ko'rinadi va chiqim DTO'sida sanaladi.
    /// Fayl MAVJUD yuklash yo'lidan keladi — ikkinchi yuklash endpoint'i yo'q.
    /// </summary>
    [Fact]
    public async Task Hujjat_biriktiriladi_va_royxatda_korinadi()
    {
        var (admin, client) = await ClientAsync(Roles.Admin);
        var expense = await CreateExpenseAsync(client, "supplies", 90_000m, PaymentMethod.Card);

        var response = await client.PostAsJsonAsync($"{Expenses}/{expense.Id}/attachments", new
        {
            fileUrl = "/uploads/a1b2c3.pdf",
            fileName = "hisob-faktura.pdf",
            contentType = "application/pdf",
            sizeBytes = 51_200L,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attachment = (await response.Content.ReadFromJsonAsync<ExpenseAttachmentDto>())!;
        Assert.Equal(expense.Id, attachment.ExpenseId);
        Assert.Equal(admin.Id, attachment.UploadedBy);       // §4.4 — JWT'dan
        Assert.Equal("hisob-faktura.pdf", attachment.FileName);

        var list = await client.GetFromJsonAsync<List<ExpenseAttachmentDto>>(
            $"{Expenses}/{expense.Id}/attachments");
        Assert.Single(list!);

        var reloaded = await GetExpenseAsync(client, expense.Id);
        Assert.Equal(1, reloaded.AttachmentCount);
    }

    /// <summary>
    /// Tashqi URL, ruxsat etilmagan kengaytma va nol hajm — uchalasi ham 400.
    ///
    /// <para>
    /// Tashqi URL eng muhimi: uni biz nazorat qilmaymiz, ya'ni "dalil"
    /// istalgan payt almashib qolishi mumkin — jadval INSERT-only bo'lgani
    /// bunga hech qanday to'siq emas, chunki o'zgaradigan narsa fayl, qator
    /// emas.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("https://example.com/chek.pdf", "chek.pdf", "application/pdf", 1024, "invalid_file_url")]
    [InlineData("/uploads/x.svg", "x.svg", "image/svg+xml", 1024, "invalid_file_type")]
    [InlineData("/uploads/x.pdf", "x.pdf", "application/pdf", 0, "invalid_file_size")]
    public async Task Yaroqsiz_hujjat_400(
        string fileUrl, string fileName, string contentType, long sizeBytes, string code)
    {
        var (_, client) = await ClientAsync(Roles.Admin);
        var expense = await CreateExpenseAsync(client, "other", 40_000m, PaymentMethod.Card);

        var response = await client.PostAsJsonAsync($"{Expenses}/{expense.Id}/attachments",
            new { fileUrl, fileName, contentType, sizeBytes });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var reloaded = await GetExpenseAsync(client, expense.Id);
        Assert.Equal(0, reloaded.AttachmentCount);
    }

    /// <summary>
    /// <c>expense_attachments</c> — DALIL, ya'ni faqat SELECT va INSERT
    /// (finance-parity §3.1 A4). <c>app_rw</c> bilan UPDATE va DELETE
    /// <b>42501</b> bilan rad etiladi.
    /// </summary>
    [Fact]
    public async Task Hujjatni_app_rw_ozgartira_ham_ochira_ham_olmaydi()
    {
        var (_, client) = await ClientAsync(Roles.Admin);
        var expense = await CreateExpenseAsync(client, "other", 70_000m, PaymentMethod.Card);

        var created = await client.PostAsJsonAsync($"{Expenses}/{expense.Id}/attachments", new
        {
            fileUrl = "/uploads/dalil.jpg",
            fileName = "dalil.jpg",
            contentType = "image/jpeg",
            sizeBytes = 2048L,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var attachment = (await created.Content.ReadFromJsonAsync<ExpenseAttachmentDto>())!;

        await using var connection = new NpgsqlConnection(fixture.Database.AppRwConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
                 {
                     "UPDATE expense_attachments SET file_url = '/uploads/boshqa.jpg' WHERE id = @id",
                     "DELETE FROM expense_attachments WHERE id = @id",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", attachment.Id);

            var pg = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PermissionDenied, pg.SqlState);
        }

        await using var db = NewDb();
        var stored = await db.ExpenseAttachments.AsNoTracking().FirstAsync(a => a.Id == attachment.Id);
        Assert.Equal("/uploads/dalil.jpg", stored.FileUrl);
    }

    // =====================================================================
    //  5. RBAC — HAR BIR RAD ETISH (SPEC §4.3)
    // =====================================================================

    /// <summary>
    /// Topshirish — kassa stolining amali: kassir, admin va direktor
    /// (<c>FinanceAction.ManageOwnShift</c> bilan AYNAN bir xil to'plam).
    /// O'qituvchi va xodim — <b>403</b>, token'siz — <b>401</b>.
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Topshiriq_ruxsatsiz_rolga_403(string role)
    {
        var (_, client) = await ClientAsync(role);

        var post = await client.PostAsJsonAsync(
            Handovers, new { amount = 10_000m, destination = "bank" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

        var get = await client.GetAsync(Handovers);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        var reverse = await client.PostAsJsonAsync(
            $"{Handovers}/{Guid.NewGuid()}/reverse", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, reverse.StatusCode);
    }

    /// <summary>Token'siz so'rov — 401, hamma uchta endpointda.</summary>
    [Fact]
    public async Task Topshiriq_tokensiz_401()
    {
        var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync(Handovers)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Handovers, new { amount = 1m, destination = "bank" })).StatusCode);
    }

    /// <summary>
    /// Kassir FAQAT o'z smenalaridan chiqqan pulni ko'radi — so'rovda boshqa
    /// <c>cashierId</c> yozgan bo'lsa ham. Bitta qatorni ochishga urinish —
    /// <b>403</b>.
    /// </summary>
    [Fact]
    public async Task Kassir_ozganing_topshirigini_kormaydi()
    {
        var (first, firstClient) = await ClientAsync(Roles.Cashier);
        var (_, secondClient) = await ClientAsync(Roles.Cashier);

        await OpenShiftAsync(firstClient, openingFloat: 200_000m);
        var hidden = await RecordHandoverAsync(firstClient, 60_000m, "safe", null);

        // Ikkinchi kassir birinchisining id'si bilan so'raydi — filtr jimgina
        // o'zinikiga toraytiriladi.
        var list = await secondClient.GetFromJsonAsync<List<CashHandoverDto>>(
            $"{Handovers}?cashierId={first.Id}");
        Assert.DoesNotContain(list!, h => h.Id == hidden.Id);

        var single = await secondClient.GetAsync($"{Handovers}/{hidden.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, single.StatusCode);
        Assert.Equal("not_your_shift",
            (await single.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Storno ham rad etiladi — o'z smenasi ochiq bo'lsa ham.
        await OpenShiftAsync(secondClient, openingFloat: 0m);
        var reverse = await secondClient.PostAsJsonAsync(
            $"{Handovers}/{hidden.Id}/reverse", new { reason = "Meniki emas" });
        Assert.Equal(HttpStatusCode.Forbidden, reverse.StatusCode);
        Assert.Equal("not_your_shift",
            (await reverse.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>Admin va direktor hamma kassirning topshiriqlarini ko'radi.</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Nazoratchi_hamma_topshiriqni_koradi(string role)
    {
        var (cashier, cashierClient) = await ClientAsync(Roles.Cashier);
        await OpenShiftAsync(cashierClient, openingFloat: 150_000m);
        var handover = await RecordHandoverAsync(cashierClient, 30_000m, "bank", null);

        var (_, supervisor) = await ClientAsync(role);

        var list = await supervisor.GetFromJsonAsync<List<CashHandoverDto>>(
            $"{Handovers}?cashierId={cashier.Id}");
        Assert.Contains(list!, h => h.Id == handover.Id);

        var single = await supervisor.GetAsync($"{Handovers}/{handover.Id}");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
    }

    /// <summary>
    /// Hujjat biriktirish — chiqim yozish huquqi (admin/direktor). Kassir
    /// chiqim endpoint'lariga umuman kirmaydi (SPEC §4.3), o'qituvchi ham.
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Hujjat_endpointi_ruxsatsiz_rolga_403(string role)
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var expense = await CreateExpenseAsync(admin, "other", 25_000m, PaymentMethod.Card);

        var (_, client) = await ClientAsync(role);

        var post = await client.PostAsJsonAsync($"{Expenses}/{expense.Id}/attachments", new
        {
            fileUrl = "/uploads/x.pdf",
            fileName = "x.pdf",
            contentType = "application/pdf",
            sizeBytes = 100L,
        });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

        var get = await client.GetAsync($"{Expenses}/{expense.Id}/attachments");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record Scene(string StudentId, Guid InvoiceId);

    /// <summary>
    /// Bitta o'quvchi va bitta ochiq hisob-faktura — naqd to'lovni HAQIQIY
    /// <c>PaymentService</c> orqali qabul qilish uchun. Ma'lumot OWNER
    /// ulanishi bilan tayyorlanadi (<c>app_rw</c> ba'zi jadvallarga yoza olmaydi).
    /// </summary>
    private async Task<Scene> SceneAsync(decimal amount)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string studentId = string.Empty;
        Guid invoiceId = Guid.Empty;

        await fixture.Api.WithDbAsync(async db =>
        {
            var categoryId = await db.FeeCategories.AsNoTracking()
                .Where(c => c.Code == "tuition").Select(c => c.Id).SingleAsync();

            var month = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);

            var student = new Student
            {
                FullName = $"Kassa chiqimi o'quvchisi {suffix}",
                LastName = "Chiqim",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            };
            db.Students.Add(student);

            var invoice = new Invoice
            {
                StudentId = student.Id,
                CategoryId = categoryId,
                PeriodMonth = month,
                Amount = amount,
                Discount = 0m,
                DueOn = month.AddDays(9),
                Status = InvoiceStatus.Open,
                CreatedAt = AppClock.NowInstant,
            };
            db.Invoices.Add(invoice);

            await db.SaveChangesAsync();
            studentId = student.Id;
            invoiceId = invoice.Id;
        });

        return new Scene(studentId, invoiceId);
    }

    private static async Task<CashShiftDto> OpenShiftAsync(HttpClient client, decimal openingFloat)
    {
        var response = await client.PostAsJsonAsync($"{Shifts}/open", new { openingFloat });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
    }

    /// <summary>Yangi kassa ochadi (kassalar modeli, 2026-09) — <c>ManageCashBoxes</c> talab qiladi.</summary>
    private static async Task<CashBoxDto> CreateBoxAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/cash-boxes", new { name = $"Test kassa {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxDto>())!;
    }

    private static async Task<CashShiftDto> CloseShiftAsync(
        HttpClient client, Guid shiftId, decimal countedCash)
    {
        var response = await client.PostAsJsonAsync(
            $"{Shifts}/{shiftId}/close", new { countedCash, note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
    }

    private static async Task<ZReportDto> GetZReportAsync(HttpClient client, Guid shiftId)
    {
        var response = await client.GetAsync($"{Shifts}/{shiftId}/z-report");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ZReportDto>())!;
    }

    private static async Task<ExpenseDto> CreateExpenseAsync(
        HttpClient client, string category, decimal amount, string method)
    {
        var response = await client.PostAsJsonAsync(Expenses, new
        {
            onDate = Day,
            category,
            amount,
            method,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
    }

    private static async Task<ExpenseDto> GetExpenseAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Expenses}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
    }

    private static async Task<CashHandoverDto> RecordHandoverAsync(
        HttpClient client, decimal amount, string destination, string? note)
    {
        var response = await client.PostAsJsonAsync(Handovers, new { amount, destination, note });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashHandoverDto>())!;
    }

    // ---------------------------------------------------------------------
    //  HTTP klienti — ILOVANING O'Z DI grafi bilan
    // ---------------------------------------------------------------------
    //
    //  Bu yerda `WithWebHostBuilder` bilan hech qanday xizmat ULANMAYDI:
    //  test haqiqiy simni tekshirishi kerak. `CashHandoverService` ataylab
    //  DI'ga qo'shilmagan (`Program.cs` bu to'lqinda orkestrator fayli) va
    //  controller uni ro'yxatdan o'tgan bog'liqliklardan yig'adi — agar o'sha
    //  yig'ish buzilsa, bu testlar 500 bilan qizil bo'ladi.

    private async Task<(AppUser User, HttpClient Client)> ClientAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }
}
