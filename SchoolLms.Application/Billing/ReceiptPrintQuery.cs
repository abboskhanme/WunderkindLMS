using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  58 mm termal chek — brauzer chop etadigan ma'lumot (2026-09-22).
// ===========================================================================
//
//  MIJOZ SO'ROVI (2026-09-22): maktab 58 mm XPrinter termal printer oldi.
//  To'lov qabul qilingach chop etish oynasi O'ZI ochilsin (PDF'ni avval
//  saqlamasdan), chek IKKI nusxada chiqsin (maktab + ota-ona) va unda qaysi
//  abonement (toifa × oy) ga qancha tushgani hamda o'sha oy TO'LIQ yopilgani
//  yoki qancha QOLGANI ko'rinsin.
//
//  BU FAYL NIMA QILADI: chekni JSON ko'rinishida yig'adi — brauzer uni
//  formatlamaydi, faqat chop etadi. Barcha matn (pul, sana, oy, to'lov turi)
//  PDF chek bilan BIR XIL `ReceiptText` yordamchilaridan olinadi, qatorlar esa
//  AYNAN `ReceiptService.BuildModel` dan. Ya'ni PDF va qog'oz chek bir xil
//  narsani aytadi; farqi faqat "yopildi / qoldi" ustunida.
//
//  NEGA `IReceiptService` GA METOD QO'SHILMADI: u interfeysni testlardagi
//  soxta implementatsiyalar (PaymentsTests.RecordingReceiptService) amalga
//  oshiradi — yangi a'zo ularni buzardi. Bu yerda holat ham, bog'liqlik ham
//  yo'q, shuning uchun statik so'rov klassi yetadi (SalaryPaymentQuery,
//  StudentBalanceQuery bilan bir xil naqsh).
//
//  PUL ARIFMETIKASI YO'Q: "qoldi" summasi `IInvoiceService.ListAsync`
//  qaytaradigan SERVERDA hisoblangan `InvoiceDto.Remaining`. Bu yerda
//  ayirish ham, qo'shish ham qilinmaydi.

/// <summary>
/// 58 mm chekning bitta qatori.
/// </summary>
/// <param name="Kind"><c>allocation</c> (abonement oyi) | <c>advance</c> (avans) | <c>refund</c> (storno).</param>
/// <param name="InvoiceId">Abonement oyi (hisob-faktura). Avans/qaytarishda null.</param>
/// <param name="CategoryName">Toifa nomi yoki avans/qaytarish yorlig'i.</param>
/// <param name="PeriodMonth">Oy (oyning 1-kuni). Avans/qaytarishda null.</param>
/// <param name="PeriodText"><c>2026-yil sentyabr</c>; oyga bog'lanmagan qatorda bo'sh satr.</param>
/// <param name="Amount">SHU to'lovdan shu qatorga tushgan summa.</param>
/// <param name="AmountText">Formatlangan summa (<see cref="ReceiptText.Money"/>).</param>
/// <param name="Remaining">Oyning chop etilgan paytdagi qoldig'i. Avans/qaytarishda null.</param>
/// <param name="IsClosed">Oy to'liq yopildimi. Avans/qaytarishda null.</param>
/// <param name="StatusText">"to'liq yopildi" | "qoldi: …" | "hisob bekor qilingan"; avans/qaytarishda null.</param>
public sealed record ReceiptPrintLineDto(
    string Kind,
    Guid? InvoiceId,
    string CategoryName,
    DateOnly? PeriodMonth,
    string PeriodText,
    decimal Amount,
    string AmountText,
    decimal? Remaining,
    bool? IsClosed,
    string? StatusText);

/// <summary>
/// 58 mm termal chek — <c>GET /api/receipts/{paymentId}</c> javobi.
/// <c>*Text</c> maydonlari chekka AYNAN shu ko'rinishda tushadi; xom
/// qiymatlar esa ekrandagi oldindan ko'rish va testlar uchun.
/// </summary>
public sealed record ReceiptPrintDto(
    Guid PaymentId,
    long ReceiptNo,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    DateTimeOffset ReceivedAt,
    string ReceivedAtText,
    string StudentId,
    string StudentName,
    string? ClassName,
    IReadOnlyList<ReceiptPrintLineDto> Lines,
    decimal Total,
    string TotalText,
    string Method,
    string MethodText,
    string CashierName,
    // Bu qatorning O'ZI storno (bekor qilish hujjati)mi.
    bool IsReversal,
    // Storno sanasi; null = chek kuchda.
    DateTimeOffset? CancelledAt,
    // "BEKOR QILINGAN" — faqat storno bo'lsa, aks holda null.
    string? CancelledStamp,
    // "Bekor qilingan sana: 12.09.2026 10:20" — faqat storno bo'lsa.
    string? CancelledAtText,
    // "Qoldi" qaysi lahzaga to'g'ri ekanini aytadi (pastdagi izoh).
    DateTimeOffset PrintedAt,
    string PrintedAtText,
    // Chek pastidagi QR (2026-09-24): tekshirish sahifasi manzili va uning PNG rasmi (data URL).
    string? VerifyUrl = null,
    string? QrDataUrl = null);

/// <summary>
/// 58 mm termal chekni yig'adi. Faqat O'QIYDI — hech narsa yozmaydi.
/// </summary>
public static class ReceiptPrintQuery
{
    /// <summary>Qator turlari (<see cref="ReceiptPrintLineDto.Kind"/>).</summary>
    public const string KindAllocation = "allocation";
    public const string KindAdvance = "advance";
    public const string KindRefund = "refund";

    /// <summary>Oyning qoldig'i nol — abonement oyi to'liq yopildi.</summary>
    public const string ClosedLabel = "to'liq yopildi";

    /// <summary>Hisob-faktura keyinroq bekor qilingan (void) — "qoldi" deyish noto'g'ri bo'lardi.</summary>
    public const string VoidLabel = "hisob bekor qilingan";

    /// <summary>Oyda hali to'lanmagan qism: <c>qoldi: 300 000 so'm</c>.</summary>
    public static string RemainingLabel(decimal remaining) => $"qoldi: {ReceiptText.Money(remaining)}";

    /// <summary>
    /// Chek ma'lumotini bazadan yig'adi. To'lov yo'q bo'lsa null
    /// (controller buni 404 ga o'giradi).
    /// </summary>
    public static async Task<ReceiptPrintDto?> GetAsync(
        Guid paymentId,
        IPaymentService payments,
        IInvoiceService invoices,
        IAppDbContext db,
        CancellationToken ct = default,
        string? verifyBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(db);

        // To'lov — PDF bilan bir xil manbadan (P1-11 DTO'si).
        var payment = await payments.GetAsync(paymentId, ct);
        if (payment is null) return null;

        var school = await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var cancelledAt = await ReceiptService.CancelledAtAsync(db, paymentId, ct);
        var model = ReceiptService.BuildModel(payment, school, cancelledAt);

        // Sinf — bitta skalyar so'rov; chekda ismning yonida turadi (ikki bir
        // xil ismli o'quvchini ajratishga yordam beradi).
        var className = await db.Students.AsNoTracking()
            .Where(s => s.Id == payment.StudentId)
            .Select(s => s.ClassName)
            .FirstOrDefaultAsync(ct);

        var receipt = Build(payment, model, className, await InvoicesAsync(payment, invoices, ct), AppClock.NowInstant);
        var verifyUrl = await ReceiptService.VerifyUrlAsync(db, paymentId, verifyBaseUrl, ct);
        return verifyUrl is null ? receipt : receipt with { VerifyUrl = verifyUrl, QrDataUrl = ReceiptQr.PngDataUrl(verifyUrl) };
    }

    /// <summary>
    /// <b>Sof funksiya</b> — bazasiz, shuning uchun to'g'ridan-to'g'ri sinaladi.
    /// Qatorlar <paramref name="model"/> dan (PDF bilan bir xil), holat esa
    /// <paramref name="invoices"/> dan olinadi.
    /// </summary>
    /// <param name="payment">To'lov.</param>
    /// <param name="model"><see cref="ReceiptService.BuildModel"/> natijasi.</param>
    /// <param name="className">O'quvchining sinfi (bo'sh bo'lsa chop etilmaydi).</param>
    /// <param name="invoices">
    /// To'lov taqsimlangan hisob-fakturalar — SERVERDA hisoblangan
    /// <c>Remaining</c> bilan. Ro'yxatda yo'q qatorning holati bo'sh qoladi
    /// (taxmin yozilmaydi).
    /// </param>
    /// <param name="printedAt">Chek yig'ilgan lahza.</param>
    public static ReceiptPrintDto Build(
        PaymentDto payment,
        ReceiptModel model,
        string? className,
        IReadOnlyCollection<InvoiceDto> invoices,
        DateTimeOffset printedAt)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(invoices);

        var byId = invoices
            .GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = model.Lines.Select(line => ToLine(line, byId)).ToList();

        return new ReceiptPrintDto(
            PaymentId: payment.Id,
            ReceiptNo: model.ReceiptNo,
            SchoolName: model.SchoolName,
            SchoolAddress: model.SchoolAddress,
            SchoolPhone: model.SchoolPhone,
            ReceivedAt: model.ReceivedAt,
            ReceivedAtText: ReceiptText.DateTime(model.ReceivedAt),
            StudentId: payment.StudentId,
            StudentName: model.StudentName,
            ClassName: string.IsNullOrWhiteSpace(className) ? null : className.Trim(),
            Lines: lines,
            Total: model.Total,
            TotalText: ReceiptText.Money(model.Total),
            Method: model.Method,
            MethodText: ReceiptText.MethodLabel(model.Method),
            CashierName: model.CashierName,
            IsReversal: payment.ReversalOf is not null,
            CancelledAt: model.CancelledAt,
            CancelledStamp: model.CancelledAt is null ? null : ReceiptText.CancelledStamp,
            CancelledAtText: model.CancelledAt is { } at
                ? $"Bekor qilingan sana: {ReceiptText.DateTime(at)}"
                : null,
            PrintedAt: printedAt,
            PrintedAtText: ReceiptText.DateTime(printedAt));
    }

    private static ReceiptPrintLineDto ToLine(ReceiptLine line, IReadOnlyDictionary<Guid, InvoiceDto> invoices)
    {
        var amountText = ReceiptText.Money(line.Amount);

        if (line.InvoiceId is not { } invoiceId)
        {
            // Avans yoki storno'dagi qaytarilgan summa: oyga bog'lanmagan,
            // "yopildi/qoldi" savoli yo'q. Yorliqni `BuildModel` tanlagan.
            var kind = line.CategoryName == ReceiptText.RefundLine ? KindRefund : KindAdvance;
            return new ReceiptPrintLineDto(
                kind, null, line.CategoryName, null, string.Empty,
                line.Amount, amountText, null, null, null);
        }

        // DIQQAT: qoldiq — CHOP ETILGAN PAYTDAGI holat, to'lov paytidagi emas.
        // Chek keyinroq qayta chop etilsa (shu oyga yana to'lov bo'lgan yoki shu
        // to'lov storno qilingan bo'lsa) "qoldi" boshqacha chiqadi — shuning
        // uchun chekda "Chop etildi: …" sanasi ham turadi. Summa `InvoiceDto`
        // dan (serverda hisoblangan), bu yerda arifmetika yo'q.
        var invoice = invoices.GetValueOrDefault(invoiceId);
        var (remaining, closed, status) = invoice switch
        {
            null => ((decimal?)null, (bool?)null, (string?)null),
            { Status: InvoiceStatus.Void } => (invoice.Remaining, false, VoidLabel),
            { Remaining: <= 0m } => (invoice.Remaining, true, ClosedLabel),
            _ => (invoice.Remaining, false, RemainingLabel(invoice.Remaining)),
        };

        return new ReceiptPrintLineDto(
            KindAllocation, invoiceId, line.CategoryName, line.PeriodMonth,
            ReceiptText.Period(line.PeriodMonth), line.Amount, amountText,
            remaining, closed, status);
    }

    /// <summary>
    /// To'lov taqsimlangan hisob-fakturalar — <see cref="IInvoiceService.ListAsync"/>
    /// orqali (qoldiqni u hisoblaydi). O'quvchi va oylar oralig'i bilan
    /// toraytiriladi, keyin FAQAT shu to'lovning hisob-fakturalari qoladi.
    /// </summary>
    private static async Task<IReadOnlyCollection<InvoiceDto>> InvoicesAsync(
        PaymentDto payment, IInvoiceService invoices, CancellationToken ct)
    {
        if (payment.Allocations.Count == 0) return [];

        var ids = payment.Allocations.Select(a => a.InvoiceId).ToHashSet();
        var from = payment.Allocations.Min(a => a.PeriodMonth);
        var to = payment.Allocations.Max(a => a.PeriodMonth);

        var rows = await invoices.ListAsync(
            new InvoiceQuery(StudentId: payment.StudentId, FromMonth: from, ToMonth: to), ct);

        return [.. rows.Where(i => ids.Contains(i.Id))];
    }
}
