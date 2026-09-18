using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// F6.03 — o'zgarishlar jurnali (<see cref="FinanceReportQueries.RevenueExpectationChangesAsync"/>,
/// <c>FinanceReportQueries.RevenueExpectationChanges.cs</c>).
///
/// <para>
/// <b>Qabul mezoni — reconciliation.</b> <c>studentJoined</c>/<c>studentLeft</c>
/// qatorlaridagi DISTINCT o'quvchi soni <see cref="FinanceReportQueries.RevenueExpectationAsync"/>
/// ning <c>StudentsAdmitted</c>/<c>StudentsDeparted</c> bilan AYNAN teng bo'lishi
/// SHART — <see cref="Studentjoined_left_StudentsAdmitted_departed_bilan_kelishadi"/>
/// buni tekshiradi (vazifa: "figures that also appear elsewhere must reconcile").
/// </para>
/// <para>
/// Testlar haqiqiy servis qatlamini ishlatadi (<c>SubscriptionService</c>,
/// <c>DiscountService</c>) — qo'lda <c>AuditLog</c> qatori yasalmaydi: shunda
/// Before/After JSON shakli SERVIS o'zi ishlab chiqaradigan, HAQIQIY shaklda
/// bo'ladi (ikkinchi ta'rif emas).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RevenueExpectationChangesTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    private const string ChangesUrl = "/api/admin/finance/pnl/expectation/changes";

    // =====================================================================
    //  1. RUXSAT — bir xil darvoza, RevenueExpectationTests bilan bir xil sabab
    // =====================================================================

    [Fact]
    public async Task Kassir_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);
        var response = await client.GetAsync(ChangesUrl);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();
        var response = await client.GetAsync(ChangesUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_kira_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var response = await client.GetAsync($"{ChangesUrl}?month=2026-01");
        Assert.True(response.IsSuccessStatusCode,
            $"{ChangesUrl} → {(int)response.StatusCode} {response.StatusCode}");
    }

    [Fact]
    public async Task Notogri_tur_400()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await client.GetAsync($"{ChangesUrl}?month=2026-01&kind=notAThing");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =====================================================================
    //  2. RECONCILIATION — studentJoined/studentLeft = RevenueExpectationAsync
    // =====================================================================

    [Fact]
    public async Task Studentjoined_left_StudentsAdmitted_departed_bilan_kelishadi()
    {
        await using var db = await NewDbAsync("joinleft");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");

        var june = new DateOnly(2020, 6, 1);

        db.Students.AddRange(
            NewStudent("stu-a", "Kelgan A", "6-A"),
            NewStudent("stu-b", "Kelgan B", "6-A"),
            NewStudent("stu-c", "Ketgan C", "6-B"));
        await db.SaveChangesAsync();

        var subs = Subscriptions(db, actorId);
        await subs.CreateAsync(
            new CreateSubscriptionRequest("stu-a", tuition, 900_000m, null, new DateOnly(2020, 6, 5), null),
            actorId);
        await subs.CreateAsync(
            new CreateSubscriptionRequest("stu-b", tuition, 700_000m, null, new DateOnly(2020, 6, 20), null),
            actorId);
        // C — martdan boshlangan, iyunda tugaydi.
        var subC = await subs.CreateAsync(
            new CreateSubscriptionRequest("stu-c", tuition, 500_000m, null, new DateOnly(2020, 3, 1), null),
            actorId);
        await subs.EndAsync(subC.Id, new EndSubscriptionRequest(new DateOnly(2020, 6, 15)), actorId);

        var queries = new FinanceReportQueries(db);
        var expectation = await queries.RevenueExpectationAsync(june);
        var changes = await queries.RevenueExpectationChangesAsync(new ChangeJournalQuery(june, PageSize: 100));

        var joined = changes.Rows.Where(r => r.Kind == ChangeJournalKind.StudentJoined)
            .Select(r => r.StudentId).Distinct().Count();
        var left = changes.Rows.Where(r => r.Kind == ChangeJournalKind.StudentLeft)
            .Select(r => r.StudentId).Distinct().Count();

        Assert.Equal(expectation.StudentsAdmitted, joined);
        Assert.Equal(expectation.StudentsDeparted, left);
        Assert.Equal(2, joined);
        Assert.Equal(1, left);

        var leftRow = changes.Rows.Single(r => r.Kind == ChangeJournalKind.StudentLeft);
        Assert.Equal(-500_000m, leftRow.NetEffect);
        Assert.Equal("Tizim", leftRow.Author); // audit_log'da HttpContext yo'q — sukut "Tizim"

        var joinedRows = changes.Rows.Where(r => r.Kind == ChangeJournalKind.StudentJoined).ToList();
        Assert.Contains(joinedRows, r => r.StudentId == "stu-a" && r.NetEffect == 900_000m);
        Assert.Contains(joinedRows, r => r.StudentId == "stu-b" && r.NetEffect == 700_000m);
    }

    // =====================================================================
    //  3. tariffChanged — audit_log Before/After farqi
    // =====================================================================

    [Fact]
    public async Task TariffChanged_faqat_summa_ozgarganda_paydo_boladi()
    {
        await using var db = await NewDbAsync("tariff");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");

        db.Students.Add(NewStudent("stu-t", "Tarif T", "7-A"));
        await db.SaveChangesAsync();

        var subs = Subscriptions(db, actorId);
        var created = await subs.CreateAsync(
            new CreateSubscriptionRequest("stu-t", tuition, 800_000m, null, new DateOnly(2021, 1, 1), null),
            actorId);

        // Tahrirlar HOZIR sodir bo'ladi (`audit_log.Timestamp` — `AppClock.Iso()`,
        // orqaga sana bilan yozib bo'lmaydi) — shuning uchun jurnal JORIY oydan so'raladi.
        var editMonth = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);
        // Summasiz — faqat tafsilot o'zgaradi: tariffChanged BO'LMASLIGI kerak.
        await subs.UpdateAsync(created.Id, new UpdateSubscriptionRequest(800_000m, "yangi tafsilot", null), actorId);
        // Summasi o'zgaradi — tariffChanged BO'LISHI kerak.
        await subs.UpdateAsync(created.Id, new UpdateSubscriptionRequest(950_000m, "yangi tafsilot", null), actorId);

        var queries = new FinanceReportQueries(db);
        var changes = await queries.RevenueExpectationChangesAsync(new ChangeJournalQuery(editMonth, PageSize: 100));

        var tariffRows = changes.Rows.Where(r => r.Kind == ChangeJournalKind.TariffChanged).ToList();
        var tariffRow = Assert.Single(tariffRows);
        Assert.Equal("stu-t", tariffRow.StudentId);
        Assert.Equal(150_000m, tariffRow.NetEffect);
        Assert.Equal("Tizim", tariffRow.Author);
    }

    // =====================================================================
    //  4. Chegirma voqealari — so'ralgan / tasdiqlangan / rad etilgan / tugagan
    // =====================================================================

    /// <summary>
    /// <b>DIQQAT:</b> <c>Discount.CreatedAt</c>/<c>DecidedAt</c> HAR DOIM
    /// <c>AppClock.NowInstant</c> — servisda buni ORQAGA sana bilan yozib
    /// bo'lmaydi (test uchun ham soxtalashtirilmagan, real vaqtni ishlatadi).
    /// Shuning uchun "so'ralgan"/"qaror qilingan" testlari HAQIQIY joriy oyni
    /// so'raydi, <see cref="AppClock.Today"/> dan. Faqat <c>EndsOn</c>
    /// (muddati tugash — "expired" kind) erkin sana bilan sinaladi, chunki u
    /// so'rovda TO'G'RIDAN-TO'G'RI beriladi.
    /// </summary>
    [Fact]
    public async Task Chegirma_soralganda_NetEffect_nol_va_shu_oyda_korinadi()
    {
        await using var db = await NewDbAsync("discounts");
        var requesterId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");

        db.Students.Add(NewStudent("stu-d1", "Cheg D1", "8-A"));
        await db.SaveChangesAsync();

        var discounts = Discounts(db);
        var currentMonth = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);

        await discounts.CreateAsync(
            new CreateDiscountRequest("stu-d1", tuition, 20m, 0m, "Ko'p farzandli", AppClock.Today, null),
            requesterId);

        var queries = new FinanceReportQueries(db);
        var changes = await queries.RevenueExpectationChangesAsync(new ChangeJournalQuery(currentMonth, PageSize: 100));

        var row = Assert.Single(changes.Rows, r => r.Kind == ChangeJournalKind.DiscountRequested);
        Assert.Equal("stu-d1", row.StudentId);
        Assert.Equal(0m, row.NetEffect);
    }

    [Fact]
    public async Task Tasdiqlangan_va_radetilgan_chegirma_togri_NetEffect()
    {
        await using var db = await NewDbAsync("discounts2");
        var requesterId = await SeedUserAsync(db, Roles.Admin);
        var directorId = await SeedUserAsync(db, Roles.SuperAdmin);
        var tuition = await CategoryIdAsync(db, "tuition");

        db.Students.AddRange(
            NewStudent("stu-e1", "Qaror E1", "9-A"),
            NewStudent("stu-e2", "Qaror E2", "9-A"));
        await db.SaveChangesAsync();

        var discounts = Discounts(db);
        var currentMonth = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);

        var e1 = await discounts.CreateAsync(
            new CreateDiscountRequest("stu-e1", tuition, 25m, 0m, "Aka-uka", AppClock.Today, null),
            requesterId);
        var e2 = await discounts.CreateAsync(
            new CreateDiscountRequest("stu-e2", tuition, 10m, 0m, "Boshqa", AppClock.Today, null),
            requesterId);

        await discounts.ApproveAsync(e1.Id, directorId);
        await discounts.RejectAsync(e2.Id, directorId, "Yetarli asos yo'q");

        var queries = new FinanceReportQueries(db);
        var changes = await queries.RevenueExpectationChangesAsync(new ChangeJournalQuery(currentMonth, PageSize: 100));

        Assert.Equal(2, changes.Rows.Count(r => r.Kind == ChangeJournalKind.DiscountRequested));

        var approvedRow = changes.Rows.Single(r => r.Kind == ChangeJournalKind.DiscountApproved);
        Assert.Null(approvedRow.NetEffect); // g'olib chegirma noaniq — ataylab null
        Assert.Equal($"Test {Roles.SuperAdmin}", approvedRow.Author);

        var rejectedRow = changes.Rows.Single(r => r.Kind == ChangeJournalKind.DiscountRejected);
        Assert.Equal(0m, rejectedRow.NetEffect); // rad etilgan — hech qachon ta'sir qilmagan, ANIQ 0
    }

    [Fact]
    public async Task Tasdiqlangan_chegirma_muddati_tugaganda_expired_korinadi()
    {
        await using var db = await NewDbAsync("discexpire");
        var requesterId = await SeedUserAsync(db, Roles.Admin);
        var directorId = await SeedUserAsync(db, Roles.SuperAdmin);
        var tuition = await CategoryIdAsync(db, "tuition");

        db.Students.Add(NewStudent("stu-f1", "Muddat F1", "12-A"));
        await db.SaveChangesAsync();

        var discounts = Discounts(db);
        var startDate = new DateOnly(2022, 1, 1);
        var expiryDate = new DateOnly(2022, 6, 20);
        var expiryMonth = new DateOnly(2022, 6, 1);

        var f1 = await discounts.CreateAsync(
            new CreateDiscountRequest("stu-f1", tuition, 10m, 0m, "Vaqtinchalik", startDate, expiryDate),
            requesterId);
        await discounts.ApproveAsync(f1.Id, directorId);

        var queries = new FinanceReportQueries(db);
        var changes = await queries.RevenueExpectationChangesAsync(new ChangeJournalQuery(expiryMonth, PageSize: 100));

        var expiredRow = Assert.Single(changes.Rows, r => r.Kind == ChangeJournalKind.DiscountExpired);
        Assert.Equal("stu-f1", expiredRow.StudentId);
        Assert.Equal(expiryDate, expiredRow.Date);
        Assert.Null(expiredRow.NetEffect);
    }

    // =====================================================================
    //  5. studentArchived — arxiv sanasi, obuna YOPILMAGAN holatda
    // =====================================================================

    [Fact]
    public async Task Arxivlangan_lekin_obunasi_ochiq_oquvchi_alohida_korinadi()
    {
        await using var db = await NewDbAsync("archive");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");

        // Arxivlangan o'quvchiga YANGI obuna ochib bo'lmaydi (SubscriptionService
        // qoidasi), shuning uchun avval obuna ochiladi, KEYIN o'quvchi arxivlanadi
        // — voqealar haqiqiy hayotda ham shu tartibda bo'ladi.
        var gap = NewStudent("stu-gap", "Teshik G", "10-A");
        var clean = NewStudent("stu-clean", "Toza C", "10-A");
        db.Students.AddRange(gap, clean);
        await db.SaveChangesAsync();

        // `clean` uchun obuna HAM shu oy yopiladi — dublikat (studentArchived)
        // paydo bo'lmasligi kerak, faqat studentLeft.
        var subs = Subscriptions(db, actorId);
        var cleanSub = await subs.CreateAsync(
            new CreateSubscriptionRequest("stu-clean", tuition, 400_000m, null, new DateOnly(2023, 1, 1), null),
            actorId);
        await subs.EndAsync(cleanSub.Id, new EndSubscriptionRequest(new DateOnly(2023, 5, 18)), actorId);
        // `gap` uchun ATAYLAB obuna yopilmaydi — teshik shu.
        await subs.CreateAsync(
            new CreateSubscriptionRequest("stu-gap", tuition, 400_000m, null, new DateOnly(2023, 1, 1), null),
            actorId);

        gap.IsArchived = true;
        gap.ArchivedAt = "2023-05-12";
        gap.ArchiveReason = "Boshqa maktabga ketdi";
        clean.IsArchived = true;
        clean.ArchivedAt = "2023-05-18";
        clean.ArchiveReason = "Boshqa maktabga ketdi";
        await db.SaveChangesAsync();

        var queries = new FinanceReportQueries(db);
        var changes = await queries.RevenueExpectationChangesAsync(
            new ChangeJournalQuery(new DateOnly(2023, 5, 1), PageSize: 100));

        var archivedRows = changes.Rows.Where(r => r.Kind == ChangeJournalKind.StudentArchived).ToList();
        Assert.Single(archivedRows);
        Assert.Equal("stu-gap", archivedRows[0].StudentId);
        Assert.Null(archivedRows[0].NetEffect);

        Assert.Contains(changes.Rows, r => r.Kind == ChangeJournalKind.StudentLeft && r.StudentId == "stu-clean");
        Assert.DoesNotContain(changes.Rows, r => r.Kind == ChangeJournalKind.StudentArchived && r.StudentId == "stu-clean");
    }

    // =====================================================================
    //  6. Tur filtri va sahifalash
    // =====================================================================

    [Fact]
    public async Task Tur_filtri_va_sahifalash_togri_ishlaydi()
    {
        await using var db = await NewDbAsync("paging");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");
        var month = new DateOnly(2024, 1, 1);

        for (var i = 0; i < 5; i++)
        {
            var id = $"stu-p{i}";
            db.Students.Add(NewStudent(id, $"Sahifa {i}", "11-A"));
        }
        await db.SaveChangesAsync();

        var subs = Subscriptions(db, actorId);
        for (var i = 0; i < 5; i++)
        {
            await subs.CreateAsync(
                new CreateSubscriptionRequest($"stu-p{i}", tuition, 100_000m, null, new DateOnly(2024, 1, 1 + i), null),
                actorId);
        }

        var queries = new FinanceReportQueries(db);

        var filtered = await queries.RevenueExpectationChangesAsync(
            new ChangeJournalQuery(month, ChangeJournalKind.StudentJoined, PageSize: 100));
        Assert.Equal(5, filtered.Total);
        Assert.All(filtered.Rows, r => Assert.Equal(ChangeJournalKind.StudentJoined, r.Kind));

        var page1 = await queries.RevenueExpectationChangesAsync(
            new ChangeJournalQuery(month, ChangeJournalKind.StudentJoined, Page: 1, PageSize: 2));
        var page2 = await queries.RevenueExpectationChangesAsync(
            new ChangeJournalQuery(month, ChangeJournalKind.StudentJoined, Page: 2, PageSize: 2));

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Rows.Count);
        Assert.Equal(2, page2.Rows.Count);
        Assert.DoesNotContain(page2.Rows[0].StudentId, page1.Rows.Select(r => r.StudentId));
    }

    // =====================================================================
    //  Test ma'lumoti
    // =====================================================================

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("chgjrnl_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static AuditService TestAudit(AppDbContext db) => new(db, new HttpContextAccessor());

    private static SubscriptionService Subscriptions(AppDbContext db, string actorId) =>
        new(db, TestAudit(db), new InvoiceService(db, new LedgerService(db)));

    private static DiscountService Discounts(AppDbContext db) => new(db, TestAudit(db));

    private static async Task<string> SeedUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"Test {role}",
            Role = role,
            Email = $"{role}.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> CategoryIdAsync(AppDbContext db, string code) =>
        await db.FeeCategories.AsNoTracking().Where(c => c.Code == code).Select(c => c.Id).SingleAsync();

    private static Student NewStudent(string id, string fullName, string className) => new()
    {
        Id = id,
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = className,
        EnrollmentDate = "2018-09-01",
    };
}
