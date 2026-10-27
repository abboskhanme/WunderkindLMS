using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Chegirma turlari, tasdiqsiz biriktirish, kasallik chegirmasi — docs/modules/billing-plans.md §7–§10.
///
/// <para>
/// Each test runs on its OWN database: accrual walks every student of the database, and on the
/// shared one it would write months for the whole suite. The money checks: a catalog discount
/// needs no approver yet reaches accrual; an invoiced month gets the difference through an
/// adjustment that posts debit revenue / credit receivable; sick days use the abonement's lesson
/// days; nothing is ever taken below what is unpaid; adjustments are append-only for the app.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DiscountCatalogTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Katalog_chegirmasi_tasdiqsiz_biriktiriladi_va_hisobda_ayriladi()
    {
        var (db, _) = await NewDbAsync();
        await using var _db = db;
        var (actor, student, tuition) = await SeedAsync(db);
        db.StudentSubscriptions.Add(Subscription(student, tuition.Id, 1_000_000m, new DateOnly(2030, 1, 1), actor));
        await db.SaveChangesAsync();
        var type = await Catalog(db).CreateAsync(new CreateDiscountTypeRequest("Ko'p bolali oila", 0m, 100_000m, tuition.Id), actor);

        // amount only — a percent is refused (client: "foizda emas summada")
        var percent = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Catalog(db).CreateAsync(new CreateDiscountTypeRequest("Foizli", 10m, 0m, null), actor));
        Assert.Equal("invalid_discount", percent.Code);

        var attached = await Catalog(db).AttachAsync(
            new AttachDiscountRequest(student.Id, type.Id, new DateOnly(2030, 1, 1), null), actor);
        await Invoices(db).AccrueMonthAsync(new DateOnly(2030, 1, 1), actor);

        Assert.Equal(DiscountStatus.Approved, attached.Discount.Status);
        Assert.Null(attached.Discount.ApprovedByName);
        Assert.Equal(type.Id, attached.Discount.DiscountTypeId);
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.StudentId == student.Id);
        Assert.Equal(100_000m, invoice.Discount);
        Assert.Equal(1, (await Catalog(db).ListAsync(true)).Single().StudentCount);
    }

    [Fact]
    public async Task Hisoblangan_oyga_biriktirilsa_farqi_tuzatish_bilan_ayriladi()
    {
        var (db, _) = await NewDbAsync();
        await using var _db = db;
        var (actor, student, tuition) = await SeedAsync(db);
        var invoice = await InvoiceAsync(db, student, tuition.Id, new DateOnly(2030, 2, 1), 1_000_000m);
        var type = await Catalog(db).CreateAsync(new CreateDiscountTypeRequest("Xodim farzandi", 0m, 200_000m, null), actor);

        var result = await Catalog(db).AttachAsync(
            new AttachDiscountRequest(student.Id, type.Id, new DateOnly(2030, 2, 1), null), actor);

        Assert.Equal((1, 200_000m), (result.AdjustedInvoices, result.AdjustedTotal));
        await using var check = PostgresFixture.NewContext(_connectionStrings[^1]);
        Assert.Equal(200_000m, (await check.Invoices.SingleAsync(i => i.Id == invoice.Id)).Discount);
        var adjustment = await check.InvoiceAdjustments.SingleAsync(a => a.InvoiceId == invoice.Id);
        Assert.Equal((InvoiceAdjustmentKind.Discount, 200_000m), (adjustment.Kind, adjustment.Amount));
        var lines = await check.LedgerEntries.Where(e => e.RefType == LedgerRefType.InvoiceAdjustment && e.RefId == adjustment.Id).ToListAsync();
        Assert.Contains(lines, l => l.Account == Accounts.RevenueFor(FeeCategoryCode.Tuition) && l.Direction == LedgerDirection.Debit && l.Amount == 200_000m);
        Assert.Contains(lines, l => l.Account == Accounts.Receivable && l.Direction == LedgerDirection.Credit && l.Amount == 200_000m);

        // the same catalog discount twice over the same period → 409
        var dup = await Assert.ThrowsAsync<BillingRuleException>(() => Catalog(db).AttachAsync(
            new AttachDiscountRequest(student.Id, type.Id, new DateOnly(2030, 3, 1), null), actor));
        Assert.Equal("discount_already_attached", dup.Code);
    }

    [Fact]
    public async Task Kasallik_chegirmasi_dars_kunlari_boyicha_va_qoldiqdan_oshmaydi()
    {
        var (db, _) = await NewDbAsync();
        await using var _db = db;
        var (actor, student, tuition) = await SeedAsync(db);
        var bus = await db.FeeCategories.AsNoTracking().FirstAsync(c => c.Code == FeeCategoryCode.Bus);
        var month = new DateOnly(2030, 3, 1);
        var plan = await Plans(db).CreateAsync(new CreateSubscriptionPlanRequest("5-11 sinflar", tuition.Id, 1_000_000m, DaysPerWeek: 5), actor);
        var sub = Subscription(student, tuition.Id, 1_000_000m, month, actor);
        sub.PlanId = plan.Id;
        db.StudentSubscriptions.Add(sub);
        db.StudentSubscriptions.Add(Subscription(student, bus.Id, 310_000m, month, actor)); // no abonement → "Butun oy"
        await db.SaveChangesAsync();
        var tuitionInvoice = await InvoiceAsync(db, student, tuition.Id, month, 1_000_000m);
        var busInvoice = await InvoiceAsync(db, student, bus.Id, month, 310_000m);
        var lessonDays = LessonDays.Count(month, 5, new HashSet<DateOnly>());

        var preview = await Sick(db).PreviewAsync(student.Id, "2030-03", 3);
        var tuitionLine = preview.Lines.Single(l => l.InvoiceId == tuitionInvoice.Id);
        Assert.Equal((lessonDays, decimal.Round(1_000_000m * 3 / lessonDays, 2)), (tuitionLine.LessonDays, tuitionLine.Deduction));
        Assert.Equal("5-11 sinflar", tuitionLine.PlanName);
        var busLine = preview.Lines.Single(l => l.InvoiceId == busInvoice.Id);
        Assert.Equal((31, 30_000m), (busLine.LessonDays, busLine.Deduction)); // 310 000 ÷ 31 × 3
        Assert.Equal(0, await db.InvoiceAdjustments.CountAsync()); // preview writes nothing

        var applied = await Sick(db).ApplyAsync(new SickDiscountRequest(student.Id, "2030-03", 3, "Shifokor ma'lumotnomasi"), actor);
        Assert.Equal(preview.Total, applied.Total);
        await using (var check = PostgresFixture.NewContext(_connectionStrings[^1]))
        {
            Assert.Equal(30_000m, (await check.Invoices.SingleAsync(i => i.Id == busInvoice.Id)).Discount);
            Assert.All(await check.InvoiceAdjustments.ToListAsync(), a => Assert.Equal((InvoiceAdjustmentKind.Sick, (int?)3), (a.Kind, a.SickDays)));
        }

        // more sick days than lesson days = the whole month, never more than is unpaid
        var rest = await Sick(db).ApplyAsync(new SickDiscountRequest(student.Id, "2030-03", 31), actor);
        Assert.Equal(1_310_000m - applied.Total, rest.Total);
        var none = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Sick(db).ApplyAsync(new SickDiscountRequest(student.Id, "2030-03", 1), actor));
        Assert.Equal("nothing_to_deduct", none.Code);
    }

    [Fact]
    public async Task Kasallik_chegirmasi_bekor_qilinsa_qarz_qaytadi_katalog_chegirmasi_qoladi()
    {
        var (db, _) = await NewDbAsync();
        await using var _db = db;
        var (actor, student, tuition) = await SeedAsync(db);
        var month = new DateOnly(2030, 4, 1); // 30 days, no abonement → "Butun oy"
        var invoice = await InvoiceAsync(db, student, tuition.Id, month, 900_000m);
        var applied = await Sick(db).ApplyAsync(new SickDiscountRequest(student.Id, "2030-04", 2), actor);
        Assert.Equal(60_000m, applied.Total); // 900 000 ÷ 30 × 2
        Assert.NotNull(applied.GroupId);

        // a catalog discount attached after it is not swallowed by the sick one
        var type = await Catalog(db).CreateAsync(new CreateDiscountTypeRequest("Aka-uka", 0m, 100_000m, null), actor);
        var attached = await Catalog(db).AttachAsync(new AttachDiscountRequest(student.Id, type.Id, month, null), actor);
        Assert.Equal(100_000m, attached.AdjustedTotal);

        var listed = Assert.Single(await Sick(db).ListAsync(student.Id));
        Assert.Equal((applied.GroupId!.Value, "2030-04", 2, 60_000m), (listed.GroupId, listed.Month, listed.Days, listed.Total));

        var cancelled = await Sick(db).CancelAsync(applied.GroupId.Value, actor); // the same cashier may undo it
        Assert.Equal(60_000m, cancelled.Total);
        await using (var check = PostgresFixture.NewContext(_connectionStrings[^1]))
        {
            Assert.Equal(100_000m, (await check.Invoices.SingleAsync(i => i.Id == invoice.Id)).Discount);
            var reversal = await check.InvoiceAdjustments.SingleAsync(a => a.ReversalOf != null);
            Assert.Equal((InvoiceAdjustmentKind.Sick, 60_000m), (reversal.Kind, reversal.Amount));
            var lines = await check.LedgerEntries.Where(e => e.RefType == LedgerRefType.InvoiceAdjustment && e.RefId == reversal.Id).ToListAsync();
            Assert.Contains(lines, l => l.Account == Accounts.Receivable && l.Direction == LedgerDirection.Debit && l.Amount == 60_000m);
            Assert.Contains(lines, l => l.Account == Accounts.RevenueFor(FeeCategoryCode.Tuition) && l.Direction == LedgerDirection.Credit && l.Amount == 60_000m);
        }
        Assert.Empty(await Sick(db).ListAsync(student.Id));

        var again = await Assert.ThrowsAsync<BillingRuleException>(() => Sick(db).CancelAsync(applied.GroupId.Value, actor));
        Assert.Equal("sick_discount_already_cancelled", again.Code);
        var missing = await Assert.ThrowsAsync<BillingRuleException>(() => Sick(db).CancelAsync(Guid.NewGuid(), actor));
        Assert.Equal("sick_discount_not_found", missing.Code);
    }

    [Fact]
    public async Task Tuzatish_qatorini_ilova_tahrirlay_va_ochira_olmaydi()
    {
        var (db, database) = await NewDbAsync();
        await using var _db = db;
        database.RequireRealAppRw();
        var (actor, student, tuition) = await SeedAsync(db);
        await InvoiceAsync(db, student, tuition.Id, new DateOnly(2030, 4, 1), 500_000m);
        await Sick(db).ApplyAsync(new SickDiscountRequest(student.Id, "2030-04", 2), actor);

        await using var app = new NpgsqlConnection(database.AppRwConnectionString);
        await app.OpenAsync();
        foreach (var sql in new[] { "update invoice_adjustments set amount = 1", "delete from invoice_adjustments" })
        {
            await using var cmd = new NpgsqlCommand(sql, app);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }
    }

    [Fact]
    public async Task Ochirilgan_chegirma_tuzatgan_oy_qarzi_qaytadi()
    {
        var (db, _) = await NewDbAsync();
        await using var _db = db;
        var (actor, student, tuition) = await SeedAsync(db);
        var invoice = await InvoiceAsync(db, student, tuition.Id, new DateOnly(2030, 5, 1), 1_000_000m);
        var type = await Catalog(db).CreateAsync(new CreateDiscountTypeRequest("Xodim farzandi", 0m, 200_000m, null), actor);
        var attached = await Catalog(db).AttachAsync(
            new AttachDiscountRequest(student.Id, type.Id, new DateOnly(2030, 5, 1), null), actor);

        var result = await Catalog(db).DeleteAsync(attached.Discount.Id, actor);

        Assert.Equal((1, 200_000m), (result.RestoredInvoices, result.RestoredTotal));
        await using var check = PostgresFixture.NewContext(_connectionStrings[^1]);
        Assert.Equal(0m, (await check.Invoices.SingleAsync(i => i.Id == invoice.Id)).Discount);
        var reversal = await check.InvoiceAdjustments.SingleAsync(a => a.ReversalOf != null);
        var lines = await check.LedgerEntries.Where(e => e.RefType == LedgerRefType.InvoiceAdjustment && e.RefId == reversal.Id).ToListAsync();
        Assert.Contains(lines, l => l.Account == Accounts.Receivable && l.Direction == LedgerDirection.Debit && l.Amount == 200_000m);
        Assert.Equal(DiscountStatus.Deleted, (await check.Discounts.SingleAsync()).Status);
        Assert.Empty(await new DiscountService(db, Audit(db)).ListAsync(new DiscountQuery(student.Id, null, null)));

        var again = await Assert.ThrowsAsync<BillingRuleException>(() => Catalog(db).DeleteAsync(attached.Discount.Id, actor));
        Assert.Equal("discount_deleted", again.Code);
        // deleted, so the same type can be attached over the same period again
        await Catalog(db).AttachAsync(new AttachDiscountRequest(student.Id, type.Id, new DateOnly(2030, 5, 1), null), actor);
    }

    [Fact]
    public async Task Ochirilgan_chegirma_hisoblashda_yozilgani_qaytadi_va_keyingisi_qollanadi()
    {
        var (db, _) = await NewDbAsync();
        await using var _db = db;
        var (actor, student, tuition) = await SeedAsync(db);
        var month = new DateOnly(2030, 6, 1);
        db.StudentSubscriptions.Add(Subscription(student, tuition.Id, 1_000_000m, month, actor));
        await db.SaveChangesAsync();
        var specific = await Catalog(db).CreateAsync(new CreateDiscountTypeRequest("Yotoqli", 0m, 300_000m, tuition.Id), actor);
        var general = await Catalog(db).CreateAsync(new CreateDiscountTypeRequest("Umumiy", 0m, 100_000m, null), actor);
        var winner = await Catalog(db).AttachAsync(new AttachDiscountRequest(student.Id, specific.Id, month, null), actor);
        await Catalog(db).AttachAsync(new AttachDiscountRequest(student.Id, general.Id, month, null), actor);
        await Invoices(db).AccrueMonthAsync(month, actor);
        // accrual: the category-specific one wins, the two are not summed
        Assert.Equal(300_000m, (await db.Invoices.AsNoTracking().SingleAsync(i => i.StudentId == student.Id)).Discount);

        var result = await Catalog(db).DeleteAsync(winner.Discount.Id, actor);

        // 300 000 back on the debt, the general 100 000 takes over: net +200 000
        Assert.Equal((1, 200_000m), (result.RestoredInvoices, result.RestoredTotal));
        await using var check = PostgresFixture.NewContext(_connectionStrings[^1]);
        Assert.Equal(100_000m, (await check.Invoices.SingleAsync(i => i.StudentId == student.Id)).Discount);
        var undo = await check.InvoiceAdjustments.SingleAsync(a => a.Kind == InvoiceAdjustmentKind.DiscountUndo);
        Assert.Equal((300_000m, winner.Discount.Id), (undo.Amount, undo.DiscountId));
        var lines = await check.LedgerEntries.Where(e => e.RefType == LedgerRefType.InvoiceAdjustment && e.RefId == undo.Id).ToListAsync();
        Assert.Contains(lines, l => l.Account == Accounts.Receivable && l.Direction == LedgerDirection.Debit && l.Amount == 300_000m);
        Assert.Contains(lines, l => l.Account == Accounts.RevenueFor(FeeCategoryCode.Tuition) && l.Direction == LedgerDirection.Credit && l.Amount == 300_000m);
        var takeover = await check.InvoiceAdjustments.SingleAsync(a => a.Kind == InvoiceAdjustmentKind.Discount);
        Assert.Equal(100_000m, takeover.Amount);
    }

    // ---------------------------------------------------------------- yordamchilar

    private static AuditService Audit(AppDbContext db) => new(db, new HttpContextAccessor());
    private static DiscountCatalogService Catalog(AppDbContext db) => new(db, Audit(db));
    private static SubscriptionPlanService Plans(AppDbContext db) => new(db, Audit(db));
    private static SickDiscountService Sick(AppDbContext db) => new(db, Audit(db));
    private static InvoiceService Invoices(AppDbContext db) => new(db, new LedgerService(db));

    private async Task<(AppDbContext Db, TestDatabase Database)> NewDbAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("discount_catalog");
        _connectionStrings.Add(database.OwnerConnectionString);
        return (PostgresFixture.NewContext(database.OwnerConnectionString), database);
    }

    private static async Task<(string Actor, Student Student, FeeCategory Tuition)> SeedAsync(AppDbContext db)
    {
        var user = new AppUser { FullName = "Moliya xodimi", Role = Roles.Admin, Email = $"fin-{Guid.NewGuid():N}", PasswordHash = "x" };
        var schoolClass = new SchoolClass { Name = "7-A", Grade = 7, MonthlyFee = 1_000_000m };
        var student = new Student { FullName = "Chegirma O'quvchi", ClassName = "7-A", EnrollmentDate = "2029-09-01" };
        db.Users.Add(user);
        db.Classes.Add(schoolClass);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var tuition = await db.FeeCategories.AsNoTracking().FirstAsync(c => c.Code == FeeCategoryCode.Tuition);
        return (user.Id, student, tuition);
    }

    private static StudentSubscription Subscription(Student student, Guid categoryId, decimal amount, DateOnly from, string actor) => new()
    {
        StudentId = student.Id, CategoryId = categoryId, MonthlyAmount = amount, StartsOn = from,
        CreatedBy = actor, CreatedAt = AppClock.NowInstant,
    };

    private static async Task<Invoice> InvoiceAsync(AppDbContext db, Student student, Guid categoryId, DateOnly month, decimal amount)
    {
        var invoice = new Invoice
        {
            StudentId = student.Id, CategoryId = categoryId, PeriodMonth = month, Amount = amount,
            DueOn = month.AddDays(9), Status = InvoiceStatus.Open, CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Entry(invoice).State = EntityState.Detached;
        return invoice;
    }
}
