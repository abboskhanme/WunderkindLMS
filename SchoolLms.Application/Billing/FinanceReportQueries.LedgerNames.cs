using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  JURNAL SATRIGA ODAM O'QIYDIGAN NOM BERISH
//  (docs/modules/finance-parity.md §2.4 F4.02, §2.5 F5.03, §2.7 F7.03.)
// ===========================================================================
//
//  Drill-down oynasi bitta savolga javob beradi: "bu raqam qayerdan
//  chiqdi". Demak har satr yonida o'quvchi ismi, chek raqami yoki chiqim
//  toifasi turishi kerak. Moliya entity'larida navigatsiya xossalari yo'q
//  (`BillingModel.cs` FK'larni navigatsiyasiz e'lon qiladi), shuning uchun
//  ismlar PARTIYALAB o'qiladi — satr soniga qaramay uchta so'rov, N+1 yo'q.
//  Aynan shu naqsh `CashDayQueries` da ham ishlatilgan; farqi shundaki, bu
//  yerda HISOB-FAKTURA satrlari ham bezatiladi (P&L da pul emas, hisoblangan
//  daromad ko'rinadi).
//
//  STORNO — `ref_id` ASL yozuvni ko'rsatadi (`LedgerService.ReverseAsync`
//  ko'zguga originalning `ref_id` sini beradi), shuning uchun uchala lug'at
//  ham storno satri uchun ham ishlaydi; nom oldiga "STORNO — " qo'shiladi va
//  satr YASHIRILMAYDI (SPEC §4.1).

public sealed partial class FinanceReportQueries
{
    /// <summary>Bazadan shu ko'rinishda keladigan jurnal satri (bezashdan oldin).</summary>
    internal sealed record LedgerRowSource(
        long Id, DateOnly EntryDate, string Account, string Direction, decimal Amount,
        string RefType, Guid? RefId, string? Memo, DateTimeOffset CreatedAt);

    /// <summary>
    /// Jurnal satrlarini ekran qatorlariga aylantiradi: nom, o'quvchi, chek
    /// raqami, usul va muallif SERVERDA qo'shiladi.
    /// </summary>
    /// <param name="rows">Bezatiladigan satrlar.</param>
    /// <param name="signed">Satr katakka qanday kirganini beradigan funksiya
    /// (belgi qoidasi chaqiruvchida — P&amp;L va pul oqimida u har xil).</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    internal async Task<List<LedgerLineDto>> DecorateAsync(
        IReadOnlyList<LedgerRowSource> rows,
        Func<LedgerRowSource, decimal> signed,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(signed);

        if (rows.Count == 0) return [];

        var refIds = rows.Where(r => r.RefId is not null).Select(r => r.RefId!.Value).Distinct().ToList();
        var names = await NamesAsync(refIds, ct);

        return [.. rows.Select(row => ToLine(row, names, signed(row)))];
    }

    /// <summary>To'lov / chiqim / hisob-faktura nomlari — uchta so'rov, partiyalab.</summary>
    internal async Task<LedgerNames> NamesAsync(List<Guid> refIds, CancellationToken ct)
    {
        if (refIds.Count == 0) return new LedgerNames([], [], []);

        var payments = await (
            from p in db.Payments.AsNoTracking()
            where refIds.Contains(p.Id)
            join s in db.Students.AsNoTracking() on p.StudentId equals s.Id into students
            from s in students.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on p.CashierId equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new LedgerPaymentInfo(
                p.Id, p.ReceiptNo, p.Method,
                s == null ? null : s.FullName,
                u == null ? null : u.FullName))
            .ToListAsync(ct);

        var expenses = await (
            from e in db.Expenses.AsNoTracking()
            where refIds.Contains(e.Id)
            join u in db.Users.AsNoTracking() on e.CreatedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new LedgerExpenseInfo(
                e.Id, e.Category, e.Note, u == null ? null : u.FullName))
            .ToListAsync(ct);

        var invoices = await (
            from i in db.Invoices.AsNoTracking()
            where refIds.Contains(i.Id)
            join s in db.Students.AsNoTracking() on i.StudentId equals s.Id into students
            from s in students.DefaultIfEmpty()
            join c in db.FeeCategories.AsNoTracking() on i.CategoryId equals c.Id into categories
            from c in categories.DefaultIfEmpty()
            select new LedgerInvoiceInfo(
                i.Id, i.PeriodMonth,
                c == null ? null : c.Name,
                s == null ? null : s.FullName))
            .ToListAsync(ct);

        return new LedgerNames(
            payments.ToDictionary(p => p.PaymentId),
            expenses.ToDictionary(e => e.ExpenseId),
            invoices.ToDictionary(i => i.InvoiceId));
    }

    /// <summary>Bitta satrni ekran qatoriga aylantiradi.</summary>
    internal static LedgerLineDto ToLine(LedgerRowSource row, LedgerNames names, decimal signed)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(names);

        var isReversal = row.RefType == LedgerRefType.Reversal;

        LedgerPaymentInfo? payment = null;
        LedgerExpenseInfo? expense = null;
        LedgerInvoiceInfo? invoice = null;
        if (row.RefId is { } refId)
        {
            names.Payments.TryGetValue(refId, out payment);
            if (payment is null) names.Expenses.TryGetValue(refId, out expense);
            if (payment is null && expense is null) names.Invoices.TryGetValue(refId, out invoice);
        }

        var title = payment is not null
            ? payment.StudentName ?? "Noma'lum o'quvchi"
            : expense is not null
                ? ExpenseTitle(expense)
                : invoice is not null
                    ? InvoiceTitle(invoice)
                    : KindLabel(row.RefType);

        if (isReversal) title = $"STORNO — {title}";

        return new LedgerLineDto(
            EntryId: row.Id,
            EntryDate: row.EntryDate,
            Account: row.Account,
            Direction: row.Direction,
            Amount: row.Amount,
            Signed: signed,
            Kind: row.RefType,
            KindLabel: KindLabel(row.RefType),
            IsReversal: isReversal,
            RefId: row.RefId,
            Title: title,
            Person: payment?.StudentName ?? invoice?.StudentName,
            ActorName: payment?.CashierName ?? expense?.CreatedByName,
            ReceiptNo: payment?.ReceiptNo,
            Method: payment?.Method,
            Memo: row.Memo,
            CreatedAt: row.CreatedAt);
    }

    /// <summary>
    /// Chiqim satrining nomi. Toifa yopiq ro'yxatdan (<see cref="Accounts"/>),
    /// lekin noma'lum qiymat ekranni YIQITMASLIGI kerak — o'shanda kodning
    /// o'zi ko'rsatiladi (<c>CashDayQueries.ToMovement</c> bilan bir xil qoida).
    /// </summary>
    private static string ExpenseTitle(LedgerExpenseInfo expense) =>
        Accounts.IsExpenseCategory(expense.Category)
            ? MoneyFlowQueries.LabelFor(Accounts.ExpenseFor(expense.Category))
            : expense.Category;

    /// <summary>"O'qish to'lovi · Sen 2026 — Alisher" ko'rinishidagi nom.</summary>
    private static string InvoiceTitle(LedgerInvoiceInfo invoice)
    {
        var head = invoice.CategoryName ?? "Hisob-faktura";
        var month = invoice.PeriodMonth.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        return invoice.StudentName is null
            ? $"{head} · {month}"
            : $"{head} · {month} — {invoice.StudentName}";
    }

    /// <summary>Jurnal manba turi → o'zbekcha nom (<c>CashDayQueries</c> bilan bir xil).</summary>
    internal static string KindLabel(string refType) => refType switch
    {
        LedgerRefType.Payment => "To'lov",
        LedgerRefType.Invoice => "Hisob-faktura",
        LedgerRefType.Expense => "Chiqim",
        LedgerRefType.Salary => "Maosh",
        LedgerRefType.Reversal => "Storno",
        _ => "Boshqa harakat",
    };

    /// <summary>Uchta lug'at bitta joyda — chaqiruvchi ularni birga uzatadi.</summary>
    internal sealed record LedgerNames(
        Dictionary<Guid, LedgerPaymentInfo> Payments,
        Dictionary<Guid, LedgerExpenseInfo> Expenses,
        Dictionary<Guid, LedgerInvoiceInfo> Invoices);

    internal sealed record LedgerPaymentInfo(
        Guid PaymentId, long ReceiptNo, string Method, string? StudentName, string? CashierName);

    internal sealed record LedgerExpenseInfo(
        Guid ExpenseId, string Category, string? Note, string? CreatedByName);

    internal sealed record LedgerInvoiceInfo(
        Guid InvoiceId, DateOnly PeriodMonth, string? CategoryName, string? StudentName);
}
