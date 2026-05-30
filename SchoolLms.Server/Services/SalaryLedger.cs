using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;

namespace SchoolLms.Server.Services;

/// <summary>
/// O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): jami belgilangan, berilgan, qoldiq
/// va har oyda qancha maosh berilgani. Admin moliya bo'limi ham, o'qituvchi ilovasi ham shu yagona
/// mantiqdan foydalanadi (ikki joyda farq qilib ketmasligi uchun).
/// </summary>
public static class SalaryLedger
{
    public static async Task<SalaryLedgerDto> BuildAsync(
        AppDbContext db, Teacher teacher, string? from, string? to)
    {
        var fromMonth = string.IsNullOrEmpty(from) ? $"{DateTime.Now.Year:D4}-01" : from[..7];
        var toMonth = string.IsNullOrEmpty(to) ? TuitionService.CurrentMonth() : to[..7];

        // Oylik o'qituvchi boshlagan oydan hisoblanadi — undan oldingi oylar uchun qarz yozilmaydi.
        var startMonth = !string.IsNullOrEmpty(teacher.SalaryStartMonth)
                         && string.CompareOrdinal(teacher.SalaryStartMonth, fromMonth) > 0
            ? teacher.SalaryStartMonth
            : fromMonth;

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
            var paid = paidByMonth.GetValueOrDefault(month, 0m);
            var remaining = teacher.Salary - paid;
            var status = remaining <= 0 ? "paid" : paid > 0 ? "partial" : "unpaid";
            months.Add(new MonthSalaryDto(month, teacher.Salary, paid, remaining, status));
        }

        var totalExpected = teacher.Salary * months.Count;
        var totalPaid = payments.Sum(p => p.Amount);
        var paymentDtos = payments.Select(t => new PaymentDto(t.Date, t.Amount, t.Note, t.Month)).ToList();

        return new SalaryLedgerDto(
            teacher.Id, teacher.FullName, teacher.Salary,
            totalExpected, totalPaid, totalExpected - totalPaid,
            months, paymentDtos);
    }
}
