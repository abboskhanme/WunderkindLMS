using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// "Chiqib ketdi — abonementni bekor qilish" (mijoz, 2026-10-06): o'qilgan dars kunlari qoladi,
/// qolgani va keyingi oylar hisobdan chiqadi, ortiqcha to'lov avansga qaytadi, kassaga tegilmaydi.
/// Har test O'Z bazasida (hisob-kitob butun bazani aylanadi — <see cref="AccrualTests"/> bilan bir xil sabab).
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentWithdrawalTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid BusCategory = new("00000000-0000-0000-0000-0000000000c2");

    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);
    private static readonly DateOnly LastMonth = ThisMonth.AddMonths(-1);
    /// <summary>O'tgan oyning 10-kuni — oy o'rtasida ketdi.</summary>
    private static readonly DateOnly LastDay = LastMonth.AddDays(9);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("withdraw");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private static StudentWithdrawalService Withdrawals(AppDbContext db) =>
        new(db, new AuditService(db, new HttpContextAccessor()));

    private static InvoiceService Invoices(AppDbContext db) => new(db, new LedgerService(db));

    // =================================================================

    [Fact]
    public async Task Oy_ortasida_chiqsa_oqilgan_kunlar_qoladi_qolgani_va_keyingi_oy_hisobdan_chiqadi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, TuitionCategory, 1_600_000m, LastMonth);
        await Invoices(db).AccrueMonthAsync(LastMonth, world.ActorId);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var keep = await ExpectedKeepAsync(db, 1_600_000m);

        var preview = await Withdrawals(db).PreviewAsync(world.StudentId, new WithdrawalRequest(LastDay));
        Assert.Equal(2, preview.Lines.Count);
        Assert.Equal(keep, preview.Lines[0].Keep);
        Assert.Equal(3_200_000m - keep, preview.TotalWriteOff);
        await using (var peek = NewDb())
            Assert.Empty(await peek.StudentWithdrawals.ToListAsync());   // preview writes nothing

        var result = await Withdrawals(db).ApplyAsync(world.StudentId, new WithdrawalRequest(LastDay, Note: "Ko'chib ketdi"), world.ActorId);

        await using var check = NewDb();
        var invoices = await check.Invoices.AsNoTracking().Where(i => i.StudentId == world.StudentId)
            .OrderBy(i => i.PeriodMonth).ToListAsync();
        Assert.Equal(keep, invoices[0].Amount - invoices[0].Discount);
        Assert.Equal(0m, invoices[1].Amount - invoices[1].Discount);
        Assert.Equal(-keep, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        Assert.Equal(-keep, result.BalanceAfter);

        var row = await check.StudentSubscriptions.AsNoTracking().SingleAsync(s => s.Id == sub);
        Assert.Equal(LastDay, row.EndsOn);
        var withdrawal = await check.StudentWithdrawals.AsNoTracking().SingleAsync();
        Assert.Equal(3_200_000m - keep, withdrawal.WrittenOff);
        Assert.Equal(0m, withdrawal.Released);
        Assert.Equal(2, await check.InvoiceAdjustments.CountAsync(a => a.GroupId == withdrawal.Id && a.Kind == InvoiceAdjustmentKind.Withdrawal));

        // No cash, no new month: the ledger only moved receivable against revenue.
        Assert.Equal(0m, await new LedgerService(check).BalanceAsync(Accounts.Cash));
        Assert.Equal(keep, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        await AssertLedgerBalancedAsync(check);
        var accrued = await Invoices(check).AccrueStudentAsync(world.StudentId, world.ActorId);
        Assert.Equal(0, accrued.Sum(r => r.Created));
    }

    [Fact]
    public async Task Ortiqcha_tolangan_pul_avansga_qaytadi_va_boshqa_qarzni_yopadi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var tuition = await AddSubscriptionAsync(db, world, TuitionCategory, 1_600_000m, LastMonth);
        await Invoices(db).AccrueMonthAsync(LastMonth, world.ActorId);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var payment = await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 3_200_000m, PaymentMethod.Cash, "Ikki oy", []), world.CashierId);
        // An older unpaid month of another abonement — the freed money must close it first.
        var twoAgo = LastMonth.AddMonths(-1);
        await AddSubscriptionAsync(db, world, BusCategory, 300_000m, twoAgo, twoAgo.AddMonths(1).AddDays(-1));
        await Invoices(db).AccrueMonthAsync(twoAgo, world.ActorId);
        var keep = await ExpectedKeepAsync(db, 1_600_000m);

        var result = await Withdrawals(db).ApplyAsync(
            world.StudentId, new WithdrawalRequest(LastDay, [tuition]), world.ActorId);
        Assert.Equal(3_200_000m - keep, result.TotalReleased);

        await using var check = NewDb();
        var releases = await check.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == payment.Id && a.Kind == PaymentAllocationKind.Release).ToListAsync();
        Assert.Equal(-(3_200_000m - keep), releases.Sum(a => a.Amount));
        Assert.All(releases, a => Assert.NotNull(a.WithdrawalId));

        var bus = await check.Invoices.AsNoTracking().SingleAsync(i => i.StudentId == world.StudentId && i.CategoryId == BusCategory);
        Assert.Equal(InvoiceStatus.Paid, bus.Status);
        var advance = 3_200_000m - keep - 300_000m;
        Assert.Equal(advance, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        Assert.Equal(advance, await new StudentBalanceQuery(check).AdvanceForAsync(world.StudentId));

        // The payment still reads as it was taken (two tuition months) — release rows are not shown;
        // the freed money's new line (the bus month) is an ordinary allocation.
        var dto = (await new PaymentService(check, new LedgerService(check)).GetAsync(payment.Id))!;
        Assert.All(dto.Allocations, a => Assert.True(a.Amount > 0m));
        Assert.Equal(3_200_000m, dto.Allocations.Where(a => a.CategoryId == TuitionCategory).Sum(a => a.Amount));
        Assert.Equal(300_000m, dto.Allocations.Where(a => a.CategoryId == BusCategory).Sum(a => a.Amount));
        Assert.Equal(3_200_000m, await new LedgerService(check).BalanceAsync(Accounts.Cash));
        await AssertLedgerBalancedAsync(check);
    }

    [Fact]
    public async Task Bekor_qilinsa_qarz_va_abonement_qaytadi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, TuitionCategory, 1_600_000m, LastMonth);
        await Invoices(db).AccrueMonthAsync(LastMonth, world.ActorId);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var applied = await Withdrawals(db).ApplyAsync(world.StudentId, new WithdrawalRequest(LastDay), world.ActorId);

        var canceller = await AddUserAsync(db, Roles.Admin);
        await using (var other = NewDb())
        {
            var entry = await Withdrawals(other).CancelAsync(world.StudentId, applied.WithdrawalId!.Value, canceller);
            Assert.NotNull(entry.CancelledAt);
            Assert.False(entry.CanCancel);
        }

        await using var check = NewDb();
        Assert.Equal(-3_200_000m, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        Assert.Null((await check.StudentSubscriptions.AsNoTracking().SingleAsync(s => s.Id == sub)).EndsOn);
        await AssertLedgerBalancedAsync(check);

        var again = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Withdrawals(check).CancelAsync(world.StudentId, applied.WithdrawalId!.Value, canceller));
        Assert.Equal("withdrawal_already_cancelled", again.Code);
    }

    /// <summary>
    /// Kasallik chegirmasi berilgan kunlar ham "o'qilmagan": qoladigan summadan to'liq ayiriladi, proporsional
    /// emas. 20 kun kasal bo'lsa chegirma o'qilgan qismdan katta — oydan hech narsa qolmaydi.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(20)]
    public async Task Kasal_bolgan_kunlari_uchun_haq_olinmaydi(int sickDays)
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        await AddSubscriptionAsync(db, world, TuitionCategory, 1_600_000m, LastMonth);
        await Invoices(db).AccrueMonthAsync(LastMonth, world.ActorId);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var sick = (await new SickDiscountService(db, new AuditService(db, new HttpContextAccessor())).ApplyAsync(
            new SickDiscountRequest(world.StudentId, LastMonth.ToString("yyyy-MM"), sickDays), world.CashierId)).Total;
        Assert.True(sick > 0m);
        var keep = Math.Max(0m, await ExpectedKeepAsync(db, 1_600_000m) - sick);

        var result = await Withdrawals(db).ApplyAsync(world.StudentId, new WithdrawalRequest(LastDay), world.ActorId);

        var leave = result.Lines.Single(l => l.Month == LastMonth.ToString("yyyy-MM"));
        Assert.Equal(sick, leave.SickDeduction);
        Assert.Equal(keep, leave.Keep);
        await using var check = NewDb();
        var invoice = await check.Invoices.AsNoTracking()
            .SingleAsync(i => i.StudentId == world.StudentId && i.PeriodMonth == LastMonth);
        Assert.Equal(keep, invoice.Amount - invoice.Discount);
        Assert.Equal(-keep, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        await AssertLedgerBalancedAsync(check);
    }

    /// <summary>
    /// Bekor qilish — to'liq "undo": qaytarilgan pul o'sha to'lovdan o'sha hisob-fakturaga qaytadi, keyingi
    /// oyga emas (avans qoidasi eng eski oydan boshlardi, bu yerda esa oy allaqachon to'langan edi).
    /// </summary>
    [Fact]
    public async Task Bekor_qilinsa_tolov_oldingi_toifalariga_aynan_qaytadi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        await AddSubscriptionAsync(db, world, TuitionCategory, 1_600_000m, LastMonth);
        await AddSubscriptionAsync(db, world, BusCategory, 300_000m, LastMonth);
        await Invoices(db).AccrueMonthAsync(LastMonth, world.ActorId);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var month = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == world.StudentId && i.PeriodMonth == LastMonth).ToListAsync();
        var tuition = month.Single(i => i.CategoryId == TuitionCategory).Id;
        var bus = month.Single(i => i.CategoryId == BusCategory).Id;
        await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 1_900_000m, PaymentMethod.Cash, null, []), world.CashierId);

        var applied = await Withdrawals(db).ApplyAsync(world.StudentId, new WithdrawalRequest(LastDay), world.ActorId);
        Assert.True(applied.TotalReleased > 0m);
        await using (var other = NewDb())
            await Withdrawals(other).CancelAsync(world.StudentId, applied.WithdrawalId!.Value, world.ActorId);

        await using var check = NewDb();
        var paid = await check.PaymentAllocations.AsNoTracking()
            .Where(a => a.InvoiceId == tuition || a.InvoiceId == bus)
            .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Total = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total);
        Assert.Equal(1_600_000m, paid[tuition]);
        Assert.Equal(300_000m, paid[bus]);
        var statuses = await check.Invoices.AsNoTracking().Where(i => i.Id == tuition || i.Id == bus)
            .ToDictionaryAsync(i => i.Id, i => i.Status);
        Assert.Equal(InvoiceStatus.Paid, statuses[tuition]);
        Assert.Equal(InvoiceStatus.Paid, statuses[bus]);
        Assert.Equal(-1_900_000m, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        await AssertLedgerBalancedAsync(check);
    }

    [Fact]
    public async Task Kelajakdagi_sana_va_abonementsiz_holat_rad_etiladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);

        var future = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Withdrawals(db).ApplyAsync(world.StudentId, new WithdrawalRequest(AppClock.Today.AddDays(1)), world.ActorId));
        Assert.Equal("last_day_in_future", future.Code);

        var none = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Withdrawals(db).ApplyAsync(world.StudentId, new WithdrawalRequest(LastDay), world.ActorId));
        Assert.Equal("nothing_to_withdraw", none.Code);
    }

    [Fact]
    public async Task Faqat_abonementni_boshqaradigan_rol_qila_oladi()
    {
        var student = new Student { FullName = "Chiqish " + Guid.NewGuid().ToString("N")[..6], ClassName = "1-A" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        var url = $"/api/admin/billing/students/{student.Id}/withdrawals/preview";
        var body = new { lastDay = LastDay.ToString("yyyy-MM-dd") };

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync(url, body)).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var ok = await admin.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Empty((await ok.Content.ReadFromJsonAsync<WithdrawalPreviewDto>())!.Lines);
    }

    // =================================================================
    //  Yordamchilar
    // =================================================================

    private sealed record World(string ActorId, string CashierId, string StudentId);

    /// <summary>O'qilgan dars kunlari bo'yicha olib qolinadigan summa — servisdagi formula bilan.</summary>
    private static async Task<decimal> ExpectedKeepAsync(AppDbContext db, decimal payable)
    {
        var holidays = await LessonDays.HolidaysAsync(db, LastMonth, CancellationToken.None);
        var total = LessonDays.CountBetween(LastMonth, ThisMonth.AddDays(-1), LessonDays.SchoolWeek, holidays);
        var studied = LessonDays.CountBetween(LastMonth, LastDay, LessonDays.SchoolWeek, holidays);
        Assert.InRange(studied, 1, total - 1);
        return decimal.Round(payable * studied / total, 2);
    }

    private async Task<World> NewWorldAsync(AppDbContext db)
    {
        var actorId = await AddUserAsync(db, Roles.Admin);
        var cashierId = await AddUserAsync(db, Roles.Cashier);
        var student = new Student
        {
            FullName = "O'quvchi " + Guid.NewGuid().ToString("N")[..8],
            ClassName = "1-A",
            EnrollmentDate = "2026-01-01",
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        await new CashShiftService(db).OpenAsync(cashierId, 0m);
        return new World(actorId, cashierId, student.Id);
    }

    private static async Task<string> AddUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser { FullName = $"Test {role}", Role = role, Email = $"{role}.{Guid.NewGuid():N}" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> AddSubscriptionAsync(
        AppDbContext db, World world, Guid categoryId, decimal amount, DateOnly startsOn, DateOnly? endsOn = null)
    {
        var subscription = new StudentSubscription
        {
            StudentId = world.StudentId,
            CategoryId = categoryId,
            MonthlyAmount = amount,
            StartsOn = startsOn,
            EndsOn = endsOn,
            CreatedBy = world.ActorId,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudentSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    private static async Task AssertLedgerBalancedAsync(AppDbContext db)
    {
        var rows = await db.LedgerEntries.AsNoTracking().ToListAsync();
        var debit = rows.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        var credit = rows.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);
        Assert.True(debit == credit, $"Jurnal nomutanosib: debet {debit}, kredit {credit}.");
    }
}
