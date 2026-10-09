using System.Net;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// "Abonementlar analitikasi" (client, 2026-10-09): classes × abonement types for a period, and a
/// class page saying who is on which abonement. An abonement counts when its term meets the period;
/// money is the invoices of the period's months, each tied to the abonement that covers its month.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class SubscriptionAnalyticsTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid Tuition = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Bus = new("00000000-0000-0000-0000-0000000000c2");
    private static readonly DateOnly Sep = new(2026, 9, 1);
    private static readonly DateOnly Oct = new(2026, 10, 1);
    private static readonly DateOnly SepEnd = new(2026, 9, 30);
    private static readonly DateOnly OctEnd = new(2026, 10, 31);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("subanalytics");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Sinflar_abonementlar_kesimida_davr_bilan()
    {
        await using var db = PostgresFixture.NewContext(_database.OwnerConnectionString);
        var w = await SeedAsync(db);
        var query = new SubscriptionAnalyticsQuery(db);

        // ---- September–October -------------------------------------------------------------
        var two = await query.ClassesAsync(Sep, OctEnd);
        Assert.Equal(["5-A", "6-A", "Sinfsiz", "Arxiv (maktabdan ketgan)"], two.Classes.Select(c => c.ClassName));
        Assert.Equal(5, two.Students);              // Ali, Bek, Can, Dil, Nil — not the archived Eva
        Assert.Equal(4, two.WithSubscription);      // all but Can
        Assert.Equal(1, two.WithoutTuition);        // Can
        Assert.Equal(7_200_000m, two.Billed);       // the void invoice is not money
        Assert.Equal(2_000_000m, two.Paid);         // Bek's payment was reversed
        Assert.Equal(5_200_000m, two.Debt);
        Assert.Contains(two.Plans, p => p.PlanId == SubscriptionAnalyticsQuery.NoPlan);   // Can's invoice

        var fiveA = two.Classes.Single(c => c.ClassName == "5-A");
        Assert.Equal((3, 1), (fiveA.Students, fiveA.WithoutTuition));
        var full = Cell(fiveA, w.Full);
        Assert.Equal((2, 2_900_000m, 1_000_000m), (full.Students, full.Billed, full.Paid));   // Ali 1.9M, Bek's Oct 1M
        Assert.Equal((1, 800_000m, 0m), (Cell(fiveA, w.Half).Students, Cell(fiveA, w.Half).Billed, Cell(fiveA, w.Half).Paid));
        Assert.Equal((1, 200_000m), (Cell(fiveA, w.Bus).Students, Cell(fiveA, w.Bus).Billed));
        // Billed with no abonement — and the pupil it was billed to is counted there too.
        Assert.Equal((1, 500_000m), (Cell(fiveA, SubscriptionAnalyticsQuery.NoPlan).Students,
            Cell(fiveA, SubscriptionAnalyticsQuery.NoPlan).Billed));
        Assert.Equal((4_400_000m, 1_000_000m, 3_400_000m), (fiveA.Billed, fiveA.Paid, fiveA.Debt));

        var archived = two.Classes.Single(c => c.ClassId == SubscriptionAnalyticsQuery.Archived);
        Assert.Equal((1, 1_000_000m, 1_000_000m), (archived.Students, archived.Billed, archived.Paid));
        Assert.Equal((1, 800_000m), (two.Classes.Single(c => c.ClassId == SubscriptionAnalyticsQuery.NoClass).Students,
            two.Classes.Single(c => c.ClassId == SubscriptionAnalyticsQuery.NoClass).Billed));

        var fullTotal = two.PlanTotals.Single(t => t.PlanId == w.Full);
        Assert.Equal((4, 4_900_000m, 2_000_000m), (fullTotal.Students, fullTotal.Billed, fullTotal.Paid));   // Ali, Bek, Dil, Eva

        // ---- October only: the September abonements that ended are gone, and so is Eva ----------
        var october = await query.ClassesAsync(Oct, OctEnd);
        Assert.DoesNotContain(october.Classes, c => c.ClassId == SubscriptionAnalyticsQuery.Archived);
        var fiveAOct = october.Classes.Single(c => c.ClassName == "5-A");
        Assert.Equal(2, Cell(fiveAOct, w.Full).Students);
        Assert.Equal(0, Cell(fiveAOct, w.Half).Students);
        Assert.Equal(0, Cell(fiveAOct, w.Bus).Students);
        Assert.Equal(2_400_000m, fiveAOct.Billed);   // Ali 900k after discount, Bek 1M, Can 500k

        // ---- September only: Dil starts on 15 October -------------------------------------------
        var september = await query.ClassesAsync(Sep, SepEnd);
        var sixA = september.Classes.Single(c => c.ClassName == "6-A");
        Assert.Equal((0, 1, 0m), (Cell(sixA, w.Full).Students, sixA.WithoutTuition, sixA.Billed));
    }

    [Fact]
    public async Task Sinf_sahifasi_kim_qaysi_abonementda()
    {
        await using var db = PostgresFixture.NewContext(_database.OwnerConnectionString);
        var w = await SeedAsync(db);
        var query = new SubscriptionAnalyticsQuery(db);

        var detail = await query.ClassAsync(w.FiveA, Sep, OctEnd);
        Assert.NotNull(detail);
        Assert.Equal("5-A", detail.ClassName);
        var students = detail.Students.ToDictionary(s => s.FullName);
        Assert.Equal(["Aliyev Ali", "Berdiyev Bek", "Comilov Can"], detail.Students.Select(s => s.FullName));

        // Bek moved from the half to the full abonement — both are shown, each with its own month.
        var bek = students["Berdiyev Bek"].Subscriptions;
        Assert.Equal([(w.Half, 800_000m), (w.Full, 1_000_000m)], bek.Select(s => (s.PlanId, s.Billed)));
        Assert.Equal((1_800_000m, 0m, 1_800_000m), (students["Berdiyev Bek"].Billed, students["Berdiyev Bek"].Paid,
            students["Berdiyev Bek"].Debt));

        var ali = students["Aliyev Ali"];
        Assert.Equal(2, ali.Subscriptions.Count);
        Assert.Equal(200_000m, ali.Subscriptions.Single(s => s.PlanId == w.Bus).Billed);
        Assert.Equal((1_900_000m, 1_000_000m), (ali.Subscriptions.Single(s => s.PlanId == w.Full).Billed,
            ali.Subscriptions.Single(s => s.PlanId == w.Full).Paid));

        Assert.Empty(students["Comilov Can"].Subscriptions);
        Assert.Equal(500_000m, students["Comilov Can"].Billed);   // an invoice no abonement covers
        Assert.Equal(4_400_000m, detail.Billed);

        // October: Bek is on the full abonement only.
        var oct = await query.ClassAsync(w.FiveA, Oct, OctEnd);
        Assert.Equal([w.Full], oct!.Students.Single(s => s.FullName == "Berdiyev Bek").Subscriptions.Select(s => s.PlanId));

        // The pupils who left: listed only when the period has something of theirs.
        Assert.Equal(["Eshonova Eva"], (await query.ClassAsync(SubscriptionAnalyticsQuery.Archived, Sep, OctEnd))!
            .Students.Select(s => s.FullName));
        Assert.Empty((await query.ClassAsync(SubscriptionAnalyticsQuery.Archived, Oct, OctEnd))!.Students);
        Assert.Equal(["Nurova Nil"], (await query.ClassAsync(SubscriptionAnalyticsQuery.NoClass, Sep, OctEnd))!
            .Students.Select(s => s.FullName));

        Assert.Null(await query.ClassAsync("no-such-class", Sep, OctEnd));
    }

    /// <summary>A card opens exactly the pupils it counts (client: "cardlar ham filter vazifasini bajarsin").</summary>
    [Fact]
    public async Task Karta_bosilganda_aynan_sanalgan_oquvchilar_chiqadi()
    {
        await using var db = PostgresFixture.NewContext(_database.OwnerConnectionString);
        var w = await SeedAsync(db);
        var query = new SubscriptionAnalyticsQuery(db);
        var overview = await query.ClassesAsync(Sep, OctEnd);

        async Task<List<string>> Names(string filter) =>
            [.. (await query.StudentsAsync(Sep, OctEnd, filter)).Students.Select(s => s.FullName)];

        Assert.Equal(overview.Students, (await Names("all")).Count);
        Assert.Equal(overview.WithSubscription, (await Names("subscribed")).Count);
        Assert.Equal(["Comilov Can"], await Names("no-tuition"));
        Assert.Equal(overview.WithoutTuition, (await Names("no-tuition")).Count);

        // Abonement rows open their pupils — the ones who left included.
        Assert.Equal(["Aliyev Ali", "Berdiyev Bek", "Dilova Dil", "Eshonova Eva"], await Names($"plan:{w.Full}"));
        Assert.Equal(["Comilov Can"], await Names($"plan:{SubscriptionAnalyticsQuery.NoPlan}"));
        foreach (var total in overview.PlanTotals)
            Assert.Equal(total.Students, (await Names($"plan:{total.PlanId}")).Count);

        // Debt: largest first; the billed list adds up to the card.
        var debt = await query.StudentsAsync(Sep, OctEnd, "debt");
        Assert.Equal("Berdiyev Bek", debt.Students[0].FullName);   // 1.8M, nothing paid
        Assert.True(debt.Students.Zip(debt.Students.Skip(1)).All(x => x.First.Debt >= x.Second.Debt));
        var billed = await query.StudentsAsync(Sep, OctEnd, "billed");
        Assert.Equal(overview.Billed, billed.Billed);

        var can = (await query.StudentsAsync(Sep, OctEnd, "no-tuition")).Students.Single();
        Assert.Equal(("5-A", true, 500_000m), (can.ClassName, can.InSchool, can.NoPlanBilled));
        var eva = billed.Students.Single(r => r.FullName == "Eshonova Eva");
        Assert.Equal((SubscriptionAnalyticsQuery.Archived, false), (eva.ClassId, eva.InSchool));

        await Assert.ThrowsAsync<BillingRuleException>(() => query.StudentsAsync(Sep, OctEnd, "nonsense"));
    }

    [Fact]
    public void Davr_tekshiruvi()
    {
        Assert.Null(SubscriptionAnalyticsQuery.PeriodError(Sep, OctEnd));
        Assert.NotNull(SubscriptionAnalyticsQuery.PeriodError(OctEnd, Sep));
        Assert.NotNull(SubscriptionAnalyticsQuery.PeriodError(Sep, Sep.AddDays(SubscriptionAnalyticsQuery.MaxDays + 1)));

        var (from, to) = SubscriptionAnalyticsQuery.Period(null, new DateOnly(2026, 10, 9));
        Assert.Equal((Oct, new DateOnly(2026, 10, 9)), (from, to));
    }

    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]   // staff without the finance permission
    public async Task Moliya_rolisiz_kirib_bolmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/finance/subscription-analytics")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/finance/subscription-analytics/class/none")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/finance/subscription-analytics/students?filter=all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/finance/subscription-analytics/export")).StatusCode);
    }

    [Fact]
    public async Task Admin_kiradi_notogri_davr_400()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var ok = await client.GetAsync("/api/admin/finance/subscription-analytics?from=2026-09-01&to=2026-10-31");
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        var cls = await client.GetAsync("/api/admin/finance/subscription-analytics/class/none?from=2026-09-01&to=2026-10-31");
        Assert.True(cls.IsSuccessStatusCode, await cls.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/admin/finance/subscription-analytics?from=2026-10-31&to=2026-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/admin/finance/subscription-analytics?from=2025-01-01&to=2026-10-31")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/admin/finance/subscription-analytics/class/no-such-class")).StatusCode);

        var students = await client.GetAsync("/api/admin/finance/subscription-analytics/students?filter=no-tuition&from=2026-09-01&to=2026-10-31");
        Assert.True(students.IsSuccessStatusCode, await students.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/admin/finance/subscription-analytics/students?filter=nonsense")).StatusCode);

        // Excel: the whole report, one class, one card.
        foreach (var url in new[]
                 {
                     "/api/admin/finance/subscription-analytics/export?from=2026-09-01&to=2026-10-31",
                     "/api/admin/finance/subscription-analytics/export?classId=none&from=2026-09-01&to=2026-10-31",
                     "/api/admin/finance/subscription-analytics/export?filter=debt&from=2026-09-01&to=2026-10-31",
                 })
        {
            var xlsx = await client.GetAsync(url);
            Assert.True(xlsx.IsSuccessStatusCode, url + " " + await xlsx.Content.ReadAsStringAsync());
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                xlsx.Content.Headers.ContentType?.MediaType);
            var bytes = await xlsx.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 100 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K', url);   // a zip
        }
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/admin/finance/subscription-analytics/export?filter=nonsense")).StatusCode);
    }

    [Fact]
    public async Task Excel_hisobot_ekrandagi_raqamlar_bilan()
    {
        await using var db = PostgresFixture.NewContext(_database.OwnerConnectionString);
        await SeedAsync(db);
        var query = new SubscriptionAnalyticsQuery(db);
        var overview = await query.ClassesAsync(Sep, OctEnd);
        var everyone = await query.StudentsAsync(Sep, OctEnd, "everyone");
        Assert.Equal(6, everyone.Students.Count);   // five in school + Eva, who left

        var bytes = SubscriptionAnalyticsExport.Workbook(overview, everyone);
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheets = doc.WorkbookPart!.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
            .Select(x => x.Name!.Value).ToList();
        Assert.Equal(["Sinflar", "Abonement turlari", "O'quvchilar"], sheets);
    }

    // =====================================================================================

    private sealed record World(string FiveA, Guid Full, Guid Half, Guid Bus);

    private static SubscriptionPlanCellDto Cell(SubscriptionClassRowDto row, Guid planId) =>
        row.Plans.Single(p => p.PlanId == planId);

    /// <summary>
    /// 5-A: Ali (full + bus in September), Bek (half in September, full from October), Can (no
    /// abonement, one stray invoice). 6-A: Dil (full from 15 October). No class: Nil (half). Archived:
    /// Eva (full in September). Ali paid 1M for September, Eva paid hers, Bek's payment was reversed.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var admin = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = "a." + Guid.NewGuid().ToString("N")[..8] };
        var cashier = new AppUser { FullName = "Kassir", Role = Roles.Cashier, Email = "c." + Guid.NewGuid().ToString("N")[..8] };
        db.Users.AddRange(admin, cashier);
        var fiveA = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var sixA = new SchoolClass { Name = "6-A", Grade = 6, Language = "uz" };
        db.Classes.AddRange(fiveA, sixA);
        var full = new SubscriptionPlan { Name = "To'liq", CategoryId = Tuition, MonthlyAmount = 1_000_000m, CreatedAt = AppClock.NowInstant };
        var half = new SubscriptionPlan { Name = "Yarim", CategoryId = Tuition, MonthlyAmount = 800_000m, CreatedAt = AppClock.NowInstant };
        var bus = new SubscriptionPlan { Name = "Avtobus", CategoryId = Bus, MonthlyAmount = 200_000m, CreatedAt = AppClock.NowInstant };
        db.SubscriptionPlans.AddRange(full, half, bus);
        var ali = Pupil("Aliyev Ali", "5-A");
        var bek = Pupil("Berdiyev Bek", "5-A");
        var can = Pupil("Comilov Can", "5-A");
        var dil = Pupil("Dilova Dil", "6-A");
        var nil = Pupil("Nurova Nil", "");
        var eva = Pupil("Eshonova Eva", "5-A");   // archived below, once she has paid
        db.Students.AddRange(ali, bek, can, dil, nil, eva);
        await db.SaveChangesAsync();

        void Sub(Student s, SubscriptionPlan p, DateOnly starts, DateOnly? ends) => db.StudentSubscriptions.Add(new StudentSubscription
        {
            StudentId = s.Id, CategoryId = p.CategoryId, PlanId = p.Id, MonthlyAmount = p.MonthlyAmount,
            StartsOn = starts, EndsOn = ends, CreatedBy = admin.Id, CreatedAt = AppClock.NowInstant,
        });
        Sub(ali, full, Sep, null);
        Sub(ali, bus, Sep, SepEnd);
        Sub(bek, half, Sep, SepEnd);
        Sub(bek, full, Oct, null);
        Sub(dil, full, new DateOnly(2026, 10, 15), null);
        Sub(nil, half, Sep, null);
        Sub(eva, full, Sep, SepEnd);

        Invoice Inv(Student s, Guid category, DateOnly month, decimal amount, decimal discount = 0m, string status = InvoiceStatus.Open)
        {
            var i = new Invoice
            {
                StudentId = s.Id, CategoryId = category, PeriodMonth = month, Amount = amount, Discount = discount,
                DueOn = month.AddDays(9), Status = status, CreatedAt = AppClock.NowInstant,
                VoidedAt = status == InvoiceStatus.Void ? AppClock.NowInstant : null,
            };
            db.Invoices.Add(i);
            return i;
        }
        var aliSep = Inv(ali, Tuition, Sep, 1_000_000m);
        Inv(ali, Tuition, Oct, 1_000_000m, discount: 100_000m);
        Inv(ali, Bus, Sep, 200_000m);
        var bekSep = Inv(bek, Tuition, Sep, 800_000m);
        Inv(bek, Tuition, Oct, 1_000_000m);
        Inv(can, Tuition, Oct, 500_000m);
        Inv(dil, Tuition, Oct, 1_000_000m);
        Inv(nil, Tuition, Sep, 800_000m);
        Inv(nil, Tuition, Oct, 800_000m, status: InvoiceStatus.Void);
        var evaSep = Inv(eva, Tuition, Sep, 1_000_000m);
        await db.SaveChangesAsync();

        var shifts = new CashShiftService(db);
        await shifts.OpenAsync(cashier.Id, 0m);
        await shifts.OpenAsync(admin.Id, 0m);
        var ledger = new LedgerService(db);
        var payments = new PaymentService(db, ledger);
        await payments.AcceptAsync(new AcceptPaymentRequest(ali.Id, 1_000_000m, PaymentMethod.Cash, null,
            [new AllocationRequest(aliSep.Id, 1_000_000m)]), cashier.Id);
        await payments.AcceptAsync(new AcceptPaymentRequest(eva.Id, 1_000_000m, PaymentMethod.Cash, null,
            [new AllocationRequest(evaSep.Id, 1_000_000m)]), cashier.Id);
        var bekPaid = await payments.AcceptAsync(new AcceptPaymentRequest(bek.Id, 800_000m, PaymentMethod.Cash, null,
            [new AllocationRequest(bekSep.Id, 800_000m)]), cashier.Id);
        await payments.ReverseAsync(bekPaid.Id, "Boshqa o'quvchiniki edi", admin.Id);
        eva.IsArchived = true;
        eva.ArchivedAt = "2026-10-01";
        await db.SaveChangesAsync();

        return new World(fiveA.Id, full.Id, half.Id, bus.Id);
    }

    private static Student Pupil(string fullName, string className) => new()
    {
        FullName = fullName, LastName = fullName.Split(' ')[0], FirstName = fullName.Split(' ')[1],
        BirthDate = "2015-01-01", Gender = "male", ClassName = className, EnrollmentDate = "2026-09-01",
    };
}
