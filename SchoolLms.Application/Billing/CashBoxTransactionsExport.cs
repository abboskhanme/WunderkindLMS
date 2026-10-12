using System.Globalization;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  The cash-box journal as a real .xlsx (client, 2026-10-12).
//
//  It used to be a CSV built in the browser. Excel on the school's computers opened it as one
//  column of quoted text with broken letters ("в„–", "o'g'li" garbled), every row showed 05:00
//  (the date-only field read as UTC midnight), and the "Kassir" column repeated the pupil's name.
//  Now the server writes the file: real date-time text in school time, the amount as a number
//  Excel can sum, and the cashier who recorded the row.
//
//  The rows are exactly the ones on screen: the client sends the ids of the rows it shows (its own
//  method/type/pupil/class filters apply on top of the server's), and the file keeps that order.
// ===========================================================================

/// <summary>Builds the cash-box journal workbook from rows the journal query already produced.</summary>
public static class CashBoxTransactionsExport
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "№", "Sana", "Kassa", "Kim", "Shartnoma raqami", "Miqdor", "Tranzaksiya",
        "Tranzaksiya turi", "To'lov usuli", "Holati", "Kassir", "Izoh", "Sabab",
    ];

    private static readonly IReadOnlyList<double> Widths = [6, 17, 34, 34, 16, 15, 17, 26, 25, 18, 26, 40, 30];

    /// <summary>The workbook: a title line with the period and box, then one row per transaction.</summary>
    public static byte[] Build(
        IReadOnlyList<CashBoxTransactionRowDto> rows, DateOnly? from, DateOnly? to, string? boxName)
    {
        var period = (from, to) switch
        {
            ({ } f, { } t) when f == t => f.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ({ } f, { } t) => $"{f:dd.MM.yyyy} – {t:dd.MM.yyyy}",
            ({ } f, null) => $"{f:dd.MM.yyyy} dan",
            (null, { } t) => $"{t:dd.MM.yyyy} gacha",
            _ => "barcha davr",
        };
        var title = $"Kassa tranzaksiyalari · {period} · {boxName ?? "Barcha kassalar"}";

        var table = rows.Select((r, i) => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of((i + 1).ToString(CultureInfo.InvariantCulture)),
            ExcelExport.XlsxCell.Of(AppClock.ToLocal(r.CreatedAt).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)),
            ExcelExport.XlsxCell.Of(r.ToBoxName is null ? r.BoxName : $"{r.BoxName} → {r.ToBoxName}"),
            ExcelExport.XlsxCell.Of(r.Who),
            ExcelExport.XlsxCell.Of(r.ContractNo),
            ExcelExport.XlsxCell.Num(r.Amount),
            ExcelExport.XlsxCell.Of(KindLabel(r.Kind)),
            ExcelExport.XlsxCell.Of(r.RecipientName is null ? r.TransactionTypeName : $"{r.TransactionTypeName} · {r.RecipientName}"),
            ExcelExport.XlsxCell.Of(MethodLabel(r.Method)),
            ExcelExport.XlsxCell.Of(StatusLabel(r.Status)),
            ExcelExport.XlsxCell.Of(r.CashierName),
            ExcelExport.XlsxCell.Of(r.Note),
            ExcelExport.XlsxCell.Of(r.CancelReason),
        ]).ToList();

        return ExcelExport.BuildTables(
            [new ExcelExport.TableSpec("Kassa", Headers, table, Title: title, Widths: Widths)]);
    }

    /// <summary>The file name: <c>kassa-tranzaksiyalari_2026-10-05_2026-10-05.xlsx</c>.</summary>
    public static string FileName(DateOnly? from, DateOnly? to) =>
        $"kassa-tranzaksiyalari_{from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "boshi"}"
        + $"_{to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "oxiri"}.xlsx";

    // The same words as the cash-box screen (schoollms.client/src/pages/cashier/format.ts).

    public static string MethodLabel(string method) => method switch
    {
        PaymentMethod.Cash => "Naqd",
        PaymentMethod.Card => "Karta (terminal)",
        PaymentMethod.Transfer => "Bank o'tkazmasi",
        PaymentMethod.Online => "Onlayn (Payme/Click/Uzum)",
        _ => method,
    };

    public static string KindLabel(string kind) => kind switch
    {
        CashBoxTransactionKind.PayIn => "Kirim",
        CashBoxTransactionKind.PayOut => "Chiqim",
        CashBoxTransactionKind.Transfer => "Ko'chirish",
        CashBoxTransactionKind.Exchange => "Ayirboshlash",
        CashBoxLedgerKind.StudentPayment => "O'quvchi to'lovi",
        CashBoxLedgerKind.Expense => "Xarajat",
        CashBoxLedgerKind.Refund => "Qaytarim",
        _ => kind,
    };

    public static string StatusLabel(string status) => status switch
    {
        CashBoxRowStatus.Posted => "Bajarildi",
        CashBoxRowStatus.Cancelled => "Bekor qilindi",
        CashBoxRowStatus.Reversal => "Storno",
        CashBoxRowStatus.Pending => "Tasdiq kutilmoqda",
        CashBoxRowStatus.Returned => "Qaytarildi",
        _ => status,
    };
}
