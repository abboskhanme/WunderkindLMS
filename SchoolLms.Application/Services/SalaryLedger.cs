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
        // Faqat chorak (dars jadvali) davridagi oylar hisoblanadi — tashqaridagi oylarga maosh yo'q.
        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var byWeekday = (await TeacherSalaryCalc.LessonsByWeekdayAsync(db)).GetValueOrDefault(teacher.Id)
                        ?? new int[6];
        var plannedMonthly = TeacherSalaryCalc.WithBonus(
            TeacherSalaryCalc.Monthly(byWeekday.Sum(), teacher.Category, meta), teacher.BonusPct);
        // Kelmagan (absent) kunlar — oy bo'yicha guruhlangan.
        var absentByMonth = (await db.TeacherAttendances
                .Where(a => a.TeacherId == teacher.Id && a.Status == "absent" && a.Date.Length >= 7)
                .Select(a => a.Date).ToListAsync())
            .GroupBy(d => d[..7])
            .ToDictionary(g => g.Key, g => (IEnumerable<string>)g.ToList());

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

        var months = new List<MonthSalaryDto>();
        foreach (var month in TuitionService.MonthRange(startMonth, toMonth))
        {
            var expected = TeacherSalaryCalc.MonthlyForMonth(
                byWeekday, teacher.Category, meta, month, startDate,
                absentByMonth.GetValueOrDefault(month) ?? Enumerable.Empty<string>(), teacher.BonusPct, quarters);
            var paid = paidByMonth.GetValueOrDefault(month, 0m);
            var remaining = expected - paid;
            var status = remaining <= 0 ? "paid" : paid > 0 ? "partial" : "unpaid";
            months.Add(new MonthSalaryDto(month, expected, paid, remaining, status));
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
