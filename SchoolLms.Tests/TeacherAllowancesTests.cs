using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// The salary reads everything that changes it (client, 2026-10-15): the teacher's certificates and named
/// allowances ("sertifikat va ustama ... to'g'ridan to'g'ri moliyaga"), and Moliya → Bonus / Jarima ("jarima ...
/// biryola moliyaga bog'langan, avtomatik oylikdagi hisob-kitob"). docs/modules/teacher-allowances.md.
///
/// <para><b>Money.</b> The calculation test writes every number out: 4 Monday lessons × 50 000 = 200 000 lesson
/// pay, and each part on top of it.</para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherAllowancesTests(ApiFixture fixture) : IAsyncLifetime
{
    private const decimal HourRate = 50_000m;
    private const string Url = "/api/admin/teachers";

    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  1. The calculation — own database, every number written out
     * ================================================================== */

    /// <summary>
    /// September 2026: Mondays 7, 14, 21, 28 → 4 lessons × 50 000 = 200 000.
    /// + ustama 10 % = 20 000, + certificate 20 % = 40 000, + allowance 300 000, + bonus 50 000, − fine 30 000
    /// = <b>580 000</b>. An expired certificate, a next-month allowance and a cancelled fine change nothing.
    /// </summary>
    [Fact]
    public async Task Sertifikat_ustama_bonus_va_jarima_oylikka_kiradi()
    {
        await using var db = await NewDbAsync("pay");
        var teacher = await SeedTeacherAsync(db);
        var director = new AppUser { FullName = "Direktor", Role = Roles.SuperAdmin, Email = $"dir.{Guid.NewGuid():N}" };
        director.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(director);
        var reason = new AdjustmentReason { Kind = AdjustmentKind.Penalty, Name = "Kechikish" };
        var bonusReason = new AdjustmentReason { Kind = AdjustmentKind.Bonus, Name = "Faollik" };
        db.AdjustmentReasons.AddRange(reason, bonusReason);
        db.TeacherAllowances.AddRange(
            Allowance(teacher.Id, TeacherAllowanceSources.Certificate, TeacherAllowanceKinds.Percent, 20m, "2026-09"),
            Allowance(teacher.Id, TeacherAllowanceSources.Ustama, TeacherAllowanceKinds.Fixed, 300_000m, "2026-09"),
            // expired before September — never paid in it
            Allowance(teacher.Id, TeacherAllowanceSources.Certificate, TeacherAllowanceKinds.Percent, 50m, "2026-01",
                expires: new DateOnly(2026, 8, 31)),
            // starts in October
            Allowance(teacher.Id, TeacherAllowanceSources.Ustama, TeacherAllowanceKinds.Fixed, 100_000m, "2026-10"));
        await db.SaveChangesAsync();
        var by = director.Id;
        var cancelled = Adjustment(teacher.Id, reason.Id, AdjustmentKind.Penalty, 999_999m, 9, by);
        db.PayrollAdjustments.AddRange(
            Adjustment(teacher.Id, bonusReason.Id, AdjustmentKind.Bonus, 50_000m, 9, by),
            Adjustment(teacher.Id, reason.Id, AdjustmentKind.Penalty, 30_000m, 9, by),
            cancelled,
            // larger than the whole October pay — the month stops at 0
            Adjustment(teacher.Id, reason.Id, AdjustmentKind.Penalty, 5_000_000m, 10, by));
        await db.SaveChangesAsync();
        db.PayrollAdjustments.Add(Adjustment(teacher.Id, reason.Id, AdjustmentKind.Penalty, 999_999m, 9, by, reversalOf: cancelled.Id));
        await db.SaveChangesAsync();

        var pay = await TeacherPayContext.LoadAsync(db);
        var sep = pay.For(teacher, "2026-09");
        Assert.Equal(4, sep.PlannedLessons);
        Assert.Equal(200_000m, sep.LessonPay);
        Assert.Equal(20_000m, sep.UstamaAmount);
        Assert.Equal(20m, sep.AllowancePct);
        Assert.Equal(300_000m, sep.AllowanceFixed);
        Assert.Equal(340_000m, sep.AllowanceAmount);
        Assert.Equal(50_000m, sep.Bonus);
        Assert.Equal(30_000m, sep.Penalty);
        Assert.Equal(580_000m, sep.Total);
        // the lesson part is exactly the old formula
        Assert.Equal(TeacherSalaryCalc.MonthlyForMonth(pay.WeekdaysOf(teacher.Id), teacher.Category, pay.Meta,
            "2026-09", null, [], teacher.BonusPct, pay.Quarters), sep.LessonPay + sep.UstamaAmount);

        // October: 4 lessons again, both fixed allowances (300 000 + 100 000) — and a 5 000 000 fine → 0, not negative.
        var oct = pay.For(teacher, "2026-10");
        Assert.Equal(200_000m + 20_000m + 40_000m + 400_000m, oct.LessonPay + oct.UstamaAmount + oct.AllowanceAmount);
        Assert.Equal(0m, oct.Total);

        // July has no quarter: no lessons, so no fixed allowance either.
        var jul = pay.For(teacher, "2026-07");
        Assert.Equal(0, jul.PlannedLessons);
        Assert.Equal(0m, jul.AllowanceFixed);
        Assert.Equal(0m, jul.Total);

        // The ledger (Moliya → Xodimlar maoshi) shows the same month and its parts.
        var ledger = await SalaryLedger.BuildAsync(db, teacher, "2026-09-01", "2026-10-31");
        var sepRow = ledger.Months.Single(m => m.Month == "2026-09");
        Assert.Equal(580_000m, sepRow.Expected);
        Assert.Equal(200_000m, sepRow.LessonPay);
        Assert.Equal(20_000m, sepRow.Ustama);
        Assert.Equal(340_000m, sepRow.Allowances);
        Assert.Equal(50_000m, sepRow.Bonus);
        Assert.Equal(30_000m, sepRow.Penalty);
        Assert.Equal(0m, ledger.Months.Single(m => m.Month == "2026-10").Expected);
        Assert.Equal(580_000m, ledger.TotalExpected);
    }

    /// <summary>A non-teacher employee: fixed salary + bonus − fine, in the ledger.</summary>
    [Fact]
    public async Task Xodim_maoshidan_jarima_ayriladi_bonus_qoshiladi()
    {
        await using var db = await NewDbAsync("staffpay");
        var staff = new AppUser
        {
            FullName = "Xodim Jarimali", Role = Roles.Staff, Email = $"staff-pay.{Guid.NewGuid():N}",
            Salary = 3_000_000m, SalaryStartDate = "2026-01-01",
        };
        staff.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(staff);
        var reason = new AdjustmentReason { Kind = AdjustmentKind.Penalty, Name = "Kechikish" };
        var bonusReason = new AdjustmentReason { Kind = AdjustmentKind.Bonus, Name = "Faollik" };
        db.AdjustmentReasons.AddRange(reason, bonusReason);
        await db.SaveChangesAsync();
        db.PayrollAdjustments.AddRange(
            new PayrollAdjustment
            {
                UserId = staff.Id, Kind = AdjustmentKind.Penalty, ReasonId = reason.Id, Amount = 400_000m,
                PeriodYear = 2026, PeriodMonth = 9, CreatedBy = staff.Id, CreatedAt = AppClock.NowInstant,
            },
            new PayrollAdjustment
            {
                UserId = staff.Id, Kind = AdjustmentKind.Bonus, ReasonId = bonusReason.Id, Amount = 100_000m,
                PeriodYear = 2026, PeriodMonth = 9, CreatedBy = staff.Id, CreatedAt = AppClock.NowInstant,
            });
        await db.SaveChangesAsync();

        var ledger = await SalaryLedger.BuildForStaffAsync(db, staff, "2026-09-01", "2026-10-31");
        var sep = ledger.Months.Single(m => m.Month == "2026-09");
        Assert.Equal(2_700_000m, sep.Expected);
        Assert.Equal(3_000_000m, sep.LessonPay);
        Assert.Equal(100_000m, sep.Bonus);
        Assert.Equal(400_000m, sep.Penalty);
        Assert.Equal(3_000_000m, ledger.Months.Single(m => m.Month == "2026-10").Expected);
    }

    /* =====================================================================
     *  2. The API — teacher card tabs
     * ================================================================== */

    [Fact]
    public async Task Sertifikat_qoshiladi_tahrirlanadi_ochiriladi_va_oylik_tafsilotida_korinadi()
    {
        var teacherId = await SeedApiTeacherAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var month = AppClock.Now.ToString("yyyy-MM");

        var created = await admin.PostAsJsonAsync($"{Url}/{teacherId}/allowances", Request(
            TeacherAllowanceSources.Certificate, "IELTS", TeacherAllowanceKinds.Percent, 15m, month,
            level: "7.5", fileUrl: "/uploads/ielts.pdf", expires: "2030-01-31"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var cert = (await created.Content.ReadFromJsonAsync<TeacherAllowanceDto>())!;
        Assert.True(cert.ActiveNow);
        Assert.False(cert.IsExpired);
        Assert.Equal("7.5", cert.Level);

        var ustama = await admin.PostAsJsonAsync($"{Url}/{teacherId}/allowances", Request(
            TeacherAllowanceSources.Ustama, "Sinf rahbarligi", TeacherAllowanceKinds.Fixed, 250_000m, month));
        Assert.Equal(HttpStatusCode.OK, ustama.StatusCode);

        var list = (await admin.GetFromJsonAsync<TeacherAllowancesDto>($"{Url}/{teacherId}/allowances"))!;
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(15m, list.CurrentMonth.AllowancePct);
        Assert.Equal(month, list.CurrentMonth.Month);

        // The salary detail lists both lines; its total is the sum of the named parts.
        var detail = (await admin.GetFromJsonAsync<TeacherSalaryDetailDto>($"/api/admin/salary-rates/{teacherId}?month={month}"))!;
        Assert.Equal(2, detail.Allowances!.Count);
        Assert.Contains(detail.Allowances, a => a.Title == "IELTS" && a.Value == detail.BaseSalary * 15m / 100m);
        Assert.Equal(Math.Max(0m, detail.BaseSalary + detail.BonusAmount + detail.AllowanceAmount
            + detail.AdjustmentBonus - detail.Penalty), detail.NetSalary);
        var row = (await admin.GetFromJsonAsync<SalaryRatesDto>($"/api/admin/salary-rates?month={month}"))!
            .Teachers.Single(t => t.Id == teacherId);
        Assert.Equal(detail.NetSalary, row.MonthlySalary);
        Assert.Equal(15m, row.AllowancePct);

        // Edit: the certificate ends last month — no longer paid now.
        var prev = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");
        var edited = await admin.PutAsJsonAsync($"{Url}/{teacherId}/allowances/{cert.Id}", Request(
            TeacherAllowanceSources.Certificate, "IELTS", TeacherAllowanceKinds.Percent, 15m, prev, to: prev));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.False((await edited.Content.ReadFromJsonAsync<TeacherAllowanceDto>())!.ActiveNow);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{teacherId}/allowances/{cert.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Url}/{teacherId}/allowances/{cert.Id}")).StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<TeacherAllowancesDto>($"{Url}/{teacherId}/allowances"))!.Items);

        // Every change leaves an audit line on the teacher's salary.
        await fixture.Api.WithDbAsync(async db =>
            Assert.True(await db.AuditLogs.CountAsync(a => a.EntityType == AuditService.EntityTeacherSalary && a.EntityId == teacherId) >= 4));
    }

    [Theory]
    [InlineData("bonus", "X", "fixed", 1, "2026-09", null, null)]          // unknown source
    [InlineData("ustama", " ", "fixed", 1, "2026-09", null, null)]         // empty title
    [InlineData("ustama", "X", "share", 1, "2026-09", null, null)]         // unknown kind
    [InlineData("ustama", "X", "percent", 1001, "2026-09", null, null)]    // percent over 1000
    [InlineData("ustama", "X", "fixed", -1, "2026-09", null, null)]        // negative
    [InlineData("ustama", "X", "fixed", 1, "2026-13", null, null)]         // bad month
    [InlineData("ustama", "X", "fixed", 1, "2026-09", "2026-08", null)]    // ends before it starts
    [InlineData("certificate", "X", "fixed", 1, "2026-09", null, "https://evil.example/x.pdf")] // not our upload
    public async Task Notogri_sorov_400(string source, string title, string kind, decimal amount, string from, string? to, string? file)
    {
        var teacherId = await SeedApiTeacherAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync($"{Url}/{teacherId}/allowances",
            Request(source, title, kind, amount, from, to: to, fileUrl: file));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Ruxsat_faqat_moliya_yozish_admin_va_moliya_xodimi()
    {
        var teacherId = await SeedApiTeacherAsync();
        var path = $"{Url}/{teacherId}/allowances";
        var body = Request(TeacherAllowanceSources.Ustama, "To'garak", TeacherAllowanceKinds.Fixed, 100_000m, "2026-09");

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        // HR without finance, the cashier and a teacher never see pay.
        using var hr = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers");
        using var cashier = await fixture.Api.ClientAsAsync(Roles.Cashier);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        foreach (var c in new[] { hr, cashier, teacher })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(path, body)).StatusCode);
        }

        // Finance read-only: reads, cannot write.
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers", "finance:view");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(path, body)).StatusCode);

        // Finance staff writes.
        using var finance = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers", "finance");
        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync(path, body)).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/no-such-teacher/allowances")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"{Url}/no-such-teacher/allowances", body)).StatusCode);
    }

    /* =====================================================================
     *  Helpers
     * ================================================================== */

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("tal_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>"oliy" teacher, 10 % ustama, one lesson every Monday; quarter 01.09–31.12.2026; 50 000 an hour.</summary>
    private static async Task<Teacher> SeedTeacherAsync(AppDbContext db)
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        if (meta is null)
        {
            meta = new SchoolMeta { CurrentYear = "2026/2027", Name = "Test maktab" };
            db.SchoolMeta.Add(meta);
        }
        meta.SalaryRateOliy = HourRate;
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" });
        var cls = new SchoolClass { Name = "7-PAY", Grade = 7 };
        var subject = new Subject { Name = "Matematika" };
        var teacher = new Teacher { FullName = "Ustoz Sertifikatli", Category = "oliy", BonusPct = 10m };
        db.Classes.Add(cls);
        db.Subjects.Add(subject);
        db.Teachers.Add(teacher);
        db.ScheduleTemplates.Add(new ScheduleTemplate
        {
            ClassId = cls.Id, Name = "Asosiy",
            Lessons = [new ScheduleLesson { Day = 0, Period = 1, SubjectId = subject.Id, TeacherId = teacher.Id }],
        });
        await db.SaveChangesAsync();
        return teacher;
    }

    private async Task<string> SeedApiTeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var id = "";
        await fixture.Api.WithDbAsync(async db => id = (await db.Teachers.SingleAsync(t => t.UserId == user.Id)).Id);
        return id;
    }

    private static TeacherAllowance Allowance(
        string teacherId, string source, string kind, decimal amount, string from, DateOnly? expires = null) => new()
    {
        TeacherId = teacherId, Source = source, Title = source + " " + amount, AmountKind = kind, Amount = amount,
        FromMonth = from, ExpiresOn = expires, CreatedBy = "seed", CreatedAt = AppClock.NowInstant, UpdatedAt = AppClock.NowInstant,
    };

    private static PayrollAdjustment Adjustment(
        string teacherId, Guid reasonId, string kind, decimal amount, short month, string createdBy,
        Guid? reversalOf = null) => new()
    {
        TeacherId = teacherId, Kind = kind, ReasonId = reasonId, Amount = amount, PeriodYear = 2026, PeriodMonth = month,
        CreatedBy = createdBy, CreatedAt = AppClock.NowInstant, ReversalOf = reversalOf,
        ReversalReason = reversalOf is null ? null : "Xato yozilgan",
    };

    private static TeacherAllowanceRequest Request(
        string source, string title, string kind, decimal amount, string from,
        string? to = null, string? level = null, string? fileUrl = null, string? expires = null) =>
        new(source, title, kind, amount, from, to, level, null, null, expires, fileUrl, null);
}
