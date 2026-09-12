using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

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
//  RUXSAT TEGILMAGAN: `[AdminPerm("finance")]` — bu hisobotni ko'radigan
//  xodimlar to'plami o'zgarmadi. Yangi hisobotlar (§4.3, faqat admin/direktor)
//  `FinanceReportsController` da, boshqa darvoza ostida.
// ===========================================================================

/// <summary>
/// O'qituvchilarga berilgan maoshlar hisoboti. Batafsil: fayl boshidagi izoh.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance")]
[Route("api/admin/finance")]
public class FinanceController(AppDbContext db) : ControllerBase
{
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
    /// </summary>
    [HttpGet("salary-report")]
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

        return teachers.Select(te =>
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
                monthList.Count, expected, expected - totalPaid);
        }).ToList();
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
