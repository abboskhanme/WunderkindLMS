using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Abonementlar — docs/modules/billing-plans.md. The money rules: a subscription opened on an
/// abonement takes ITS price and type; a price change reaches everyone on it "keyingi oydan" —
/// a month already invoiced keeps the old price, one not yet invoiced is split at the month end —
/// and written invoices are never touched.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class SubscriptionPlanTests(ApiFixture fixture) : IAsyncLifetime
{
    // The database is shared by the whole suite and some reports cap the student count
    // (MaxArrearsStudents) — everything made here is removed afterwards.
    private readonly List<string> _students = [];
    private readonly List<string> _classes = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewDb();
        var planIds = await db.StudentSubscriptions.Where(s => _students.Contains(s.StudentId) && s.PlanId != null)
            .Select(s => s.PlanId!.Value).Distinct().ToListAsync();
        await db.Invoices.Where(i => _students.Contains(i.StudentId)).ExecuteDeleteAsync();
        await db.StudentSubscriptions.Where(s => _students.Contains(s.StudentId)).ExecuteDeleteAsync();
        await db.Students.Where(s => _students.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Classes.Where(c => _classes.Contains(c.Name)).ExecuteDeleteAsync();
        await db.SubscriptionPlans.Where(p => planIds.Contains(p.Id) || p.Name.Contains(" #t")).ExecuteDeleteAsync();
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    private static SubscriptionService Subscriptions(AppDbContext db) =>
        new(db, new AuditService(db, new HttpContextAccessor()), new InvoiceService(db, new LedgerService(db)));

    private static SubscriptionPlanService Plans(AppDbContext db) => new(db, new AuditService(db, new HttpContextAccessor()));

    private static DateOnly MonthStart => new(AppClock.Today.Year, AppClock.Today.Month, 1);
    private static DateOnly NextMonth => MonthStart.AddMonths(1);

    [Fact]
    public async Task Abonementdagi_obuna_uning_narxi_va_turini_oladi_va_sanaladi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);
        await using var db = NewDb();
        var plan = await Plans(db).CreateAsync(new CreateSubscriptionPlanRequest($"Avtobus {Tag()}", bus.Id, 160_000m), actor);

        // the request's category/amount are ignored — the abonement decides
        var tuition = await CategoryAsync(FeeCategoryCodes.Tuition);
        var sub = await Subscriptions(db).CreateAsync(
            new CreateSubscriptionRequest(student.Id, tuition.Id, 999m, null, MonthStart, null, plan.Id), actor);

        Assert.Equal(160_000m, sub.MonthlyAmount);
        Assert.Equal(bus.Id, sub.CategoryId);
        Assert.Equal(plan.Id, sub.PlanId);
        Assert.Equal(plan.Name, sub.PlanName);
        var listed = (await Plans(db).ListAsync(activeOnly: false)).Single(p => p.Id == plan.Id);
        Assert.Equal(1, listed.StudentCount);
        Assert.Equal(FeeCategoryCodes.Bus, listed.CategoryCode);
        Assert.Contains((await Subscriptions(db).ListAsync(new SubscriptionQuery(PlanId: plan.Id))), s => s.Id == sub.Id);
    }

    [Fact]
    public async Task Arxivdagi_abonementga_obuna_ochilmaydi_va_nom_takrorlanmaydi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);
        await using var db = NewDb();
        var name = $"Avtobus {Tag()}";
        var plan = await Plans(db).CreateAsync(new CreateSubscriptionPlanRequest(name, bus.Id, 160_000m), actor);

        var dup = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Plans(db).CreateAsync(new CreateSubscriptionPlanRequest(name.ToUpperInvariant(), bus.Id, 1m), actor));
        Assert.Equal("plan_name_taken", dup.Code);

        await Plans(db).UpdateAsync(plan.Id, new UpdateSubscriptionPlanRequest(name, 160_000m, IsActive: false), actor);
        var closed = await Assert.ThrowsAsync<BillingRuleException>(() => Subscriptions(db).CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 0m, null, MonthStart, null, plan.Id), actor));
        Assert.Equal("plan_inactive", closed.Code);
    }

    [Fact]
    public async Task Yangi_narx_hammaga_keyingi_oydan_hisob_fakturaga_tegmaydi()
    {
        var actor = await NewUserAsync();
        var invoiced = await NewStudentAsync();   // this month already invoiced → updated in place
        var notYet = await NewStudentAsync();     // active, this month NOT invoiced → split at month end
        var later = await NewStudentAsync();      // starts next month → just the new price
        var tuition = await CategoryAsync(FeeCategoryCodes.Tuition);
        await using var db = NewDb();
        var plan = await Plans(db).CreateAsync(new CreateSubscriptionPlanRequest($"5-11 sinflar {Tag()}", tuition.Id, 1_590_000m), actor);
        var subs = Subscriptions(db);
        // Opening a subscription writes this month at once (SubscriptionService.AccrueNowAsync).
        var a = await subs.CreateAsync(new CreateSubscriptionRequest(invoiced.Id, tuition.Id, 0m, null, MonthStart, null, plan.Id), actor);
        var c = await subs.CreateAsync(new CreateSubscriptionRequest(later.Id, tuition.Id, 0m, null, NextMonth, null, plan.Id), actor);
        var invoice = await db.Invoices.AsNoTracking()
            .SingleAsync(i => i.StudentId == invoiced.Id && i.CategoryId == tuition.Id && i.PeriodMonth == MonthStart);
        // "Active but this month not written yet" — a subscription that reached the table without the
        // service (imported), so the background accrual has not run for it.
        var bRow = new StudentSubscription
        {
            StudentId = notYet.Id, CategoryId = tuition.Id, PlanId = plan.Id, MonthlyAmount = 1_590_000m,
            StartsOn = MonthStart, CreatedBy = actor, CreatedAt = AppClock.NowInstant,
        };
        db.StudentSubscriptions.Add(bRow);
        await db.SaveChangesAsync();
        var b = new { bRow.Id };

        var result = await Plans(db).UpdateAsync(plan.Id, new UpdateSubscriptionPlanRequest(plan.Name, 1_690_000m, true), actor);

        Assert.Equal(3, result.Repriced);
        Assert.Equal(NextMonth, result.EffectiveFrom);
        await using var check = NewDb();
        var rows = await check.StudentSubscriptions.AsNoTracking().Where(s => s.PlanId == plan.Id).ToListAsync();

        var aRow = rows.Single(s => s.Id == a.Id);
        Assert.Equal(1_690_000m, aRow.MonthlyAmount);
        Assert.Null(aRow.EndsOn);

        var bOld = rows.Single(s => s.Id == b.Id);
        Assert.Equal(1_590_000m, bOld.MonthlyAmount);
        Assert.Equal(NextMonth.AddDays(-1), bOld.EndsOn);
        var bNew = rows.Single(s => s.StudentId == notYet.Id && s.Id != b.Id);
        Assert.Equal((NextMonth, (DateOnly?)null, 1_690_000m), (bNew.StartsOn, bNew.EndsOn, bNew.MonthlyAmount));

        Assert.Equal(1_690_000m, rows.Single(s => s.Id == c.Id).MonthlyAmount);
        Assert.Equal(1_590_000m, (await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).Amount);
        Assert.Equal(3, (await Plans(check).ListAsync(false)).Single(p => p.Id == plan.Id).StudentCount);

        // the P&L change journal reads amount changes from the subscription's audit pair
        Assert.True(await check.AuditLogs.AnyAsync(l => l.EntityType == "StudentSubscription" && l.EntityId == a.Id.ToString()
                                                     && l.Action == "update"));
    }

    [Fact]
    public async Task Abonement_obunasining_narxi_qolda_ozgarmaydi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);
        await using var db = NewDb();
        var plan = await Plans(db).CreateAsync(new CreateSubscriptionPlanRequest($"Avtobus {Tag()}", bus.Id, 160_000m), actor);
        var sub = await Subscriptions(db).CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 0m, null, MonthStart, null, plan.Id), actor);

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Subscriptions(db).UpdateAsync(sub.Id, new UpdateSubscriptionRequest(99_000m, null, null), actor));
        Assert.Equal("amount_fixed", ex.Code);
    }

    [Fact]
    public async Task Abonementni_faqat_moliya_xodimi_qoshadi()
    {
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        var body = new { name = $"Avtobus {Tag()}", categoryId = bus.Id, monthlyAmount = 160_000m };

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync("/api/admin/billing/plans", body)).StatusCode);
        var created = await admin.PostAsJsonAsync("/api/admin/billing/plans", body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var list = await admin.GetFromJsonAsync<List<SubscriptionPlanDto>>("/api/admin/billing/plans");
        Assert.Contains(list!, p => p.Name == body.name && p.MonthlyAmount == 160_000m && p.StudentCount == 0);
    }

    // ---------------------------------------------------------------- yordamchilar

    /// <summary>Unique, and marks a test abonement (" #t…") for the clean-up.</summary>
    private static string Tag() => " #t" + Guid.NewGuid().ToString("N")[..8];

    private async Task<string> NewUserAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        return user.Id;
    }

    private async Task<Student> NewStudentAsync()
    {
        var suffix = Tag();
        var schoolClass = new SchoolClass { Name = $"P-{suffix}", Grade = 8, MonthlyFee = 1_500_000m };
        var student = new Student { FullName = $"Abonement o'quvchi {suffix}", ClassName = schoolClass.Name, EnrollmentDate = "2026-09-01" };
        await using var db = NewDb();
        db.Classes.Add(schoolClass);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        _students.Add(student.Id);
        _classes.Add(schoolClass.Name);
        return student;
    }

    private async Task<FeeCategory> CategoryAsync(string code)
    {
        await using var db = NewDb();
        return await db.FeeCategories.AsNoTracking().FirstAsync(c => c.Code == code);
    }
}
