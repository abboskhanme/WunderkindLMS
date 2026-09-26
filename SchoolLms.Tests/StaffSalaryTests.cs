using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Every employee gets a salary (docs/modules/employees-unified.md): staff accounts carry
/// phone / monthly salary / start date, get paid through <c>api/admin/staff/{id}/salary-*</c>
/// (same gate as the teacher salary endpoints) and appear in the salary report after teachers.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StaffSalaryTests(ApiFixture fixture)
{
    private const string StaffUrl = "/api/admin/staff";
    private const string ReportUrl = "/api/admin/finance/salary-report";

    // 3 100 000 / 31 × 21 = 2 100 000 exactly — January prorated from the 11th.
    private const decimal MonthlySalary = 3_100_000m;
    private const string StartDate = "2025-01-11";

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // =================================================================
    //  Staff card: phone / salary / start date
    // =================================================================

    [Fact]
    public async Task Xodim_telefon_oylik_va_boshlanish_sanasi_bilan_yaratiladi_va_yangilanadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var created = await admin.PostAsJsonAsync(StaffUrl, new
        {
            fullName = $"Oylik Xodim {tag}",
            position = "Buxgalter",
            phone = " +998 97 666 66 66 ",
            salary = 2_500_000.50m,
            salaryStartDate = "2026-09-15",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var dto = (await created.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Equal("+998 97 666 66 66", dto.Phone);
        Assert.Equal(2_500_000.50m, dto.Salary);
        Assert.Equal("2026-09-15", dto.SalaryStartDate);

        // Omitted fields (null) stay as they are on update.
        var renamed = await admin.PutAsJsonAsync($"{StaffUrl}/{dto.Id}",
            new { fullName = $"Oylik Xodim 2 {tag}", position = "Bosh buxgalter" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var kept = (await renamed.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Equal("+998 97 666 66 66", kept.Phone);
        Assert.Equal(2_500_000.50m, kept.Salary);
        Assert.Equal("2026-09-15", kept.SalaryStartDate);

        // Explicit values replace; "" clears the start date.
        var changed = await admin.PutAsJsonAsync($"{StaffUrl}/{dto.Id}", new
        {
            fullName = kept.FullName, position = kept.Position,
            phone = "", salary = 3_000_000m, salaryStartDate = "",
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = (await changed.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Equal("", after.Phone);
        Assert.Equal(3_000_000m, after.Salary);
        Assert.Equal("", after.SalaryStartDate);

        // The list returns the same fields, serialized in camelCase.
        var list = await admin.GetStringAsync(StaffUrl);
        using var doc = JsonDocument.Parse(list);
        var row = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == dto.Id);
        Assert.Equal(3_000_000m, row.GetProperty("salary").GetDecimal());
        Assert.Equal("", row.GetProperty("phone").GetString());
        Assert.Equal("", row.GetProperty("salaryStartDate").GetString());

        // A salary change is money configuration — it leaves an audit row.
        await fixture.Api.WithDbAsync(async db =>
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == "StaffSalary" && a.EntityId == dto.Id
                                                         && a.Action == "update")));
    }

    /// <summary>
    /// The staff list stays readable to every staff account (reference data), but the salary is
    /// money: only admins, finance roles and the staff section (full or view) see it.
    /// </summary>
    [Theory]
    [InlineData("students", false)]
    [InlineData("staff:view", true)]
    [InlineData("finance", true)]
    public async Task Xodimlar_royxatida_oylikni_faqat_ruxsatli_kishi_koradi(string permission, bool sees)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staffId, _, _) = await CreateStaffAsync(admin);
        using var reader = await fixture.Api.ClientAsAsync(Roles.Staff, permission);

        var res = await reader.GetAsync(StaffUrl);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var row = (await res.Content.ReadFromJsonAsync<List<StaffDto>>())!.Single(s => s.Id == staffId);

        Assert.Equal(sees ? MonthlySalary : 0m, row.Salary);
        Assert.Equal(sees ? StartDate : "", row.SalaryStartDate);
    }

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(1.005, null)]
    [InlineData(null, "2026-02-30")]
    [InlineData(null, "26.09.2026")]
    [InlineData(null, "2026-9-1")]
    public async Task Notogri_oylik_yoki_sana_400(double? salary, string? start)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var create = await admin.PostAsJsonAsync(StaffUrl, new
        {
            fullName = $"Xato Oylik {tag}", position = "", salary = (decimal?)salary, salaryStartDate = start,
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.Users.AnyAsync(u => u.FullName == $"Xato Oylik {tag}")));

        // Update is refused the same way and leaves the row untouched.
        var ok = await admin.PostAsJsonAsync(StaffUrl, new
        {
            fullName = $"Tog'ri Oylik {tag}", position = "", salary = 1_000_000m, salaryStartDate = "2026-01-01",
        });
        var id = (await ok.Content.ReadFromJsonAsync<StaffDto>())!.Id;

        var update = await admin.PutAsJsonAsync($"{StaffUrl}/{id}", new
        {
            fullName = $"Tog'ri Oylik {tag}", position = "", salary = (decimal?)salary, salaryStartDate = start,
        });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
            Assert.Equal(1_000_000m, user.Salary);
            Assert.Equal("2026-01-01", user.SalaryStartDate);
        });
    }

    // =================================================================
    //  Salary report: staff rows after teachers
    // =================================================================

    [Fact]
    public async Task Maosh_hisobotida_xodim_qatori_qisman_birinchi_oy_va_berilgan_maosh_bilan()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staffId, name, position) = await CreateStaffAsync(admin);

        // Jan prorated (21 of 31 days) + Feb + Mar.
        var q1 = await ReportRowAsync(admin, staffId, "2025-01-01", "2025-03-31");
        Assert.Equal(MonthlySalary, q1.Salary);
        Assert.Equal(3, q1.Months);
        Assert.Equal(2_100_000m + 2 * MonthlySalary, q1.Expected);
        Assert.Equal(0m, q1.TotalPaid);
        Assert.Equal(name, q1.TeacherName);
        Assert.Equal(position, q1.Position);
        Assert.Equal(SalaryReportKinds.Staff, q1.Kind);

        // Months before the start date do not count.
        var dec = await ReportRowAsync(admin, staffId, "2024-12-01", "2025-01-31");
        Assert.Equal(1, dec.Months);
        Assert.Equal(2_100_000m, dec.Expected);

        // A payment through the staff endpoint reaches TotalPaid in the payment's month.
        var paid = await admin.PostAsJsonAsync($"{StaffUrl}/{staffId}/salary-payments",
            new { amount = 250_000m, method = PaymentMethod.Transfer, note = "Sentabr avansi" });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var expense = (await paid.Content.ReadFromJsonAsync<ExpenseDto>())!;
        Assert.Equal(ExpenseStatus.Posted, expense.Status);
        Assert.Equal(staffId, expense.EmployeeUserId);
        Assert.Equal(name, expense.EmployeeName);
        Assert.Null(expense.TeacherId);

        var month = AppClock.Today.ToString("yyyy-MM");
        var now = await ReportRowAsync(admin, staffId, $"{month}-01", $"{month}-28");
        Assert.Equal(1, now.PaymentsCount);
        Assert.Equal(250_000m, now.TotalPaid);
        Assert.Equal(MonthlySalary, now.Expected);
        Assert.Equal(MonthlySalary - 250_000m, now.Remaining);

        // Teacher rows are unchanged: every teacher once, all before the staff rows.
        var report = (await admin.GetFromJsonAsync<List<SalaryReportRowDto>>(
            $"{ReportUrl}?from={month}-01&to={month}-28"))!;
        var teacherRows = report.TakeWhile(r => r.Kind == SalaryReportKinds.Teacher).ToList();
        Assert.All(teacherRows, r => Assert.Equal("O'qituvchi", r.Position));
        Assert.All(report.Skip(teacherRows.Count), r => Assert.Equal(SalaryReportKinds.Staff, r.Kind));
        Assert.DoesNotContain(teacherRows, r => r.TeacherId == staffId);
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(await db.Teachers.CountAsync(), teacherRows.Count));

        // Wire names are camelCase: kind / position.
        var raw = await admin.GetStringAsync($"{ReportUrl}?from={month}-01&to={month}-28");
        using var doc = JsonDocument.Parse(raw);
        var row = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("teacherId").GetString() == staffId);
        Assert.Equal("staff", row.GetProperty("kind").GetString());
        Assert.Equal(position, row.GetProperty("position").GetString());

        // History and ledger mirror the teacher shapes.
        var history = (await admin.GetFromJsonAsync<SalaryHistoryDto>($"{StaffUrl}/{staffId}/salary-history"))!;
        Assert.Equal(250_000m, history.TotalPaid);
        Assert.Equal(MonthlySalary, history.Salary);
        Assert.Single(history.Payments);

        var ledger = (await admin.GetFromJsonAsync<SalaryLedgerDto>(
            $"{StaffUrl}/{staffId}/salary-ledger?from=2025-01-01&to=2025-03-31"))!;
        Assert.Equal(["2025-01", "2025-02", "2025-03"], ledger.Months.Select(m => m.Month));
        Assert.Equal(2_100_000m, ledger.Months[0].Expected);
        Assert.Equal(2_100_000m + 2 * MonthlySalary, ledger.TotalExpected);

        // Export carries the same rows under the new file name.
        var export = await admin.GetAsync($"{ReportUrl}/export?from={month}-01&to={month}-28");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("xodimlar-maoshi.xlsx", export.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Maosh_berilgan_xodimni_ochirib_bolmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staffId, _, _) = await CreateStaffAsync(admin);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{StaffUrl}/{staffId}/salary-payments",
            new { amount = 100_000m, method = PaymentMethod.Transfer })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"{StaffUrl}/{staffId}")).StatusCode);
    }

    [Fact]
    public async Task Chiqimda_xodim_faqat_maoshda_va_oqituvchi_bilan_birga_emas()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staffId, _, _) = await CreateStaffAsync(admin);
        var (teacherUser, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        string teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
            teacherId = (await db.Teachers.AsNoTracking().SingleAsync(t => t.UserId == teacherUser.Id)).Id);
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var otherCategory = await admin.PostAsJsonAsync("/api/admin/expenses", new
        {
            onDate = today, category = "utilities", amount = 1000m, method = PaymentMethod.Transfer,
            employeeUserId = staffId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, otherCategory.StatusCode);
        Assert.Equal("employee_not_allowed", await ErrorCodeAsync(otherCategory));

        var both = await admin.PostAsJsonAsync("/api/admin/expenses", new
        {
            onDate = today, category = "salary", amount = 1000m, method = PaymentMethod.Transfer,
            teacherId, employeeUserId = staffId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal("salary_recipient_ambiguous", await ErrorCodeAsync(both));

        // A teacher's account is not a staff account: the staff salary endpoint does not pay it.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(
            $"{StaffUrl}/{teacherUser.Id}/salary-payments",
            new { amount = 1000m, method = PaymentMethod.Transfer })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.Expenses.AnyAsync(e => e.EmployeeUserId == staffId)));
    }

    // =================================================================
    //  RBAC — same gate as the teacher salary endpoints
    // =================================================================

    [Fact]
    public async Task Maosh_endpointlari_moliya_darvozasi_ortida()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staffId, _, _) = await CreateStaffAsync(admin);

        string[] reads = [$"{StaffUrl}/{staffId}/salary-history", $"{StaffUrl}/{staffId}/salary-ledger"];
        var pay = $"{StaffUrl}/{staffId}/salary-payments";
        object body = new { amount = 50_000m, method = PaymentMethod.Transfer };

        using var finance = await fixture.Api.ClientAsAsync(Roles.Staff, "finance");
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "finance" + Roles.ViewSuffix);
        // The staff section key alone is not a money permission.
        using var noFinance = await fixture.Api.ClientAsAsync(Roles.Staff, "staff");
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        using var cashier = await fixture.Api.ClientAsAsync(Roles.Cashier);

        foreach (var url in reads)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await noFinance.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(url)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(pay, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noFinance.PostAsJsonAsync(pay, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync(pay, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(pay, body)).StatusCode);

        // Nothing was written by the refused requests.
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.Expenses.AnyAsync(e => e.EmployeeUserId == staffId)));

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(pay, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync(pay, body)).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(2, await db.Expenses.CountAsync(e => e.EmployeeUserId == staffId)));
    }

    // =================================================================
    //  Helpers
    // =================================================================

    private static async Task<(string Id, string Name, string Position)> CreateStaffAsync(HttpClient admin)
    {
        var tag = Tag();
        var name = $"Maosh Xodimi {tag}";
        var position = $"Qorovul {tag}";
        var res = await admin.PostAsJsonAsync(StaffUrl, new
        {
            fullName = name, position, salary = MonthlySalary, salaryStartDate = StartDate,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return ((await res.Content.ReadFromJsonAsync<StaffDto>())!.Id, name, position);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    private static async Task<SalaryReportRowDto> ReportRowAsync(
        HttpClient client, string staffId, string from, string to)
    {
        var rows = await client.GetFromJsonAsync<List<SalaryReportRowDto>>($"{ReportUrl}?from={from}&to={to}");
        return Assert.Single(rows!, r => r.TeacherId == staffId);
    }
}
