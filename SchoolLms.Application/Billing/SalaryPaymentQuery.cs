using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  O'qituvchiga berilgan maosh — `expenses` dan o'qiladi. Vazifa: P1-21.
// ===========================================================================
//
//  NIMA O'RNIGA KELDI
//  ------------------
//  Maosh eski `finance_transactions` jadvalidan (`direction = 'expense'`,
//  `category = 'salary'`, `teacher_id`) o'qilardi. U jadval P1-21 da o'chdi va
//  u JURNALGA UMUMAN TUSHMAS EDI — ya'ni P&L uchun o'sha pul mavjud emasdi.
//  Endi maosh boshqa har qanday chiqim kabi: `expenses` qatori +
//  `debit expense:salary / credit cash|bank` juftligi (`ExpenseService`).
//
//  NEGA `expenses.teacher_id`
//  --------------------------
//  Maosh hisoboti to'rt joyda kerak (maosh hisoboti, o'qituvchi kartochkasi,
//  maosh jadvali, o'qituvchi portali) va hammasi bitta savolga javob beradi:
//  "falonchi falon oyda qancha oldi". Izoh MATNIDAN o'qish bog'lanish emas,
//  taxmin. Shuning uchun migratsiya `expenses` ga nullable `teacher_id`
//  (FK → `teachers.id`) qo'shdi: maosh toifasidan boshqa chiqimlarda u null.
//
//  FAQAT JURNALGA TUSHGAN VA STORNO QILINMAGAN PUL SANALADI
//  --------------------------------------------------------
//  `expenses` qatorining o'zi hali "pul berildi" degani emas:
//    · chegaradan yuqori chiqim direktor tasdig'igacha jurnalga TUSHMAYDI
//      (SPEC §4.5) — u hali berilmagan pul;
//    · storno qilingan chiqim berilgan, keyin qaytarilgan pul.
//  Ikkalasini ham "berilgan maosh" deb hisoblasak, o'qituvchining qoldig'i
//  kam chiqardi. Holat `expenses` da ustun sifatida saqlanmaydi (u jurnaldan
//  keltirib chiqariladi — `ExpenseStatus`), shuning uchun filtr ham jurnal
//  bo'yicha EXISTS: `ix_ledger_entries_ref_type_ref_id` indeksidan foydalanadi.
//
//  UNUMDORLIK
//  ----------
//  Har metod BITTA so'rov yuboradi; o'qituvchilar soniga bog'liq emas.
//  Maosh hisoboti (barcha o'qituvchilar) <see cref="ForAllAsync"/> ni bir
//  marta chaqiradi — siklda emas.

/// <summary>
/// Bitta maosh to'lovi (jurnalga tushgan, storno qilinmagan chiqim qatori).
/// </summary>
/// <param name="TeacherId">Maosh oluvchi: o'qituvchi so'rovlarida <c>teachers.id</c>,
/// xodim so'rovlarida (<see cref="SalaryPaymentQuery.ForEmployeeAsync"/>) <c>users.id</c>.</param>
/// <param name="OnDate">Buxgalteriya sanasi — maosh qaysi kunga yozilgan.</param>
/// <param name="Month">Sananing oyi (<c>"yyyy-MM"</c>) — eski hisobotlar oyni
/// AYNAN shu ko'rinishda kutadi.</param>
public sealed record SalaryPaymentRow(
    Guid Id, string TeacherId, DateOnly OnDate, string Month, decimal Amount, string? Note);

/// <summary>
/// O'qituvchilarga berilgan maoshlar — <c>expenses</c> jadvalidan, faqat
/// jurnalga tushgan va storno qilinmagan qatorlar. Batafsil: fayl boshidagi izoh.
/// O'qituvchi bo'lmagan xodimlar (<c>expenses.employee_user_id</c>) — xuddi shu
/// qoidalar bilan <see cref="ForEmployeeAsync"/> / <see cref="ForAllEmployeesAsync"/>.
/// </summary>
public sealed class SalaryPaymentQuery(IAppDbContext db)
{
    /// <summary><c>expenses.category</c> ning maosh qiymati (<see cref="Accounts.ExpenseCategories"/>).</summary>
    public const string SalaryCategory = "salary";

    /// <summary>Bitta o'qituvchiga berilgan maoshlar (yangisidan eskisiga).</summary>
    public Task<IReadOnlyList<SalaryPaymentRow>> ForTeacherAsync(
        string teacherId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teacherId);
        return ListAsync(teacherId, from, to, ct);
    }

    /// <summary>Barcha o'qituvchilarga berilgan maoshlar (maosh hisoboti uchun, bitta so'rov).</summary>
    public Task<IReadOnlyList<SalaryPaymentRow>> ForAllAsync(
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default) =>
        ListAsync(null, from, to, ct);

    /// <summary>
    /// Bitta xodimga (<c>users.id</c>, role="staff") berilgan maoshlar — <c>expenses.employee_user_id</c>
    /// bo'yicha, o'qituvchi bilan AYNAN bir xil qoidalar (jurnalga tushgan, storno qilinmagan).
    /// </summary>
    public Task<IReadOnlyList<SalaryPaymentRow>> ForEmployeeAsync(
        string userId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return ListAsync(userId, from, to, ct, employees: true);
    }

    /// <summary>Barcha xodimlarga berilgan maoshlar (maosh hisoboti uchun, bitta so'rov).</summary>
    public Task<IReadOnlyList<SalaryPaymentRow>> ForAllEmployeesAsync(
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default) =>
        ListAsync(null, from, to, ct, employees: true);

    /// <param name="employees"><c>false</c> — <c>teacher_id</c> bo'yicha, <c>true</c> — <c>employee_user_id</c> bo'yicha.</param>
    private async Task<IReadOnlyList<SalaryPaymentRow>> ListAsync(
        string? recipientId, DateOnly? from, DateOnly? to, CancellationToken ct, bool employees = false)
    {
        var q = db.Expenses.AsNoTracking().Where(e => e.Category == SalaryCategory);

        q = employees
            ? q.Where(e => e.EmployeeUserId != null)
            : q.Where(e => e.TeacherId != null);

        if (recipientId is not null)
            q = employees
                ? q.Where(e => e.EmployeeUserId == recipientId)
                : q.Where(e => e.TeacherId == recipientId);
        if (from is { } f) q = q.Where(e => e.OnDate >= f);
        if (to is { } t) q = q.Where(e => e.OnDate <= t);

        // Jurnalga tushgan (pul haqiqatan chiqqan) va storno qilinmagan.
        q = q.Where(e => db.LedgerEntries.Any(
                l => l.RefType == LedgerRefType.Expense && l.RefId == e.Id)
            && !db.LedgerEntries.Any(
                l => l.RefType == LedgerRefType.Reversal && l.RefId == e.Id));

        var rows = await q
            .OrderByDescending(e => e.OnDate)
            .ThenByDescending(e => e.CreatedAt)
            .Select(e => new { e.Id, e.TeacherId, e.EmployeeUserId, e.OnDate, e.Amount, e.Note })
            .ToListAsync(ct);

        return [.. rows.Select(e => new SalaryPaymentRow(
            e.Id, (employees ? e.EmployeeUserId : e.TeacherId)!, e.OnDate, e.OnDate.ToString("yyyy-MM"),
            e.Amount, e.Note))];
    }
}
