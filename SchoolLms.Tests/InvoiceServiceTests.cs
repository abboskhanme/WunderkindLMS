using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// <c>InvoiceService</c> + <c>BillingAccrualService</c> — toifalar kesimidagi
/// oylik hisoblash (P1-09, SPEC §3.7).
///
/// <para>
/// Har test O'Z BAZASIDA yuradi (<c>PostgresFixture.CreateDatabaseAsync</c>,
/// shablondan ~100 ms). Sabab bitta emas, ikkita: (1) hisoblash BUTUN bazani
/// aylanadi — "nechta hisob-faktura yozildi" savoli umumiy bazada boshqa
/// testlarning o'quvchilariga bog'liq bo'lib qolardi; (2) to'lov muddati testi
/// <c>billing_settings</c> ning yagona qatorini O'ZGARTIRADI, va umumiy bazada
/// bu keyingi vazifalarning (P1-10, P1-11) testlarini jimgina buzardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class InvoiceServiceTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Migratsiya seed qilgan barqaror toifa id'lari (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid BusCategory = new("00000000-0000-0000-0000-0000000000c2");

    /// <summary>Joriy oy — testlar sanaga bog'lanib qolmasin (2026-09 deb yozilmagan).</summary>
    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("invoice");

    public Task DisposeAsync() => Task.CompletedTask;

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private static InvoiceService ServiceFor(AppDbContext db) => new(db, new LedgerService(db));

    // =================================================================
    //  Qabul mezoni: ikki toifa → ikkita hisob-faktura + jurnal jufti
    // =================================================================

    [Fact]
    public async Task Ikki_toifali_oquvchi_bir_oyga_aynan_ikkita_hisob_faktura_oladi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, studentId, BusCategory, 300_000m, ThisMonth, actorId);

        var result = await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1_300_000m, result.Total);

        await using var check = NewDb();
        var invoices = await check.Invoices.AsNoTracking()
            .Where(i => i.StudentId == studentId).ToListAsync();
        Assert.Equal(2, invoices.Count);
        Assert.All(invoices, i => Assert.Equal(ThisMonth, i.PeriodMonth));
        Assert.All(invoices, i => Assert.Equal(InvoiceStatus.Open, i.Status));
        Assert.All(invoices, i => Assert.Equal(0m, i.Discount));
        Assert.Equal(1_000_000m, invoices.Single(i => i.CategoryId == TuitionCategory).Amount);
        Assert.Equal(300_000m, invoices.Single(i => i.CategoryId == BusCategory).Amount);
    }

    /// <summary>
    /// P1-09 qabul mezoni: "an invoice with no matching ledger pair is impossible".
    /// Har hisob-faktura AYNAN ikki satr beradi — <c>debit receivable</c> va
    /// <c>credit revenue:&lt;toifa&gt;</c>, summasi to'lanadigan summaga teng.
    /// </summary>
    [Fact]
    public async Task Har_hisob_faktura_debit_receivable_credit_revenue_juftini_yozadi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, studentId, BusCategory, 300_000m, ThisMonth, actorId);

        await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        await using var check = NewDb();
        var invoices = await check.Invoices.AsNoTracking().ToListAsync();
        var entries = await check.LedgerEntries.AsNoTracking().ToListAsync();

        Assert.Equal(4, entries.Count);
        Assert.All(entries, e => Assert.Equal(LedgerRefType.Invoice, e.RefType));
        // Buxgalteriya sanasi — hisoblangan OY, bugungi kun emas.
        Assert.All(entries, e => Assert.Equal(ThisMonth, e.EntryDate));
        Assert.All(entries, e => Assert.Equal(actorId, e.CreatedBy));

        foreach (var invoice in invoices)
        {
            var pair = entries.Where(e => e.RefId == invoice.Id).ToList();
            Assert.Equal(2, pair.Count);

            var debit = Assert.Single(pair, e => e.Direction == LedgerDirection.Debit);
            Assert.Equal(Accounts.Receivable, debit.Account);
            Assert.Equal(invoice.Amount - invoice.Discount, debit.Amount);

            var credit = Assert.Single(pair, e => e.Direction == LedgerDirection.Credit);
            Assert.Equal(
                invoice.CategoryId == TuitionCategory ? Accounts.RevenueTuition : Accounts.RevenueBus,
                credit.Account);
            Assert.Equal(debit.Amount, credit.Amount);
        }

        // Butun jurnal balanslashgan bo'lishi shart.
        Assert.Equal(
            entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount),
            entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount));
    }

    /// <summary>
    /// Teskari tomondan ham tekshiramiz: jurnal yozilmasa, hisob-faktura ham
    /// QOLMAYDI. Ikkalasi bitta tranzaksiyada — aks holda bazada "qarz bor, lekin
    /// jurnalda izi yo'q" qatori paydo bo'lardi va uni keyin hech kim izohlay olmasdi.
    /// </summary>
    [Fact]
    public async Task Jurnal_yiqilsa_hisob_faktura_ham_saqlanmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);

        // Bazada yo'q foydalanuvchi: `ledger_entries.created_by` → `users(id)` FK yiqiladi.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => ServiceFor(db).AccrueMonthAsync(ThisMonth, "yoq-foydalanuvchi"));

        await using var check = NewDb();
        Assert.Equal(0, await check.Invoices.CountAsync());
        Assert.Equal(0, await check.LedgerEntries.CountAsync());
    }

    // =================================================================
    //  Idempotentlik
    // =================================================================

    [Fact]
    public async Task Ikki_marta_hisoblash_qatorlar_sonini_ozgartirmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, studentId, BusCategory, 300_000m, ThisMonth, actorId);

        var service = ServiceFor(db);
        var first = await service.AccrueMonthAsync(ThisMonth, actorId);
        var second = await service.AccrueMonthAsync(ThisMonth, actorId);

        Assert.Equal(2, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(2, second.Skipped);
        Assert.Equal(0m, second.Total);

        await using var check = NewDb();
        Assert.Equal(2, await check.Invoices.CountAsync());
        // Ikkinchi yurish jurnalga ham HECH NARSA qo'shmagan bo'lishi kerak.
        Assert.Equal(4, await check.LedgerEntries.CountAsync());
    }

    /// <summary>
    /// Idempotentlikni xotiradagi "allaqachon" ro'yxati emas, BAZADAGI unikal
    /// indeks kafolatlaydi. Ikkita mustaqil ulanish bir oyni BIR VAQTDA
    /// hisoblaydi: ikkalasi ham bo'sh holatni ko'radi, ya'ni xotiradagi filtr
    /// ikkalasini ham o'tkazib yuboradi. Baribir bitta qator qolishi shart —
    /// yutqazgani 23505 oladi va uni "skipped" deb qabul qiladi.
    /// </summary>
    [Fact]
    public async Task Parallel_ikki_hisoblash_ham_bitta_qator_qoldiradi()
    {
        await using var seed = NewDb();
        var actorId = await AddUserAsync(seed);
        var studentId = await AddStudentAsync(seed);
        await AddSubscriptionAsync(seed, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);

        await using var first = NewDb();
        await using var second = NewDb();

        var results = await Task.WhenAll(
            ServiceFor(first).AccrueMonthAsync(ThisMonth, actorId),
            ServiceFor(second).AccrueMonthAsync(ThisMonth, actorId));

        Assert.Equal(1, results.Sum(r => r.Created));
        Assert.Equal(1, results.Sum(r => r.Skipped));

        await using var check = NewDb();
        Assert.Equal(1, await check.Invoices.CountAsync());
        Assert.Equal(2, await check.LedgerEntries.CountAsync());
    }

    /// <summary>
    /// Xotiradagi filtrni BUTUNLAY chetlab o'tadi va aynan <c>23505</c> yo'lini
    /// tekshiradi: qator boshqa ulanishda TRANZAKSIYA ICHIDA (hali commit
    /// qilinmagan) yoziladi — hisoblash uni ko'ra olmaydi, ya'ni "allaqachon bor"
    /// filtri o'tkazib yuboradi. INSERT unikal indeksda kutib qoladi, blokirovka
    /// commit bilan ochilgach 23505 keladi va u "skipped" deb qabul qilinadi.
    /// Agar idempotentlik faqat xotiradagi ro'yxatga tayangan bo'lsa — bu test
    /// dublikat qator yoki yiqilgan hisoblash bilan tugardi.
    /// </summary>
    [Fact]
    public async Task Xotiradagi_filtr_kormagan_dublikatni_unikal_indeks_tutadi()
    {
        await using var seed = NewDb();
        var actorId = await AddUserAsync(seed);
        var studentId = await AddStudentAsync(seed);
        await AddSubscriptionAsync(seed, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);

        await using var blocker = NewDb();
        await using var blockerTx = await blocker.Database.BeginTransactionAsync();
        blocker.Invoices.Add(new Invoice
        {
            StudentId = studentId,
            CategoryId = TuitionCategory,
            PeriodMonth = ThisMonth,
            Amount = 1_000_000m,
            DueOn = ThisMonth.AddDays(9),
            CreatedAt = AppClock.NowInstant,
        });
        await blocker.SaveChangesAsync();

        await using var db = NewDb();
        var accrual = ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        // Hisoblash shu payt INSERT ustida turibdi (indeks blokirovkasi).
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await blockerTx.CommitAsync();

        var result = await accrual;
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);

        await using var check = NewDb();
        Assert.Equal(1, await check.Invoices.CountAsync());
        // Yutqazgan urinish jurnalga ham hech narsa qoldirmagan bo'lishi kerak.
        Assert.Equal(0, await check.LedgerEntries.CountAsync());
    }

    /// <summary>
    /// Kafolat AYNAN bazada turganini ko'rsatadi: ilovani chetlab o'tib qo'yilgan
    /// ikkinchi qator ham o'tmaydi (<c>unique (student_id, category_id, period_month)</c>).
    /// </summary>
    [Fact]
    public async Task Bir_oyga_ikkinchi_hisob_fakturani_BAZA_rad_etadi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        await using var raw = NewDb();
        raw.Invoices.Add(new Invoice
        {
            StudentId = studentId,
            CategoryId = TuitionCategory,
            PeriodMonth = ThisMonth,
            Amount = 1_000_000m,
            DueOn = ThisMonth.AddDays(9),
            CreatedAt = AppClock.NowInstant,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
        Assert.Contains("23505", ex.InnerException?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    // =================================================================
    //  Kimga hisoblanmaydi / to'liq oy qoidasi
    // =================================================================

    [Fact]
    public async Task Arxivlangan_oquvchiga_hisob_faktura_yozilmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var activeId = await AddStudentAsync(db);
        var archivedId = await AddStudentAsync(db, archived: true);
        await AddSubscriptionAsync(db, activeId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, archivedId, TuitionCategory, 1_000_000m, ThisMonth, actorId);

        var result = await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        Assert.Equal(1, result.Created);

        await using var check = NewDb();
        Assert.False(await check.Invoices.AnyAsync(i => i.StudentId == archivedId));
        Assert.True(await check.Invoices.AnyAsync(i => i.StudentId == activeId));
    }

    /// <summary>
    /// Mijoz javobi Q12 (docs/ASSUMPTIONS.md): to'liq bo'lmagan oy ham TO'LIQ oy.
    /// Oy o'rtasida boshlangan ham, oy o'rtasida tugagan obuna ham to'liq summa oladi —
    /// eski <c>TuitionService</c> xulqi saqlanadi.
    /// </summary>
    [Fact]
    public async Task Oy_ortasida_boshlangan_yoki_tugagan_obuna_toliq_oy_hisoblanadi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var lateId = await AddStudentAsync(db);
        var leavingId = await AddStudentAsync(db);

        await AddSubscriptionAsync(db, lateId, TuitionCategory, 1_000_000m, ThisMonth.AddDays(19), actorId);
        await AddSubscriptionAsync(db, leavingId, TuitionCategory, 1_000_000m,
            ThisMonth.AddMonths(-3), actorId, endsOn: ThisMonth.AddDays(9));

        await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        await using var check = NewDb();
        var invoices = await check.Invoices.AsNoTracking().Where(i => i.PeriodMonth == ThisMonth).ToListAsync();
        Assert.Equal(2, invoices.Count);
        Assert.All(invoices, i => Assert.Equal(1_000_000m, i.Amount));
    }

    // =================================================================
    //  Chegirma — FAQAT tasdiqlangani (mijoz javobi, SPEC §8.1 Q5)
    // =================================================================

    [Fact]
    public async Task Tasdiqlanmagan_chegirma_hisobni_kamaytirmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth.AddMonths(-1), actorId);
        await AddDiscountAsync(db, studentId, categoryId: null, percent: 50m, amount: 0m,
            status: DiscountStatus.Pending, createdBy: actorId, approvedBy: null,
            startsOn: ThisMonth.AddMonths(-1));

        await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        await using var check = NewDb();
        var invoice = await check.Invoices.AsNoTracking().SingleAsync();
        Assert.Equal(0m, invoice.Discount);
        Assert.Equal(1_000_000m, invoice.Amount);

        // Jurnalda ham to'liq summa turishi kerak.
        var credit = await check.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.Direction == LedgerDirection.Credit);
        Assert.Equal(1_000_000m, credit.Amount);
    }

    [Fact]
    public async Task Tasdiqlangan_chegirma_hisobga_va_jurnalga_qollanadi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth.AddMonths(-1), actorId);
        await AddDiscountAsync(db, studentId, categoryId: null, percent: 20m, amount: 50_000m,
            status: DiscountStatus.Approved, createdBy: actorId, approvedBy: directorId,
            startsOn: ThisMonth.AddMonths(-1));

        var result = await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        // Avval foiz (1 000 000 → 800 000), keyin summa (−50 000) = 750 000.
        Assert.Equal(750_000m, result.Total);

        await using var check = NewDb();
        var invoice = await check.Invoices.AsNoTracking().SingleAsync();
        Assert.Equal(1_000_000m, invoice.Amount);
        Assert.Equal(250_000m, invoice.Discount);

        var credit = await check.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.Direction == LedgerDirection.Credit);
        Assert.Equal(750_000m, credit.Amount);
    }

    /// <summary>
    /// Boshqa toifaga berilgan chegirma bu toifaga TEGMAYDI (avtobus chegirmasi
    /// o'qish to'lovini kamaytirmaydi).
    /// </summary>
    [Fact]
    public async Task Toifaga_berilgan_chegirma_boshqa_toifaga_otmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddSubscriptionAsync(db, studentId, BusCategory, 300_000m, ThisMonth, actorId);
        await AddDiscountAsync(db, studentId, categoryId: BusCategory, percent: 100m, amount: 0m,
            status: DiscountStatus.Approved, createdBy: actorId, approvedBy: directorId,
            startsOn: ThisMonth);

        await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        await using var check = NewDb();
        var tuition = await check.Invoices.AsNoTracking().SingleAsync(i => i.CategoryId == TuitionCategory);
        var bus = await check.Invoices.AsNoTracking().SingleAsync(i => i.CategoryId == BusCategory);

        Assert.Equal(0m, tuition.Discount);
        Assert.Equal(300_000m, bus.Discount);
    }

    /// <summary>
    /// 100% chegirma: qator hisobotda ko'rinib tursin deb YOZILADI, lekin ochiq
    /// qarz qoldirmaydi va jurnalga tushmaydi — 0 li yozuvni jurnal qabul qilmaydi
    /// (<c>ck_ledger_entries_amount: amount &gt; 0</c>) va qo'yadigan hech narsa ham yo'q.
    /// </summary>
    [Fact]
    public async Task Toliq_chegirmali_oy_qarz_qoldirmaydi_va_jurnalga_tushmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);
        await AddDiscountAsync(db, studentId, categoryId: null, percent: 100m, amount: 0m,
            status: DiscountStatus.Approved, createdBy: actorId, approvedBy: directorId,
            startsOn: ThisMonth);

        var result = await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);
        Assert.Equal(1, result.Created);
        Assert.Equal(0m, result.Total);

        await using var check = NewDb();
        var invoice = await check.Invoices.AsNoTracking().SingleAsync();
        Assert.Equal(1_000_000m, invoice.Discount);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Empty(await check.LedgerEntries.AsNoTracking().ToListAsync());

        var card = await ServiceFor(check).ForStudentAsync(studentId);
        Assert.NotNull(card);
        Assert.Equal(0m, card!.Debt);
    }

    // =================================================================
    //  To'lov muddati — sozlamadan (mijoz javobi, SPEC §8.1 Q6)
    // =================================================================

    [Fact]
    public async Task Tolov_muddati_billing_settings_dan_olinadi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);

        // Sukut (seed) qiymati — 10-kun.
        await ServiceFor(db).AccrueMonthAsync(ThisMonth, actorId);

        await using var check = NewDb();
        Assert.Equal(10, (await check.Invoices.AsNoTracking().SingleAsync()).DueOn.Day);

        // Admin sozlamani o'zgartiradi — bu UPDATE, migratsiya emas.
        var settings = await check.BillingSettings.SingleAsync();
        settings.PaymentDueDay = 5;
        settings.OverdueAfterDay = 7;
        settings.UpdatedAt = AppClock.NowInstant;
        await check.SaveChangesAsync();

        // Keyingi oy YANGI muddat bilan hisoblanadi.
        var next = ThisMonth.AddMonths(1);
        await ServiceFor(check).AccrueMonthAsync(next, actorId);

        await using var after = NewDb();
        var later = await after.Invoices.AsNoTracking().SingleAsync(i => i.PeriodMonth == next);
        Assert.Equal(5, later.DueOn.Day);
    }

    [Fact]
    public async Task Muddati_otgan_oy_overdue_belgisini_oladi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        var oldMonth = ThisMonth.AddMonths(-2);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, oldMonth, actorId);

        var nextMonth = ThisMonth.AddMonths(1);

        var service = ServiceFor(db);
        await service.AccrueMonthAsync(oldMonth, actorId);
        await service.AccrueMonthAsync(nextMonth, actorId);

        var overdue = await service.ListAsync(new InvoiceQuery(StudentId: studentId, OnlyOverdue: true));

        // Ikki oy oldingi qarz muddati o'tgan; kelasi oyniki esa hech qachon
        // (test oyning qaysi kunida yurishidan qat'i nazar) o'tgan bo'lmaydi.
        Assert.All(overdue, r => Assert.True(r.IsOverdue));
        Assert.DoesNotContain(overdue, r => r.PeriodMonth == nextMonth);

        var row = Assert.Single(overdue, r => r.PeriodMonth == oldMonth);
        Assert.Equal(1_000_000m, row.Payable);
        Assert.Equal(0m, row.Paid);
        Assert.Equal(1_000_000m, row.Remaining);
        Assert.Equal(InvoiceStatus.Open, row.Status);
    }

    // =================================================================
    //  AccrueDue + fon xizmati
    // =================================================================

    [Fact]
    public async Task AccrueDue_joriy_oygacha_toldiradi_va_ikkinchi_yurishda_hech_narsa_yozmaydi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);

        var service = ServiceFor(db);
        var first = await service.AccrueDueAsync(actorId);
        var second = await service.AccrueDueAsync(actorId);

        Assert.Contains(first, r => r.PeriodMonth == ThisMonth);
        // Obuna shu oyda boshlangan — oldingi oylarga hisob-faktura chiqmaydi.
        Assert.Equal(1, first.Sum(r => r.Created));
        Assert.Equal(0, second.Sum(r => r.Created));

        await using var check = NewDb();
        Assert.Equal(1, await check.Invoices.CountAsync());
    }

    /// <summary>
    /// Fon xizmati: bitta yurish hisoblanmagan oylarni to'ldiradi. Jurnal
    /// yozuvining muallifi — bazadagi direktor (tizim foydalanuvchisi yo'q,
    /// <c>ledger_entries.created_by</c> esa <c>users(id)</c> ga FK).
    /// </summary>
    [Fact]
    public async Task Fon_xizmati_bir_yurishda_oylarni_hisoblaydi()
    {
        await using var seed = NewDb();
        var directorId = await AddUserAsync(seed, Roles.SuperAdmin);
        var studentId = await AddStudentAsync(seed);
        await AddSubscriptionAsync(seed, studentId, TuitionCategory, 1_000_000m, ThisMonth, directorId);

        var services = new ServiceCollection();
        var connectionString = _database.OwnerConnectionString;
        services.AddScoped<IAppDbContext>(_ => PostgresFixture.NewContext(connectionString));
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        await using var provider = services.BuildServiceProvider();

        var job = new BillingAccrualService(provider, NullLogger<BillingAccrualService>.Instance);
        await job.RunOnceAsync();
        // Ikkinchi yurish (startup + har 12 soat) hech narsa qo'shmasligi kerak.
        await job.RunOnceAsync();

        await using var check = NewDb();
        var invoice = await check.Invoices.AsNoTracking().SingleAsync();
        Assert.Equal(ThisMonth, invoice.PeriodMonth);
        Assert.Equal(2, await check.LedgerEntries.CountAsync());
        Assert.All(await check.LedgerEntries.AsNoTracking().ToListAsync(),
            e => Assert.Equal(directorId, e.CreatedBy));
    }

    // =================================================================
    //  Bekor qilish (void)
    // =================================================================

    [Fact]
    public async Task Bekor_qilingan_oy_jurnalda_teskari_yoziladi_va_qarzdan_chiqadi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth, actorId);

        var service = ServiceFor(db);
        await service.AccrueMonthAsync(ThisMonth, actorId);
        var invoiceId = (await db.Invoices.AsNoTracking().SingleAsync()).Id;

        // SPEC §4.5: hisoblashni boshlagan odam uni O'ZI bekor qila olmaydi.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.VoidAsync(invoiceId, "xato narx", actorId));

        var voided = await service.VoidAsync(invoiceId, "Narx xato hisoblangan", directorId);
        Assert.Equal(InvoiceStatus.Void, voided.Status);

        await using var check = NewDb();
        // Original TEGILMAGAN, ustiga ko'zgu qatorlar qo'shilgan: qarz nolga qaytadi.
        Assert.Equal(4, await check.LedgerEntries.CountAsync());
        var receivable = await check.LedgerEntries.AsNoTracking()
            .Where(e => e.Account == Accounts.Receivable).ToListAsync();
        Assert.Equal(0m, receivable.Sum(e => e.Direction == LedgerDirection.Debit ? e.Amount : -e.Amount));

        var card = await ServiceFor(check).ForStudentAsync(studentId);
        Assert.NotNull(card);
        Assert.Equal(0m, card!.Debt);
        Assert.Single(card.Invoices);
    }

    // =================================================================
    //  O'quvchi kartochkasi
    // =================================================================

    [Fact]
    public async Task Oquvchi_kartochkasi_qarzni_toifalar_kesimida_beradi()
    {
        await using var db = NewDb();
        var actorId = await AddUserAsync(db);
        var studentId = await AddStudentAsync(db);
        await AddSubscriptionAsync(db, studentId, TuitionCategory, 1_000_000m, ThisMonth.AddMonths(-1), actorId);
        await AddSubscriptionAsync(db, studentId, BusCategory, 300_000m, ThisMonth.AddMonths(-1), actorId);

        var service = ServiceFor(db);
        await service.AccrueMonthAsync(ThisMonth.AddMonths(-1), actorId);
        await service.AccrueMonthAsync(ThisMonth, actorId);

        var card = await service.ForStudentAsync(studentId);

        Assert.NotNull(card);
        Assert.Equal(2 * 1_300_000m, card!.Debt);
        Assert.Equal(0m, card.Credit);
        Assert.Equal(4, card.Invoices.Count);
        Assert.Equal(2, card.Subscriptions.Count);
        Assert.All(card.Subscriptions, s => Assert.True(s.IsActive));
        Assert.Empty(card.Payments);

        // Kassir ekrani: eng eski qarz birinchi.
        var open = await service.OpenForStudentAsync(studentId);
        Assert.Equal(4, open.Count);
        Assert.Equal(ThisMonth.AddMonths(-1), open[0].PeriodMonth);

        Assert.Null(await service.ForStudentAsync("yo-q-oquvchi"));
    }

    // =================================================================
    //  Arifmetika (P1-23 kengaytiradi)
    // =================================================================

    [Fact]
    public void Chegirma_arifmetikasi_eski_TuitionService_bilan_bir_xil()
    {
        // Avval foiz, keyin summa, quyi chegara 0 — eski kodning AYNAN qoidasi.
        Assert.Equal(750_000m, DiscountMath.ChargeFor(1_000_000m, 20m, 50_000m));
        Assert.Equal(250_000m, DiscountMath.DiscountFor(1_000_000m, 20m, 50_000m));
        Assert.Equal(0m, DiscountMath.ChargeFor(1_000_000m, 100m, 0m));
        // 100% dan oshsa ham manfiy chiqmaydi.
        Assert.Equal(0m, DiscountMath.ChargeFor(1_000_000m, 90m, 500_000m));
        Assert.Equal(1_000_000m, DiscountMath.DiscountFor(1_000_000m, 90m, 500_000m));
        // Kasrli foiz (yangi imkoniyat: numeric(5,2)).
        Assert.Equal(875_000m, DiscountMath.ChargeFor(1_000_000m, 12.5m, 0m));

        // Eski TuitionService bilan bir xil kirishlarda bir xil natija.
        foreach (var (fee, pct, amount) in new[]
                 {
                     (1_000_000m, 0, 0m), (1_000_000m, 20, 50_000m),
                     (850_000m, 15, 0m), (0m, 50, 10_000m), (700_000m, 100, 0m),
                 })
        {
            Assert.Equal(
                TuitionService.ChargeFor(fee, pct, amount),
                DiscountMath.ChargeFor(fee, pct, amount));
        }
    }

    [Fact]
    public void Holat_faqat_taqsimotlar_yigindisidan_kelib_chiqadi()
    {
        Assert.Equal(InvoiceStatus.Open, InvoiceService.StatusFor(1_000_000m, 0m));
        Assert.Equal(InvoiceStatus.Partial, InvoiceService.StatusFor(1_000_000m, 400_000m));
        Assert.Equal(InvoiceStatus.Paid, InvoiceService.StatusFor(1_000_000m, 1_000_000m));
        // To'lanadigan narsa bo'lmasa (100% chegirma) — ochiq qarz emas.
        Assert.Equal(InvoiceStatus.Paid, InvoiceService.StatusFor(0m, 0m));
    }

    // =================================================================
    //  Yordamchilar (har test o'z ma'lumotini o'zi yaratadi)
    // =================================================================

    private static async Task<string> AddUserAsync(AppDbContext db, string role = Roles.Admin)
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

    private static async Task AddDiscountAsync(
        AppDbContext db, string studentId, Guid? categoryId, decimal percent, decimal amount,
        string status, string createdBy, string? approvedBy, DateOnly startsOn)
    {
        db.Discounts.Add(new Discount
        {
            StudentId = studentId,
            CategoryId = categoryId,
            Percent = percent,
            Amount = amount,
            Reason = "Test",
            StartsOn = startsOn,
            Status = status,
            CreatedBy = createdBy,
            ApprovedBy = approvedBy,
            DecidedAt = approvedBy is null ? null : AppClock.NowInstant,
            CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();
    }
}
