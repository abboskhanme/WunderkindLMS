using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Oylik hisoblash (accrual) va jurnal muvozanati (P1-23).
///
/// <para>
/// To'rtta qoida tekshiriladi — ikki toifa → ikkita hisob-faktura; ikki marta
/// yurgizish baribir ikkita; arxivlangan o'quvchiga nol; oy o'rtasida tugagan
/// obuna Q12 qoidasi bo'yicha TO'LIQ oy. Beshinchisi eng qimmati:
/// 200 amallik TASODIFIY ssenariyda jurnal balansda qoladi.
/// </para>
///
/// <para>
/// Har test O'Z BAZASIDA yuradi: hisoblash BUTUN bazani aylanadi, ya'ni
/// "nechta hisob-faktura yozildi" savoli umumiy bazada boshqa testning
/// o'quvchilariga bog'liq bo'lib qolardi (<c>InvoiceServiceTests</c> dagi
/// sabab bilan bir xil).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AccrualTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Migratsiya seed qilgan barqaror toifa id'lari (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid BusCategory = new("00000000-0000-0000-0000-0000000000c2");

    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);

    /// <summary>
    /// Tasodifiy ssenariy URUG'I. Qotirilgan — yiqilgan yurish AYNAN takrorlanadi.
    /// Boshqa urug' bilan qidirish kerak bo'lsa: <c>BILLING_LEDGER_SEED=12345</c>.
    /// Amaldagi qiymat har bir xato xabarida chiqadi.
    /// </summary>
    private static int LedgerSeed =>
        int.TryParse(
            Environment.GetEnvironmentVariable("BILLING_LEDGER_SEED"),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            ? seed
            : 20260912;

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("accrual");

    /// <summary>
    /// Har test O'Z bazasini oladi, ya'ni O'Z ulanish hovuzini ham. Hovuz
    /// tozalanmasa, tugagan testning ulanishlari ochiq qolib, konteynerdagi
    /// <c>max_connections</c> ni yeb qo'yadi va KEYINGI test klasslari
    /// <c>53300</c> bilan yiqiladi. Faqat SHU bazaning hovuzi yopiladi —
    /// ilovaning (ApiFixture) hovuziga tegilmaydi.
    /// </summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private static InvoiceService Invoices(AppDbContext db) => new(db, new LedgerService(db));

    private static PaymentService Payments(AppDbContext db) =>
        new(db, new CashShiftService(db), new LedgerService(db));

    // =================================================================
    //  1. Ikki toifa → ikkita hisob-faktura
    // =================================================================

    /// <summary>
    /// O'qish + avtobus obunasi bo'lgan o'quvchi bir oyga AYNAN IKKITA
    /// hisob-faktura oladi, va har biri O'Z toifasining daromad hisobiga
    /// tushadi (<c>revenue:tuition</c> / <c>revenue:bus</c>) — ikkalasi bitta
    /// "daromad" uyumiga qo'shilib ketmaydi, aks holda toifa bo'yicha hisobot
    /// ma'nosini yo'qotardi.
    /// </summary>
    [Fact]
    public async Task Ikki_toifali_obuna_ikkita_hisob_faktura_va_ikkita_jurnal_jufti_beradi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db, Roles.Admin);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, studentId, BusCategory, 300_000m, ThisMonth, actorId);

        var result = await Invoices(db).AccrueMonthAsync(ThisMonth, actorId);

        Assert.Equal(ThisMonth, result.PeriodMonth);
        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1_300_000m, result.Total);

        await using var check = NewDb();
        var invoices = await check.Invoices.AsNoTracking().ToListAsync();
        Assert.Equal(2, invoices.Count);
        Assert.All(invoices, i => Assert.Equal(studentId, i.StudentId));
        Assert.All(invoices, i => Assert.Equal(ThisMonth, i.PeriodMonth));
        Assert.All(invoices, i => Assert.Equal(InvoiceStatus.Open, i.Status));
        Assert.All(invoices, i => Assert.Equal(0m, i.Discount));
        // Sukut sozlama (billing_seed.sql): to'lov muddati — oyning 10-kuni.
        Assert.All(invoices, i => Assert.Equal(10, i.DueOn.Day));
        Assert.Equal(1_000_000m, invoices.Single(i => i.CategoryId == TuitionCategory).Amount);
        Assert.Equal(300_000m, invoices.Single(i => i.CategoryId == BusCategory).Amount);

        // Har toifa O'Z daromad hisobiga. Daromad — kredit hisob, shuning uchun
        // qoldiq (debet − kredit) MANFIY chiqadi.
        var ledger = new LedgerService(check);
        Assert.Equal(-1_000_000m, await ledger.BalanceAsync(Accounts.RevenueTuition));
        Assert.Equal(-300_000m, await ledger.BalanceAsync(Accounts.RevenueBus));
        Assert.Equal(1_300_000m, await ledger.BalanceAsync(Accounts.Receivable));
        await AssertLedgerBalancedAsync(check);
    }

    // =================================================================
    //  2. Idempotentlik
    // =================================================================

    /// <summary>
    /// Ikkinchi yurish AYNI o'sha ikkita qatorni qoldiradi. Faqat SONNI
    /// sanash yetarli emas: eski qator o'chirilib, yangisi yozilsa ham son
    /// ikkita bo'lib qolardi — shuning uchun <c>id</c> lar solishtiriladi.
    /// </summary>
    [Fact]
    public async Task Ikki_marta_hisoblash_AYNI_ikkita_qatorni_qoldiradi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db, Roles.Admin);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, studentId, BusCategory, 300_000m, ThisMonth, actorId);

        var service = Invoices(db);
        var first = await service.AccrueMonthAsync(ThisMonth, actorId);
        var idsAfterFirst = await InvoiceIdsAsync();

        var second = await service.AccrueMonthAsync(ThisMonth, actorId);
        var idsAfterSecond = await InvoiceIdsAsync();

        Assert.Equal(2, first.Created);
        Assert.Equal(1_300_000m, first.Total);

        Assert.Equal(0, second.Created);
        Assert.Equal(2, second.Skipped);
        Assert.Equal(0m, second.Total);

        Assert.Equal(2, idsAfterFirst.Count);
        Assert.Equal(idsAfterFirst, idsAfterSecond);

        await using var check = NewDb();
        // Ikkinchi yurish jurnalga ham bitta qator qo'shmagan bo'lishi kerak —
        // aks holda qarz ikki marta yozilardi.
        Assert.Equal(4, await check.LedgerEntries.CountAsync());
        Assert.Equal(1_300_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        await AssertLedgerBalancedAsync(check);

        async Task<List<Guid>> InvoiceIdsAsync()
        {
            await using var reader = NewDb();
            return await reader.Invoices.AsNoTracking().Select(i => i.Id).OrderBy(id => id).ToListAsync();
        }
    }

    // =================================================================
    //  3. Arxivlangan o'quvchi
    // =================================================================

    /// <summary>
    /// Arxivlangan o'quvchiga oylik YOZILMAYDI — obunalari qolgan bo'lsa ham.
    /// Faol o'quvchi esa o'sha yurishda o'z qatorini oladi (ya'ni hisoblash
    /// butunlay to'xtab qolmaydi).
    /// </summary>
    [Fact]
    public async Task Arxivlangan_oquvchi_nol_hisob_faktura_oladi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db, Roles.Admin);
        var activeId = await AddStudentAsync(db);
        var archivedId = await AddStudentAsync(db, archived: true);

        await AddSubscriptionAsync(db, activeId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        // Arxivlanganda IKKI toifa bor — "bittasi tushib qoldi" degan holat bo'lmasin.
        await AddSubscriptionAsync(db, archivedId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, archivedId, BusCategory, 300_000m, ThisMonth, actorId);

        var result = await Invoices(db).AccrueMonthAsync(ThisMonth, actorId);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1_000_000m, result.Total);

        await using var check = NewDb();
        Assert.Empty(await check.Invoices.AsNoTracking().Where(i => i.StudentId == archivedId).ToListAsync());
        Assert.Single(await check.Invoices.AsNoTracking().Where(i => i.StudentId == activeId).ToListAsync());

        // Arxivlangan o'quvchining kartochkasida qarz ham, jurnal izi ham yo'q.
        var card = await Invoices(check).ForStudentAsync(archivedId);
        Assert.NotNull(card);
        Assert.Equal(0m, card!.Debt);
        Assert.Empty(card.Invoices);
        Assert.Equal(1_000_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
    }

    // =================================================================
    //  4. Q12 — to'liq bo'lmagan oy ham TO'LIQ oy
    // =================================================================

    /// <summary>
    /// Mijoz javobi Q12 (docs/ASSUMPTIONS.md, 2026-09-11): oy o'rtasida TUGAGAN
    /// obuna ham to'liq oylik summani oladi — kunlar bo'yicha bo'linmaydi.
    /// Test proporsional summani ALOHIDA hisoblab, natija unga TENG EMASLIGINI
    /// ham tekshiradi: kimdir kelajakda proratsiya kiritsa, bu yerda ushlanadi.
    /// </summary>
    [Fact]
    public async Task Oy_ortasida_tugagan_obuna_toliq_oy_hisoblanadi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db, Roles.Admin);
        var studentId = await AddStudentAsync(db);

        // Obuna bu oyning 10-kuni tugaydi (masalan o'quvchi ketdi).
        var endsOn = ThisMonth.AddDays(9);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m,
            ThisMonth.AddMonths(-3), actorId, endsOn);

        var result = await Invoices(db).AccrueMonthAsync(ThisMonth, actorId);

        Assert.Equal(1, result.Created);
        Assert.Equal(1_000_000m, result.Total);

        await using var check = NewDb();
        var invoice = await check.Invoices.AsNoTracking().SingleAsync();
        Assert.Equal(1_000_000m, invoice.Amount);
        Assert.Equal(0m, invoice.Discount);

        var daysInMonth = DateTime.DaysInMonth(ThisMonth.Year, ThisMonth.Month);
        var prorated = decimal.Round(1_000_000m * 10 / daysInMonth, 2);
        Assert.NotEqual(prorated, invoice.Amount);

        // Jurnalga ham to'liq summa tushadi.
        Assert.Equal(1_000_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        await AssertLedgerBalancedAsync(check);
    }

    /// <summary>
    /// Q12 ning ikkinchi yuzi: obuna oy BOSHLANISHIDAN OLDIN tugagan bo'lsa —
    /// hisob-faktura umuman yozilmaydi. "To'liq oy" qoidasi tugagan obunani
    /// abadiy hisoblab turadigan qoidaga aylanib ketmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Oy_boshlanishidan_oldin_tugagan_obunaga_hisob_faktura_yozilmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db, Roles.Admin);
        var leftId = await AddStudentAsync(db);
        var stayedId = await AddStudentAsync(db);

        // O'tgan oyning oxirgi kunida tugagan obuna.
        await AddSubscriptionAsync(db, leftId, TuitionCategory, 1_000_000m,
            ThisMonth.AddMonths(-3), actorId, ThisMonth.AddDays(-1));
        await AddSubscriptionAsync(db, stayedId, TuitionCategory, 1_000_000m,
            ThisMonth.AddMonths(-3), actorId);

        var result = await Invoices(db).AccrueMonthAsync(ThisMonth, actorId);

        Assert.Equal(1, result.Created);

        await using var check = NewDb();
        Assert.Empty(await check.Invoices.AsNoTracking().Where(i => i.StudentId == leftId).ToListAsync());
        Assert.Single(await check.Invoices.AsNoTracking().Where(i => i.StudentId == stayedId).ToListAsync());

        // Ketgan o'quvchining OLDINGI oyi esa hisoblanishi kerak.
        var previous = ThisMonth.AddMonths(-1);
        var earlier = await Invoices(check).AccrueMonthAsync(previous, actorId);
        Assert.Equal(2, earlier.Created);
        await using var after = NewDb();
        Assert.Single(await after.Invoices.AsNoTracking()
            .Where(i => i.StudentId == leftId && i.PeriodMonth == previous).ToListAsync());
    }

    // =================================================================
    //  5. 200 amallik tasodifiy ssenariy — jurnal BALANSDA qoladi
    // =================================================================

    /// <summary>
    /// P1-23 ning eng qimmatli testi: 200 ta tasodifiy amal (oylik hisoblash,
    /// to'lov, storno) ketma-ket bajariladi va oxirida jurnal butunligicha
    /// tekshiriladi.
    ///
    /// <para>Nima isbotlanadi:</para>
    /// <list type="number">
    ///   <item><c>Σ debit == Σ credit</c> — butun jurnal bo'yicha;</item>
    ///   <item>HAR partiya (bitta <c>ref_id</c>) alohida balanslashgan —
    ///   umumiy yig'indi ikkita teskari xatoni bekor qilib yashira olmasin;</item>
    ///   <item><c>receivable</c> qoldig'i o'quvchilar kartochkasidagi
    ///   <c>Σ(qarz − avans)</c> ga TENG — ya'ni jurnal va hisobot bir xil
    ///   haqiqatni aytadi;</item>
    ///   <item><c>cash</c> va <c>bank</c> qoldig'i storno qilinmagan to'lovlar
    ///   yig'indisiga teng (§8.1 Q13: faqat naqd kassaga tushadi).</item>
    /// </list>
    ///
    /// <para>
    /// URUG' qotirilgan (<see cref="LedgerSeed"/>) — yiqilgan yurishni AYNAN
    /// qaytarish mumkin, va urug' har bir xato xabarida chiqadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ikki_yuz_amalli_tasodifiy_ssenariyda_jurnal_balansda_qoladi()
    {
        const int operations = 200;
        const int studentCount = 8;
        const int monthCount = 12;

        var seed = LedgerSeed;
        var rnd = new Random(seed);

        await using var db = NewDb();

        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var cashierA = await AddUserAsync(db, Roles.Cashier);
        var cashierB = await AddUserAsync(db, Roles.Cashier);

        var shifts = new CashShiftService(db);
        await shifts.OpenAsync(cashierA, 0m);
        await shifts.OpenAsync(cashierB, 0m);

        // ---- Sahna: o'quvchilar va obunalar (tiyinli summalar — yaxlitlash ham sinaladi) ----
        var students = new List<string>(studentCount);
        for (var i = 0; i < studentCount; i++)
        {
            var studentId = await AddStudentAsync(db);
            students.Add(studentId);

            await AddSubscriptionAsync(db, studentId, TuitionCategory,
                Money(rnd, 200_000m, 2_000_000m), ThisMonth.AddMonths(-(monthCount - 1)), directorId);

            if (rnd.Next(2) == 0)
                await AddSubscriptionAsync(db, studentId, BusCategory,
                    Money(rnd, 50_000m, 400_000m), ThisMonth.AddMonths(-(monthCount - 1)), directorId);
        }

        var invoiceService = Invoices(db);
        var paymentService = Payments(db);

        var pendingMonths = new Queue<DateOnly>(
            Enumerable.Range(0, monthCount).Select(i => ThisMonth.AddMonths(-(monthCount - 1) + i)));

        // (to'lov id, kassir) — storno uchun ikkinchi shaxs kerak (SPEC §4.5).
        var accepted = new List<(Guid Id, string CashierId)>();
        var reversed = new HashSet<Guid>();
        var trace = new List<string>(operations);

        int accruals = 0, payments = 0, reversals = 0, advances = 0;

        for (var op = 0; op < operations; op++)
        {
            var where = $"urug'={seed}, amal={op}";

            // ---- (a) Oylik hisoblash ----
            if (pendingMonths.Count > 0 && (rnd.NextDouble() < 0.12 || accepted.Count == 0))
            {
                var month = pendingMonths.Dequeue();
                var result = await invoiceService.AccrueMonthAsync(month, directorId);
                accruals++;
                trace.Add($"#{op} accrue {month:yyyy-MM} → {result.Created} ta, {result.Total}");
                continue;
            }

            // ---- (b) Storno ----
            var reversible = accepted.Where(p => !reversed.Contains(p.Id)).ToList();
            if (reversible.Count > 0 && rnd.NextDouble() < 0.10)
            {
                var target = reversible[rnd.Next(reversible.Count)];
                var approverId = target.CashierId == cashierA ? cashierB : cashierA;

                await paymentService.ReverseAsync(target.Id, $"Test storno #{op}", approverId);
                reversed.Add(target.Id);
                reversals++;
                trace.Add($"#{op} reverse {target.Id}");
                continue;
            }

            // ---- (c) To'lov ----
            var studentId = students[rnd.Next(students.Count)];
            // Taklif ochiq oylarni HAMMASINI qaytaradi (puli yetmagani
            // `Suggested = 0` bilan) — shuning uchun 1 so'm ham qoldiqni ko'rsatadi.
            var open = await paymentService.SuggestAllocationAsync(studentId, 1m);
            var totalRemaining = open.Sum(s => s.Remaining);

            // Ba'zan ataylab ortiqcha to'lanadi — taqsimlanmagan avans paydo bo'lsin.
            var amount = totalRemaining <= 0m
                ? Money(rnd, 10_000m, 200_000m)
                : Money(rnd, 10_000m, totalRemaining * 1.2m);

            var suggestion = await paymentService.SuggestAllocationAsync(studentId, amount);
            var lines = suggestion
                .Where(s => s.Suggested > 0m)
                .Select(s => new AllocationRequest(s.InvoiceId, s.Suggested))
                .ToList();

            var allocated = lines.Sum(l => l.Amount);
            Assert.True(allocated <= amount,
                $"Taklif to'lov summasidan oshib ketdi: {allocated} > {amount} ({where}).");
            if (allocated < amount) advances++;

            var method = PaymentMethod.All[rnd.Next(PaymentMethod.All.Count)];
            var cashierId = rnd.Next(2) == 0 ? cashierA : cashierB;

            var payment = await paymentService.AcceptAsync(
                new AcceptPaymentRequest(studentId, amount, method, $"Test #{op}", lines),
                cashierId);

            Assert.Equal(amount - allocated, payment.Unallocated);

            accepted.Add((payment.Id, cashierId));
            payments++;
            trace.Add($"#{op} pay {amount} {method} ({lines.Count} ta taqsimot)");
        }

        var context = $"urug'={seed}; hisoblash={accruals}, to'lov={payments}, "
                      + $"storno={reversals}, avans={advances}; "
                      + $"oxirgi amallar:\n{string.Join("\n", trace.TakeLast(5))}";

        // Ssenariy hech bo'lmaganda har uch turdagi amalni bajargan bo'lsin —
        // aks holda "balans saqlandi" degan xulosa arzon bo'lardi.
        Assert.Equal(operations, accruals + payments + reversals);
        Assert.True(accruals >= 8, $"Hisoblash amallari juda kam: {accruals} ({context}).");
        Assert.True(payments > 100, $"To'lov amallari juda kam: {payments} ({context}).");
        Assert.True(reversals > 0, $"Birorta storno bo'lmadi ({context}).");
        Assert.True(advances > 0, $"Birorta taqsimlanmagan qoldiq bo'lmadi ({context}).");

        // ---- 1. Butun jurnal balansda ----
        await using var check = NewDb();
        var entries = await check.LedgerEntries.AsNoTracking().ToListAsync();
        Assert.NotEmpty(entries);

        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);
        Assert.True(debit == credit,
            $"Jurnal nomutanosib: debet {debit}, kredit {credit} (farq {debit - credit}). {context}");

        // ---- 2. HAR partiya alohida balansda ----
        foreach (var batch in entries.GroupBy(e => e.RefId))
        {
            var d = batch.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
            var c = batch.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);
            Assert.True(d == c,
                $"Partiya {batch.Key} nomutanosib: debet {d}, kredit {c} "
                + $"({batch.Count()} qator). {context}");
        }

        // ---- 3. Har to'lov AYNAN ikki qator (storno bo'lsa — yana ikkita ko'zgu) ----
        foreach (var (paymentId, _) in accepted)
        {
            var rows = entries.Where(e => e.RefId == paymentId).ToList();
            var expected = reversed.Contains(paymentId) ? 4 : 2;
            Assert.True(rows.Count == expected,
                $"To'lov {paymentId} uchun {rows.Count} ta jurnal qatori, kutilgani {expected}. {context}");
        }

        // ---- 4. receivable == Σ(qarz − avans) ----
        //
        // Kutilgan qiymat XOM JADVALLARDAN hisoblanadi (hisobot DTO'sidan emas):
        // jurnal invarianti hisobot qatlamidagi nuqsonga bog'liq bo'lib
        // qolmasin. `StudentBillingDto.Credit` ning o'zi
        // `AllocationTests.Ortiqcha_tolov_taqsimlanmagan_avans_bolib_qoladi`
        // da alohida tekshiriladi.
        var ledger = new LedgerService(check);

        var allPayments = await check.Payments.AsNoTracking().ToListAsync();
        var allAllocations = await check.PaymentAllocations.AsNoTracking().ToListAsync();
        var allInvoices = await check.Invoices.AsNoTracking().ToListAsync();

        // Storno qatorining O'ZI ham, storno qilingan to'lov ham hisobga olinmaydi.
        var stornoOf = allPayments.Where(p => p.ReversalOf is not null)
            .Select(p => p.ReversalOf!.Value).ToHashSet();
        var effective = allPayments
            .Where(p => p.ReversalOf is null && !stornoOf.Contains(p.Id))
            .ToList();
        var effectiveIds = effective.Select(p => p.Id).ToHashSet();

        var allocatedByInvoice = allAllocations
            .Where(a => effectiveIds.Contains(a.PaymentId))
            .GroupBy(a => a.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        var debtTotal = allInvoices
            .Where(i => i.Status != InvoiceStatus.Void)
            .Sum(i => Math.Max(0m, i.Amount - i.Discount - allocatedByInvoice.GetValueOrDefault(i.Id)));

        var advanceTotal = effective.Sum(p =>
            p.Amount - allAllocations.Where(a => a.PaymentId == p.Id).Sum(a => a.Amount));

        var receivable = await ledger.BalanceAsync(Accounts.Receivable);
        Assert.True(receivable == debtTotal - advanceTotal,
            $"receivable qoldig'i ({receivable}) qarz−avans "
            + $"({debtTotal} − {advanceTotal} = {debtTotal - advanceTotal}) bilan mos emas. {context}");
        Assert.True(advanceTotal > 0m,
            $"Ssenariyda taqsimlanmagan avans qolmadi — 4-tasdiq ma'nosini yo'qotdi. {context}");

        // ---- 5. cash / bank — faqat storno qilinmagan to'lovlar ----

        var expectedCash = effective.Where(p => PaymentMethod.CountsAsCash(p.Method)).Sum(p => p.Amount);
        var expectedBank = effective.Where(p => !PaymentMethod.CountsAsCash(p.Method)).Sum(p => p.Amount);

        Assert.True(expectedCash == await ledger.BalanceAsync(Accounts.Cash),
            $"cash qoldig'i {await ledger.BalanceAsync(Accounts.Cash)}, kutilgani {expectedCash}. {context}");
        Assert.True(expectedBank == await ledger.BalanceAsync(Accounts.Bank),
            $"bank qoldig'i {await ledger.BalanceAsync(Accounts.Bank)}, kutilgani {expectedBank}. {context}");

        // ---- 6. Daromad = yozilgan hisob-fakturalarning to'lanadigan summasi ----
        var payable = await check.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Void)
            .SumAsync(i => i.Amount - i.Discount);
        var revenue = -(await ledger.BalanceAsync(Accounts.RevenueTuition)
                        + await ledger.BalanceAsync(Accounts.RevenueBus));
        Assert.True(payable == revenue,
            $"Daromad ({revenue}) yozilgan hisob-fakturalar summasiga ({payable}) teng emas. {context}");
    }

    // =================================================================
    //  Yordamchilar
    // =================================================================

    /// <summary>2 kasrli tasodifiy summa (tiyinlar ham chiqadi — yaxlitlash sinaladi).</summary>
    private static decimal Money(Random rnd, decimal min, decimal max)
    {
        var lo = (long)(min * 100m);
        var hi = (long)(max * 100m);
        if (hi <= lo) hi = lo + 1;
        return new decimal(rnd.NextInt64(lo, hi)) / 100m;
    }

    private static async Task<string> AddUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"Test {role}",
            Role = role,
            Email = $"{role}.{Guid.NewGuid():N}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string> AddStudentAsync(AppDbContext db, bool archived = false)
    {
        var student = new Student
        {
            FullName = "O'quvchi " + Guid.NewGuid().ToString("N")[..8],
            ClassName = "1-A",
            EnrollmentDate = "2026-01-01",
            IsArchived = archived,
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student.Id;
    }

    private static async Task AddSubscriptionAsync(
        AppDbContext db, string studentId, Guid categoryId, decimal monthlyAmount,
        DateOnly startsOn, string createdBy, DateOnly? endsOn = null)
    {
        db.StudentSubscriptions.Add(new StudentSubscription
        {
            StudentId = studentId,
            CategoryId = categoryId,
            MonthlyAmount = monthlyAmount,
            StartsOn = startsOn,
            EndsOn = endsOn,
            CreatedBy = createdBy,
            CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();
    }

    private static async Task AssertLedgerBalancedAsync(AppDbContext db)
    {
        var rows = await db.LedgerEntries.AsNoTracking().ToListAsync();
        var debit = rows.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        var credit = rows.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);

        Assert.True(debit == credit,
            $"Jurnal nomutanosib: debet {debit}, kredit {credit} (farq {debit - credit}), "
            + $"{rows.Count} qator.");
    }
}
