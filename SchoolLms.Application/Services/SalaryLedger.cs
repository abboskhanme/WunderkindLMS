using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): jami belgilangan, berilgan, qoldiq
/// va har oyda qancha maosh berilgani. Admin moliya bo'limi ham, o'qituvchi ilovasi ham shu yagona
/// mantiqdan foydalanadi (ikki joyda farq qilib ketmasligi uchun).
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

        var payments = await db.FinanceTransactions
            .Where(t => t.TeacherId == teacher.Id && t.Direction == "expense" && t.Category == "salary"
                        && string.Compare(t.Date, fromDate) >= 0 && string.Compare(t.Date, toDate) <= 0)
            .OrderByDescending(t => t.Date).ToListAsync();

        var paidByMonth = payments
            .GroupBy(p => p.Date[..7])
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
        var paymentDtos = payments.Select(t => new PaymentDto(t.Date, t.Amount, t.Note, t.Month)).ToList();

        return new SalaryLedgerDto(
            teacher.Id, teacher.FullName, plannedMonthly,
            totalExpected, totalPaid, totalExpected - totalPaid,
            months, paymentDtos);
    }
}
