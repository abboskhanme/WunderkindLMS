using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): jami belgilangan, berilgan, qoldiq
/// va har oyda qancha maosh berilgani. Admin moliya bo'limi ham, o'qituvchi ilovasi ham shu yagona
/// mantiqdan foydalanadi (ikki joyda farq qilib ketmasligi uchun).
///
/// <para>
/// <b>P1-21:</b> berilgan maosh endi <c>expenses</c> dan o'qiladi
/// (<see cref="SalaryPaymentQuery"/>) — eski <c>finance_transactions</c> o'chdi.
/// Faqat JURNALGA TUSHGAN va storno qilinmagan chiqim "berilgan" hisoblanadi:
/// tasdiq kutayotgan chiqim hali berilmagan pul (SPEC §4.5).
/// </para>
/// </summary>
public static class SalaryLedger
{
    public static async Task<SalaryLedgerDto> BuildAsync(
        IAppDbContext db, Teacher teacher, string? from, string? to)
    {
        // Maosh o'quv yili boshidan hisoblanadi (yanvardan emas) — choraklardagi eng erta oydan.
        var fromMonth = string.IsNullOrEmpty(from)
            ? await TuitionService.AcademicYearStartMonthAsync(db) : from[..7];
        var toMonth = string.IsNullOrEmpty(to) ? TuitionService.CurrentMonth() : to[..7];

        // Oylik maosh — dars jadvali + toifa narxidan; har oy DAVOMATga moslanadi (kelmagan kun chegiriladi).
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var byWeekday = (await TeacherSalaryCalc.LessonsByWeekdayAsync(db)).GetValueOrDefault(teacher.Id)
                        ?? new int[6];
        var plannedMonthly = TeacherSalaryCalc.WithBonus(
            TeacherSalaryCalc.Monthly(byWeekday.Sum(), teacher.Category, meta), teacher.BonusPct);

        // O'qituvchi ishga kirgan KUN (yangi maydon yoki eski oy-01). Birinchi oy shu kundan qisman.
        var startDate = TeacherSalaryCalc.StartDateOf(teacher);
        var teacherStartMonth = startDate is { Length: >= 7 } ? startDate[..7] : fromMonth;
        // Oylik o'qituvchi boshlagan oydan hisoblanadi — undan oldingi oylar uchun qarz yozilmaydi.
        var startMonth = string.CompareOrdinal(teacherStartMonth, fromMonth) > 0 ? teacherStartMonth : fromMonth;

        var fromDate = $"{startMonth}-01";
        var toDate = $"{toMonth}-31";

        var payments = await new SalaryPaymentQuery(db).ForTeacherAsync(
            teacher.Id, ParseDate(fromDate), ParseDate(toDate));

        var paidByMonth = payments
            .GroupBy(p => p.Month)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // One calculation with the salary table (TeacherPay): + certificates, allowances, bonuses, fines (2026-10-15).
        var pay = await TeacherPayContext.LoadAsync(db, teacher.Id);
        var months = new List<MonthSalaryDto>();
        foreach (var month in TuitionService.MonthRange(startMonth, toMonth))
        {
            var p = pay.For(teacher, month);
            var expected = p.Total;
            var paid = paidByMonth.GetValueOrDefault(month, 0m);
            var remaining = expected - paid;
            var status = remaining <= 0 ? "paid" : paid > 0 ? "partial" : "unpaid";
            months.Add(new MonthSalaryDto(month, expected, paid, remaining, status,
                p.LessonPay, p.UstamaAmount, p.AllowanceAmount, p.Bonus, p.Penalty, p.SubstitutionLessons));
        }

        var totalExpected = months.Sum(m => m.Expected);
        var totalPaid = payments.Sum(p => p.Amount);
        var paymentDtos = payments
            .Select(t => new PaymentDto(t.OnDate.ToString("yyyy-MM-dd"), t.Amount, t.Note, t.Month))
            .ToList();

        return new SalaryLedgerDto(
            teacher.Id, teacher.FullName, plannedMonthly,
            totalExpected, totalPaid, totalExpected - totalPaid,
            months, paymentDtos);
    }

    /// <summary>
    /// O'qituvchi bo'lmagan xodimning (role="staff") maosh daftari — javob shakli o'qituvchinikining
    /// aynan o'zi. Har oy kerakli summa = belgilangan oylik (<see cref="StaffSalaryCalc"/>:
    /// birinchi oy qisman, undan oldin 0); berilgan — <c>expenses.employee_user_id</c> bo'yicha.
    /// </summary>
    public static async Task<SalaryLedgerDto> BuildForStaffAsync(
        IAppDbContext db, AppUser staff, string? from, string? to)
    {
        var fromMonth = string.IsNullOrEmpty(from)
            ? await TuitionService.AcademicYearStartMonthAsync(db) : from[..7];
        var toMonth = string.IsNullOrEmpty(to) ? TuitionService.CurrentMonth() : to[..7];

        var startDate = StaffSalaryCalc.StartDateOf(staff);
        var startMonth = StaffSalaryCalc.FirstMonth(startDate, fromMonth);

        var payments = await new SalaryPaymentQuery(db).ForEmployeeAsync(
            staff.Id, ParseDate($"{startMonth}-01"), ParseDate($"{toMonth}-31"));
        var paidByMonth = payments
            .GroupBy(p => p.Month)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // Moliya → Bonus / Jarima go into the month they are written for (2026-10-15).
        var adjustments = await PayrollAdjustmentTotals.ForStaffAsync(db, staff.Id);
        var months = new List<MonthSalaryDto>();
        foreach (var month in TuitionService.MonthRange(startMonth, toMonth))
        {
            var basePay = StaffSalaryCalc.MonthlyForMonth(staff.Salary, month, startDate);
            var adj = adjustments.GetValueOrDefault((staff.Id, month));
            var expected = PayrollAdjustmentTotals.Apply(basePay, adj);
            var paid = paidByMonth.GetValueOrDefault(month, 0m);
            var remaining = expected - paid;
            var status = remaining <= 0 ? "paid" : paid > 0 ? "partial" : "unpaid";
            months.Add(new MonthSalaryDto(month, expected, paid, remaining, status,
                basePay, Bonus: adj.Bonus, Penalty: adj.Penalty));
        }

        var totalExpected = months.Sum(m => m.Expected);
        var totalPaid = payments.Sum(p => p.Amount);
        var paymentDtos = payments
            .Select(t => new PaymentDto(t.OnDate.ToString("yyyy-MM-dd"), t.Amount, t.Note, t.Month))
            .ToList();

        return new SalaryLedgerDto(
            staff.Id, staff.FullName, staff.Salary,
            totalExpected, totalPaid, totalExpected - totalPaid,
            months, paymentDtos);
    }

    /// <summary>
    /// Eski oraliq satrini (<c>"yyyy-MM-31"</c> ham uchraydi) <c>DateOnly</c> ga
    /// o'giradi. Oyning oxirgi kuni 31 bo'lmasa ham chegara oy oxiriga siqiladi —
    /// fevral uchun "2026-02-31" haqiqiy sana emas, lekin ma'nosi "oy oxirigacha".
    /// </summary>
    private static DateOnly? ParseDate(string value)
    {
        if (value.Length < 10) return null;
        if (!int.TryParse(value[..4], out var year) || !int.TryParse(value[5..7], out var month))
            return null;
        if (!int.TryParse(value[8..10], out var day)) return null;
        return new DateOnly(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));
    }
}
