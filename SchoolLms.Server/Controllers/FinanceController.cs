using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Maosh hisoboti — eski moliya modulidan QOLGAN YAGONA endpoint. P1-21.
// ===========================================================================
//
//  NIMA O'CHDI VA NEGA
//  -------------------
//  Bu controller `finance_transactions` ustidagi to'liq CRUD edi: kirim/chiqim
//  qo'shish, TAHRIRLASH va O'CHIRISH (docs/TASKS.md §1.2 — "The four holes").
//  Aynan o'sha "tahrirlash/o'chirish" mijoz aytgan firibgarlikning o'zi.
//  Jadval va u bilan ishlagan hamma amal P1-21 da olib tashlandi:
//
//    POST/PUT/DELETE  transactions   -> pul endi FAQAT `LedgerService` orqali
//                                       yoziladi va o'zgarmas (SPEC §4.1);
//                                       chiqim: POST /api/admin/expenses,
//                                       to'lov: POST /api/cash/payments.
//    POST             accrue         -> POST /api/admin/billing/accrual/run
//                                       (fon xizmati BillingAccrualService ham
//                                        startupda va har 12 soatda yuradi)
//    GET              transactions   -> to'lovlar: GET /api/billing/payments,
//                                       chiqimlar: GET /api/admin/expenses
//    GET              summary        -> GET /api/admin/finance/pnl
//    GET              monthly        -> GET /api/admin/finance/cashflow
//    GET              student-report -> GET /api/admin/finance/debtors
//
//  NEGA MAOSH HISOBOTI QOLDI
//  -------------------------
//  Uning o'rnini bosadigan yangi ekran YO'Q: `FinanceReportsController` (P1-13)
//  o'quvchi pulini hisoblaydi, o'qituvchi maoshini emas. Manba almashtirildi —
//  endi `expenses` (`SalaryPaymentQuery`), ya'ni raqam jurnalga tushgan pulga
//  mos keladi va P&L bilan bir xil bo'ladi.
//
//  RUXSAT TORAYTIRILDI (F0.02, docs/modules/finance-parity.md §2.15)
//  --------------------------------------------------------------------
//  Ilgari bu yerda faqat `[AdminPerm("finance")]` turardi va izohda "ko'radigan
//  xodimlar to'plami o'zgarmadi" deb yozilgandi. Lekin `AdminPermAttribute`
//  GET'ni HAR QANDAY xodimga ochadi (`:40-42`) — "finance" kaliti ham shart
//  emas edi. Ya'ni butun maktabning maosh jadvali har bir staff akkauntiga
//  ko'rinib turardi, SPEC §4.3 esa hisobotlarni admin va direktorga beradi.
//  Endi bu hisobot ham `FinanceReportsController` bilan bir xil darvoza
//  ostida: `[Authorize(Roles = Roles.FinanceStaff)]` + `[FinanceRole(...)]`.
// ===========================================================================

/// <summary>
/// O'qituvchilarga berilgan maoshlar hisoboti. Batafsil: fayl boshidagi izoh.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[AdminPerm("finance")]
[Route("api/admin/finance")]
public class FinanceController(AppDbContext db) : ControllerBase
{
    /// <summary>O'qituvchi qatorining "Lavozim" ustuni.</summary>
    private const string TeacherPosition = "O'qituvchi";

    /// <summary>
    /// O'qituvchilarga berilgan maoshlar hisoboti (davr bo'yicha): oylik,
    /// kerakli (davr oylari bo'yicha davomatga moslangan), berilgan va qoldiq.
    ///
    /// <para>
    /// "Berilgan" = <c>expenses</c> dagi <c>salary</c> toifasidagi, JURNALGA
    /// TUSHGAN va storno qilinmagan chiqimlar (<see cref="SalaryPaymentQuery"/>).
    /// Direktor tasdig'ini kutayotgan chiqim hali berilmagan pul (SPEC §4.5),
    /// shuning uchun bu ustunga kirmaydi.
    /// </para>
    /// <para>
    /// O'qituvchilardan keyin o'qituvchi BO'LMAGAN xodimlar (role="staff") keladi
    /// (docs/modules/employees-unified.md): <c>Kind = "staff"</c>, <c>TeacherId</c> — akkaunt id'si,
    /// kerakli summa — belgilangan oylik (<see cref="StaffSalaryCalc"/>), berilgan —
    /// <c>expenses.employee_user_id</c> bo'yicha, xuddi shu "jurnalga tushgan, storno qilinmagan" qoida.
    /// </para>
    /// </summary>
    [HttpGet("salary-report")]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<IEnumerable<SalaryReportRowDto>>> SalaryReport(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        // Maosh o'quv yili boshidan hisoblanadi (yanvardan emas) — choraklardagi eng erta oydan.
        var fromMonth = string.IsNullOrEmpty(from)
            ? await TuitionService.AcademicYearStartMonthAsync(db) : from[..7];
        var toMonth = string.IsNullOrEmpty(to) ? TuitionService.CurrentMonth() : to[..7];

        // BITTA so'rov barcha o'qituvchi uchun — o'qituvchilar bo'yicha siklda
        // so'rov yuborish N+1 bo'lardi (maktabda 60+ o'qituvchi).
        var paid = await new SalaryPaymentQuery(db).ForAllAsync(
            MonthStart(fromMonth), MonthEnd(toMonth), ct);

        var teachers = await db.Teachers.OrderBy(t => t.FullName).ToListAsync(ct);
        // Oylik maosh — dars jadvali + toifa narxidan; har oy DAVOMATga moslanadi (kelmagan kun chegiriladi).
        var meta = await db.SchoolMeta.FirstOrDefaultAsync(ct);
        var byWeekdayAll = await TeacherSalaryCalc.LessonsByWeekdayAsync(db);
        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var absentAll = await db.TeacherAttendances
            .Where(a => a.Status == "absent" && a.Date.Length >= 7)
            .Select(a => new { a.TeacherId, a.Date }).ToListAsync(ct);

        var report = teachers.Select(te =>
        {
            var byWeekday = byWeekdayAll.GetValueOrDefault(te.Id) ?? new int[6];
            var nominalMonthly = TeacherSalaryCalc.WithBonus(
                TeacherSalaryCalc.Monthly(byWeekday.Sum(), te.Category, meta), te.BonusPct);
            var absByMonth = absentAll.Where(a => a.TeacherId == te.Id)
                .GroupBy(a => a.Date[..7])
                .ToDictionary(g => g.Key, g => (IEnumerable<string>)g.Select(x => x.Date).ToList());

            // Oylik o'qituvchi ishga kirgan KUNdan hisoblanadi (birinchi oy qisman). Avvalgi oylar — 0.
            var startDate = TeacherSalaryCalc.StartDateOf(te);
            var teacherStartMonth = startDate is { Length: >= 7 } ? startDate[..7] : fromMonth;
            var startMonth = string.CompareOrdinal(teacherStartMonth, fromMonth) > 0 ? teacherStartMonth : fromMonth;
            var monthList = string.CompareOrdinal(startMonth, toMonth) > 0
                ? new List<string>()
                : TuitionService.MonthRange(startMonth, toMonth).ToList();

            var rows = paid
                .Where(p => p.TeacherId == te.Id && string.CompareOrdinal(p.Month, startMonth) >= 0)
                .ToList();
            var totalPaid = rows.Sum(r => r.Amount);
            var expected = monthList.Sum(mn => TeacherSalaryCalc.MonthlyForMonth(
                byWeekday, te.Category, meta, mn, startDate,
                absByMonth.GetValueOrDefault(mn) ?? Enumerable.Empty<string>(), te.BonusPct, quarters));
            return new SalaryReportRowDto(
                te.Id, te.FullName, nominalMonthly, totalPaid, rows.Count,
                monthList.Count, expected, expected - totalPaid,
                SalaryReportKinds.Teacher, TeacherPosition);
        }).ToList();

        report.AddRange(await StaffSalaryRowsAsync(fromMonth, toMonth, ct));
        return report;
    }

    /// <summary>
    /// Xodimlar (role="staff") qatorlari — ikki so'rov, xodimlar soniga bog'liq emas.
    /// Davr qoidasi o'qituvchinikining aynan o'zi: boshlangan oydan oldingi oylar sanalmaydi.
    /// </summary>
    private async Task<List<SalaryReportRowDto>> StaffSalaryRowsAsync(
        string fromMonth, string toMonth, CancellationToken ct)
    {
        var paidByUser = (await new SalaryPaymentQuery(db).ForAllEmployeesAsync(
                MonthStart(fromMonth), MonthEnd(toMonth), ct))
            .ToLookup(p => p.TeacherId);

        var staff = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Staff)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Position, u.Salary, u.SalaryStartDate })
            .ToListAsync(ct);

        return staff.Select(u =>
        {
            var startDate = StaffSalaryCalc.StartDateOf(u.SalaryStartDate);
            var startMonth = StaffSalaryCalc.FirstMonth(startDate, fromMonth);
            var monthList = string.CompareOrdinal(startMonth, toMonth) > 0
                ? new List<string>()
                : TuitionService.MonthRange(startMonth, toMonth).ToList();

            var paid = paidByUser[u.Id]
                .Where(p => string.CompareOrdinal(p.Month, startMonth) >= 0)
                .ToList();
            var totalPaid = paid.Sum(p => p.Amount);
            var expected = monthList.Sum(mn => StaffSalaryCalc.MonthlyForMonth(u.Salary, mn, startDate));

            return new SalaryReportRowDto(
                u.Id, u.FullName, u.Salary, totalPaid, paid.Count,
                monthList.Count, expected, expected - totalPaid,
                SalaryReportKinds.Staff, u.Position);
        }).ToList();
    }

    /// <summary>
    /// <c>GET /api/admin/finance/salary-report/export?from&amp;to</c> — maosh
    /// hisobotining .xlsx nusxasi.
    ///
    /// <para>
    /// Mijoz, 2026-09-19: "yuklab olish csv emas excel fayl uchun bo'lsin".
    /// Raqamlar <see cref="SalaryReport"/> dan olinadi — ekrandagining
    /// aynan o'zi, bu yerda hech narsa qayta hisoblanmaydi.
    /// </para>
    /// </summary>
    [HttpGet("salary-report/export")]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult> SalaryReportExport(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var result = await SalaryReport(from, to, ct);
        if (result.Value is not IEnumerable<SalaryReportRowDto> report)
            return result.Result ?? StatusCode(500);

        var rows = report.ToList();

        string[] headers = ["Xodim", "Lavozim", "Oylik", "Oylar", "Hisoblangan", "Berilgan", "To'lovlar", "Qoldiq"];

        var cells = rows.Select(r => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of(r.TeacherName),
            ExcelExport.XlsxCell.Of(r.Position),
            ExcelExport.XlsxCell.Num(r.Salary),
            ExcelExport.XlsxCell.Num(r.Months),
            ExcelExport.XlsxCell.Num(r.Expected),
            ExcelExport.XlsxCell.Num(r.TotalPaid),
            ExcelExport.XlsxCell.Num(r.PaymentsCount),
            ExcelExport.XlsxCell.Num(r.Remaining),
        ]).ToList();

        IReadOnlyList<ExcelExport.XlsxCell> totals =
        [
            ExcelExport.XlsxCell.Of("Jami"),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Of(null),
            ExcelExport.XlsxCell.Num(rows.Sum(r => r.Expected)),
            ExcelExport.XlsxCell.Num(rows.Sum(r => r.TotalPaid)),
            ExcelExport.XlsxCell.Num(rows.Sum(r => r.PaymentsCount)),
            ExcelExport.XlsxCell.Num(rows.Sum(r => r.Remaining)),
        ];

        var bytes = ExcelExport.BuildTable("Maosh hisoboti", headers, cells, totals);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "xodimlar-maoshi.xlsx");
    }

    /// <summary>"yyyy-MM" → o'sha oyning birinchi kuni.</summary>
    private static DateOnly MonthStart(string month) =>
        new(int.Parse(month[..4]), int.Parse(month[5..7]), 1);

    /// <summary>"yyyy-MM" → o'sha oyning OXIRGI kuni (fevralda 28/29).</summary>
    private static DateOnly MonthEnd(string month)
    {
        var year = int.Parse(month[..4]);
        var m = int.Parse(month[5..7]);
        return new DateOnly(year, m, DateTime.DaysInMonth(year, m));
    }
}
