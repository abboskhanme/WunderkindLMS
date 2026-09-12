using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// To'lovni hisob-fakturalarga TAQSIMLASH arifmetikasi (P1-23) — FIFO, aniq
/// summa, ortiqcha to'lov (avans) va kam to'lov (<c>partial</c>).
///
/// <para>
/// <b>Hisob-fakturalar HAQIQIY yo'l bilan tug'iladi</b> — obuna + <c>AccrueMonthAsync</c>
/// (P1-09), qo'lda <c>db.Invoices.Add(...)</c> bilan emas. Sabab: qo'lda qo'yilgan
/// qator jurnalsiz (ledger) bo'lib qolardi va shunda "qarz qancha" degan savolni
/// jurnaldan tekshirib bo'lmasdi — ya'ni test taqsimotni tekshirardi, pulni emas.
/// </para>
///
/// <para>
/// Har test O'Z BAZASIDA yuradi (<c>InvoiceServiceTests</c> dagi sabab): bu yerdagi
/// tasdiqlar jurnal QOLDIG'IGA (<c>receivable</c>, <c>cash</c>) qaraydi, u esa
/// butun bazaga tegishli qiymat. Umumiy bazada boshqa testning to'lovi uni
/// jimgina siljitib yuborardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AllocationTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Migratsiya seed qilgan barqaror toifa id'lari (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid BusCategory = new("00000000-0000-0000-0000-0000000000c2");

    /// <summary>Joriy oyning birinchi kuni — testlar kalendarga bog'lanmasin.</summary>
    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("alloc");

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

    private static PaymentService Payments(AppDbContext db) =>
        new(db, new CashShiftService(db), new LedgerService(db));

    private static InvoiceService Invoices(AppDbContext db) => new(db, new LedgerService(db));

    // =================================================================
    //  FIFO — uchta ochiq hisob-faktura
    // =================================================================

    /// <summary>
    /// P1-23 qabul mezoni: pul ENG ESKI to'lanmagan oydan boshlab taqsimlanadi.
    /// Uch oy (300 000 + 400 000 + 500 000 = 1 200 000) ochiq, kassaga 800 000
    /// keldi: birinchi ikkitasi TO'LIQ yopiladi, uchinchisi qisman.
    /// </summary>
    [Fact]
    public async Task FIFO_uchta_ochiq_oydan_eng_eskisini_birinchi_yopadi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var months = await ThreeOpenMonthsAsync(db, world);

        var payments = Payments(db);

        // ---- 1. Taklif: FIFO tartibi va har qatordagi summa ----
        var suggestion = await payments.SuggestAllocationAsync(world.StudentId, 800_000m);

        Assert.Equal(3, suggestion.Count);
        Assert.Equal(ThisMonth.AddMonths(-2), suggestion[0].PeriodMonth);
        Assert.Equal(ThisMonth.AddMonths(-1), suggestion[1].PeriodMonth);
        Assert.Equal(ThisMonth, suggestion[2].PeriodMonth);
        Assert.Equal(months.Oldest, suggestion[0].InvoiceId);
        Assert.Equal(months.Middle, suggestion[1].InvoiceId);
        Assert.Equal(months.Newest, suggestion[2].InvoiceId);

        Assert.Equal(300_000m, suggestion[0].Remaining);
        Assert.Equal(400_000m, suggestion[1].Remaining);
        Assert.Equal(500_000m, suggestion[2].Remaining);

        Assert.Equal(300_000m, suggestion[0].Suggested);
        Assert.Equal(400_000m, suggestion[1].Suggested);
        Assert.Equal(100_000m, suggestion[2].Suggested);   // qoldiq shu yerda tugadi
        Assert.Equal(800_000m, suggestion.Sum(s => s.Suggested));

        // ---- 2. Taklif bo'yicha to'lovni qabul qilamiz ----
        var payment = await payments.AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 800_000m, PaymentMethod.Cash, "FIFO",
                [.. suggestion.Select(s => new AllocationRequest(s.InvoiceId, s.Suggested))]),
            world.CashierId);

        Assert.Equal(800_000m, payment.Amount);
        Assert.Equal(0m, payment.Unallocated);
        Assert.Equal(3, payment.Allocations.Count);
        Assert.Equal(300_000m, payment.Allocations.Single(a => a.InvoiceId == months.Oldest).Amount);
        Assert.Equal(400_000m, payment.Allocations.Single(a => a.InvoiceId == months.Middle).Amount);
        Assert.Equal(100_000m, payment.Allocations.Single(a => a.InvoiceId == months.Newest).Amount);

        // ---- 3. Holatlar taqsimotdan kelib chiqadi ----
        await using var check = NewDb();
        var card = await Invoices(check).ForStudentAsync(world.StudentId);
        Assert.NotNull(card);

        var oldest = card!.Invoices.Single(i => i.Id == months.Oldest);
        Assert.Equal(InvoiceStatus.Paid, oldest.Status);
        Assert.Equal(300_000m, oldest.Paid);
        Assert.Equal(0m, oldest.Remaining);

        var middle = card.Invoices.Single(i => i.Id == months.Middle);
        Assert.Equal(InvoiceStatus.Paid, middle.Status);
        Assert.Equal(0m, middle.Remaining);

        var newest = card.Invoices.Single(i => i.Id == months.Newest);
        Assert.Equal(InvoiceStatus.Partial, newest.Status);
        Assert.Equal(100_000m, newest.Paid);
        Assert.Equal(400_000m, newest.Remaining);

        Assert.Equal(400_000m, card.Debt);
        Assert.Equal(0m, card.Credit);

        // ---- 4. Jurnal: 1 200 000 qarz yozildi, 800 000 yopildi ----
        await AssertLedgerBalancedAsync(check);
        Assert.Equal(400_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        Assert.Equal(800_000m, await new LedgerService(check).BalanceAsync(Accounts.Cash));
    }

    /// <summary>
    /// ANIQ SUMMA: to'lov qoldiqning tiyinigacha teng bo'lsa — hamma oy
    /// <c>paid</c>, qarz 0, avans 0 va <c>receivable</c> qoldig'i nolga qaytadi.
    /// </summary>
    [Fact]
    public async Task Aniq_summa_hamma_oyni_yopadi_va_avans_qoldirmaydi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var months = await ThreeOpenMonthsAsync(db, world);

        var payments = Payments(db);
        var payment = await payments.AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 1_200_000m, PaymentMethod.Cash, "To'liq yopish",
                [
                    new AllocationRequest(months.Oldest, 300_000m),
                    new AllocationRequest(months.Middle, 400_000m),
                    new AllocationRequest(months.Newest, 500_000m),
                ]),
            world.CashierId);

        Assert.Equal(0m, payment.Unallocated);
        Assert.Equal(1_200_000m, payment.Allocations.Sum(a => a.Amount));

        await using var check = NewDb();
        var card = await Invoices(check).ForStudentAsync(world.StudentId);
        Assert.NotNull(card);
        Assert.All(card!.Invoices, i => Assert.Equal(InvoiceStatus.Paid, i.Status));
        Assert.All(card.Invoices, i => Assert.Equal(0m, i.Remaining));
        Assert.Equal(0m, card.Debt);
        Assert.Equal(0m, card.Credit);

        // Qarzsiz o'quvchida taklif ham bo'sh bo'ladi.
        Assert.Empty(await Payments(check).SuggestAllocationAsync(world.StudentId, 100_000m));

        await AssertLedgerBalancedAsync(check);
        Assert.Equal(0m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
    }

    /// <summary>
    /// ORTIQCHA TO'LOV avansga aylanadi: taqsimlanmagan qoldiq (§8.1 Q15,
    /// docs/ASSUMPTIONS.md) o'quvchi kartochkasida <c>Credit</c> bo'lib turadi,
    /// <c>receivable</c> esa MANFIY qoldiqqa o'tadi (maktab qarzdor).
    ///
    /// <para>
    /// Jurnalga to'lovning TO'LIQ summasi tushadi (800 000), taqsimlangani emas —
    /// aks holda kassadagi naqd bilan jurnal farq qilardi.
    /// </para>
    ///
    /// <para>
    /// <b>DIQQAT — bu test hozir QIZIL va u haqli.</b>
    /// <c>InvoiceService.PaymentsForStudentAsync</c> da <c>ReversedBy</c>
    /// <c>Dictionary&lt;Guid,Guid&gt;.GetValueOrDefault(...)</c> orqali olinadi,
    /// ya'ni storno qilinmagan to'lov uchun <c>null</c> emas,
    /// <c>Guid.Empty</c> qaytadi. Natijada <c>ForStudentAsync</c> dagi
    /// <c>p.ReversedBy is null</c> filtri HAR DOIM yolg'on bo'ladi va
    /// <c>StudentBillingDto.Credit</c> HAR DOIM 0 chiqadi — avans o'quvchi
    /// kartochkasida umuman ko'rinmaydi. Kutilgan qiymat mahsulot kodiga
    /// moslashtirilmadi (P1-23 shartlari): tuzatish <c>qa-debug</c> ishi.
    /// <c>PaymentService.ToDtosAsync</c> da xuddi shu joy TO'G'RI yozilgan
    /// (<c>TryGetValue(...) ? storno : null</c>) — pastdagi birinchi tasdiq
    /// shuni ko'rsatadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ortiqcha_tolov_taqsimlanmagan_avans_bolib_qoladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var invoiceId = await OneMonthAsync(db, world, 500_000m);

        var payment = await Payments(db).AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 800_000m, PaymentMethod.Cash, "Oldindan to'lov",
                [new AllocationRequest(invoiceId, 500_000m)]),
            world.CashierId);

        // To'lov xizmatining o'z javobi to'g'ri: 800 000 − 500 000 = 300 000.
        Assert.Equal(800_000m, payment.Amount);
        Assert.Equal(300_000m, payment.Unallocated);
        Assert.Null(payment.ReversedBy);
        Assert.Equal(500_000m, Assert.Single(payment.Allocations).Amount);

        await using var check = NewDb();
        var card = await Invoices(check).ForStudentAsync(world.StudentId);
        Assert.NotNull(card);
        Assert.Equal(InvoiceStatus.Paid, Assert.Single(card!.Invoices).Status);
        Assert.Equal(0m, card.Debt);

        // Jurnal avansni KO'RADI: 500 000 qarz yozildi, 800 000 kelib tushdi.
        await AssertLedgerBalancedAsync(check);
        Assert.Equal(-300_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        Assert.Equal(800_000m, await new LedgerService(check).BalanceAsync(Accounts.Cash));

        // Kartochka ham SHU raqamni ko'rsatishi kerak. Ildiz sabab avval
        // tekshiriladi — xato xabari "Credit 0 chiqdi" emas, "ReversedBy
        // Guid.Empty bo'lib qolgan" deb ko'rsatsin.
        var cardPayment = Assert.Single(card.Payments);
        Assert.Equal(300_000m, cardPayment.Unallocated);
        Assert.Null(cardPayment.ReversedBy);
        Assert.Equal(300_000m, card.Credit);
    }

    /// <summary>
    /// KAM TO'LOV: hisob-faktura <c>partial</c> bo'ladi (na <c>open</c>, na
    /// <c>paid</c>) va qoldiq aniq ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Kam_tolov_hisob_fakturani_partial_qiladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var invoiceId = await OneMonthAsync(db, world, 500_000m);

        var payment = await Payments(db).AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 200_000m, PaymentMethod.Cash, "Qisman",
                [new AllocationRequest(invoiceId, 200_000m)]),
            world.CashierId);

        Assert.Equal(0m, payment.Unallocated);

        await using var check = NewDb();
        var invoice = Assert.Single((await Invoices(check).ForStudentAsync(world.StudentId))!.Invoices);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        Assert.Equal(500_000m, invoice.Payable);
        Assert.Equal(200_000m, invoice.Paid);
        Assert.Equal(300_000m, invoice.Remaining);

        // Bazadagi ustun ham shu — DTO hisoblab qo'ygan qiymat emas.
        Assert.Equal(InvoiceStatus.Partial,
            (await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId)).Status);

        await AssertLedgerBalancedAsync(check);
        Assert.Equal(300_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
    }

    /// <summary>
    /// FIFO qisman to'langan oydan DAVOM ETADI: keyingi to'lov taklifi
    /// o'sha oyning QOLDIG'INI birinchi qatorga qo'yadi, boshidan emas.
    /// </summary>
    [Fact]
    public async Task Keyingi_taklif_qisman_tolangan_oydan_davom_etadi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var previous = ThisMonth.AddMonths(-1);
        var current = ThisMonth;

        await AddSubscriptionAsync(db, world, TuitionCategory, 500_000m,
            previous, previous.AddMonths(1).AddDays(-1));
        await AddSubscriptionAsync(db, world, TuitionCategory, 400_000m, current);

        var payments = Payments(db);
        var invoiceService = Invoices(db);
        await invoiceService.AccrueMonthAsync(previous, world.ActorId);
        await invoiceService.AccrueMonthAsync(current, world.ActorId);

        var olderInvoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.PeriodMonth == previous);
        var newerInvoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.PeriodMonth == current);

        await payments.AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 200_000m, PaymentMethod.Cash, null,
                [new AllocationRequest(olderInvoice.Id, 200_000m)]),
            world.CashierId);

        var suggestion = await payments.SuggestAllocationAsync(world.StudentId, 400_000m);

        Assert.Equal(2, suggestion.Count);
        Assert.Equal(olderInvoice.Id, suggestion[0].InvoiceId);
        Assert.Equal(300_000m, suggestion[0].Remaining);
        Assert.Equal(300_000m, suggestion[0].Suggested);
        Assert.Equal(newerInvoice.Id, suggestion[1].InvoiceId);
        Assert.Equal(400_000m, suggestion[1].Remaining);
        Assert.Equal(100_000m, suggestion[1].Suggested);

        await payments.AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 400_000m, PaymentMethod.Cash, null,
                [.. suggestion.Select(s => new AllocationRequest(s.InvoiceId, s.Suggested))]),
            world.CashierId);

        await using var check = NewDb();
        var card = await Invoices(check).ForStudentAsync(world.StudentId);
        Assert.Equal(InvoiceStatus.Paid, card!.Invoices.Single(i => i.Id == olderInvoice.Id).Status);
        Assert.Equal(InvoiceStatus.Partial, card.Invoices.Single(i => i.Id == newerInvoice.Id).Status);
        Assert.Equal(300_000m, card.Debt);
        Assert.Equal(0m, card.Credit);

        await AssertLedgerBalancedAsync(check);
        Assert.Equal(300_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
    }

    // =================================================================
    //  Chegaralar — pul BOSHQA o'quvchiga o'tib ketmasin
    // =================================================================

    /// <summary>
    /// IZOLYATSIYA: bir o'quvchining puli BOSHQASINING qarzini yopa olmaydi
    /// (§8.1 Q14 — bitta to'lov, bitta o'quvchi). Rad etilishi kifoya emas:
    /// bazada hech qanday iz qolmasligi ham tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Boshqa_oquvchining_hisob_fakturasiga_pul_taqsimlanmaydi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var stranger = await AddStudentAsync(db);

        var mine = await OneMonthAsync(db, world, 500_000m);
        await AddSubscriptionAsync(db, world with { StudentId = stranger }, BusCategory, 300_000m, ThisMonth);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var theirs = await db.Invoices.AsNoTracking().SingleAsync(i => i.StudentId == stranger);

        var ledgerBefore = await db.LedgerEntries.CountAsync();

        var ex = await Assert.ThrowsAsync<PaymentException>(() => Payments(db).AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 300_000m, PaymentMethod.Cash, null,
                [new AllocationRequest(theirs.Id, 300_000m)]),
            world.CashierId));

        Assert.Equal("invoice_other_student", ex.Code);
        Assert.Equal(PaymentError.Invalid, ex.Error);

        await using var check = NewDb();
        Assert.Empty(await check.Payments.AsNoTracking().ToListAsync());
        Assert.Empty(await check.PaymentAllocations.AsNoTracking().ToListAsync());
        Assert.Equal(ledgerBefore, await check.LedgerEntries.CountAsync());

        // O'zganing oyi tegilmagan; o'zimizniki ham ochiq qolgan.
        Assert.Equal(InvoiceStatus.Open,
            (await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == theirs.Id)).Status);
        Assert.Equal(InvoiceStatus.Open,
            (await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == mine)).Status);
    }

    /// <summary>
    /// Bitta hisob-faktura taqsimotda IKKI marta kelsa — rad. Aks holda
    /// "qoldiqdan oshmasin" tekshiruvi har qatorni alohida ko'rib, jami esa
    /// qoldiqdan oshib ketardi.
    /// </summary>
    [Fact]
    public async Task Takrorlangan_hisob_faktura_qatori_rad_etiladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var invoiceId = await OneMonthAsync(db, world, 500_000m);

        var ex = await Assert.ThrowsAsync<PaymentException>(() => Payments(db).AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 500_000m, PaymentMethod.Cash, null,
                [
                    new AllocationRequest(invoiceId, 250_000m),
                    new AllocationRequest(invoiceId, 250_000m),
                ]),
            world.CashierId));

        Assert.Equal("duplicate_invoice", ex.Code);

        await using var check = NewDb();
        Assert.Empty(await check.Payments.AsNoTracking().ToListAsync());
        Assert.Equal(InvoiceStatus.Open,
            (await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId)).Status);
    }

    /// <summary>
    /// Nol yoki manfiy summa — na to'lovda, na taqsimot qatorida. "0 so'mlik
    /// to'lov" chek raqamini yeb, hisobotda ma'nosiz qator qoldirardi.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Nol_yoki_manfiy_taqsimot_rad_etiladi(int lineAmount)
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var invoiceId = await OneMonthAsync(db, world, 500_000m);

        var ex = await Assert.ThrowsAsync<PaymentException>(() => Payments(db).AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 500_000m, PaymentMethod.Cash, null,
                [new AllocationRequest(invoiceId, lineAmount)]),
            world.CashierId));

        Assert.Equal("invalid_allocation_amount", ex.Code);

        var zero = await Assert.ThrowsAsync<PaymentException>(() => Payments(db).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 0m, PaymentMethod.Cash, null, []),
            world.CashierId));
        Assert.Equal("invalid_amount", zero.Code);

        await using var check = NewDb();
        Assert.Empty(await check.Payments.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// Bekor qilingan (<c>void</c>) oyga pul taqsimlab bo'lmaydi: uning qarzi
    /// jurnalda allaqachon teskari yozilgan, ya'ni to'lov <c>receivable</c> ni
    /// ikki marta kamaytirardi.
    /// </summary>
    [Fact]
    public async Task Bekor_qilingan_oyga_pul_taqsimlanmaydi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var invoiceId = await OneMonthAsync(db, world, 500_000m);

        // Bekor qilishni BOSHQA shaxs qiladi (SPEC §4.5).
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var voided = await Invoices(db).VoidAsync(invoiceId, "Narx xato hisoblangan", directorId);
        Assert.Equal(InvoiceStatus.Void, voided.Status);

        var ex = await Assert.ThrowsAsync<PaymentException>(() => Payments(db).AcceptAsync(
            new AcceptPaymentRequest(
                world.StudentId, 500_000m, PaymentMethod.Cash, null,
                [new AllocationRequest(invoiceId, 500_000m)]),
            world.CashierId));

        Assert.Equal("invoice_void", ex.Code);

        await using var check = NewDb();
        Assert.Empty(await check.PaymentAllocations.AsNoTracking().ToListAsync());
        // Bekor qilingan oy qarzga kirmaydi va taklifda ham ko'rinmaydi.
        var card = await Invoices(check).ForStudentAsync(world.StudentId);
        Assert.Equal(0m, card!.Debt);
        Assert.Empty(await Payments(check).SuggestAllocationAsync(world.StudentId, 500_000m));
        await AssertLedgerBalancedAsync(check);
    }

    // =================================================================
    //  Yordamchilar — har test o'z ma'lumotini o'zi yaratadi
    // =================================================================

    /// <summary>Bitta test sahnasi: hisoblovchi admin, kassir + ochiq smena, o'quvchi.</summary>
    private sealed record World(string ActorId, string CashierId, string StudentId);

    private sealed record OpenMonths(Guid Oldest, Guid Middle, Guid Newest);

    private async Task<World> NewWorldAsync(AppDbContext db)
    {
        var actorId = await AddUserAsync(db, Roles.Admin);
        var cashierId = await AddUserAsync(db, Roles.Cashier);
        var studentId = await AddStudentAsync(db);

        // Smena HAQIQIY xizmat orqali ochiladi (P1-10) — to'lov uni serverdan topadi.
        await new CashShiftService(db).OpenAsync(cashierId, 0m);

        return new World(actorId, cashierId, studentId);
    }

    /// <summary>
    /// Uchta ketma-ket oy, uchta HAR XIL summa (300k / 400k / 500k). Narx
    /// oyma-oy o'zgargan holat: har oy uchun alohida obuna, davrlari
    /// kesishmaydi — accrual har oyga o'sha oyning narxini oladi.
    /// </summary>
    private async Task<OpenMonths> ThreeOpenMonthsAsync(AppDbContext db, World world)
    {
        var m2 = ThisMonth.AddMonths(-2);
        var m1 = ThisMonth.AddMonths(-1);

        await AddSubscriptionAsync(db, world, TuitionCategory, 300_000m, m2, m2.AddMonths(1).AddDays(-1));
        await AddSubscriptionAsync(db, world, TuitionCategory, 400_000m, m1, m1.AddMonths(1).AddDays(-1));
        await AddSubscriptionAsync(db, world, TuitionCategory, 500_000m, ThisMonth);

        var service = Invoices(db);
        await service.AccrueMonthAsync(m2, world.ActorId);
        await service.AccrueMonthAsync(m1, world.ActorId);
        await service.AccrueMonthAsync(ThisMonth, world.ActorId);

        var rows = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == world.StudentId)
            .OrderBy(i => i.PeriodMonth)
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 300_000m, 400_000m, 500_000m }, rows.Select(r => r.Amount).ToArray());

        return new OpenMonths(rows[0].Id, rows[1].Id, rows[2].Id);
    }

    /// <summary>Joriy oy uchun bitta ochiq hisob-faktura.</summary>
    private async Task<Guid> OneMonthAsync(AppDbContext db, World world, decimal amount)
    {
        await AddSubscriptionAsync(db, world, TuitionCategory, amount, ThisMonth);
        var result = await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        Assert.Equal(1, result.Created);

        return (await db.Invoices.AsNoTracking()
            .SingleAsync(i => i.StudentId == world.StudentId && i.PeriodMonth == ThisMonth)).Id;
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

    private static async Task<string> AddStudentAsync(AppDbContext db)
    {
        var student = new Student
        {
            FullName = "O'quvchi " + Guid.NewGuid().ToString("N")[..8],
            ClassName = "1-A",
            EnrollmentDate = "2026-01-01",
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student.Id;
    }

    private static async Task<Guid> AddSubscriptionAsync(
        AppDbContext db, World world, Guid categoryId, decimal monthlyAmount,
        DateOnly startsOn, DateOnly? endsOn = null)
    {
        var subscription = new StudentSubscription
        {
            StudentId = world.StudentId,
            CategoryId = categoryId,
            MonthlyAmount = monthlyAmount,
            StartsOn = startsOn,
            EndsOn = endsOn,
            CreatedBy = world.ActorId,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudentSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    /// <summary>
    /// Butun jurnal balansda: <c>Σ debit == Σ credit</c>. Har testda
    /// tekshiriladi — taqsimot mantiqi jurnalni nomutanosib qoldira olmasin.
    /// </summary>
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
