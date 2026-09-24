using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;

namespace SchoolLms.Server.Controllers;

/// <summary>Chekdagi QR ochadigan ma'lumot — faqat chekning o'zida bor narsalar (id'lar, balans YO'Q).</summary>
public sealed record PublicReceiptDto(
    long ReceiptNo,
    string SchoolName,
    string ReceivedAtText,
    string StudentName,
    string? ClassName,
    IReadOnlyList<PublicReceiptLineDto> Lines,
    string TotalText,
    string MethodText,
    string CashierName,
    // "valid" — chek kuchda; "cancelled" — to'lov keyin storno qilingan; "reversal" — bu chekning o'zi storno.
    string Status,
    string? CancelledAtText);

public sealed record PublicReceiptLineDto(string CategoryName, string PeriodText, string AmountText, string? StatusText);

/// <summary>
/// Chek QR kodi (mijoz, 2026-09-24): "chekdan scaner qilib to'lov haqida malumot ham ololsin".
///
/// <para>
/// <b>Kirish — faqat token bilan.</b> <c>/api/public/receipts/{token}</c> login talab qilmaydi (QR'ni ota-ona
/// telefoni ochadi), lekin to'lovni FAQAT 128 bitli tasodifiy <c>payments.receipt_token</c> bilan topadi — chek
/// raqami yoki id bilan emas, shuning uchun boshqa cheklarni "sanab" chiqib bo'lmaydi. Javobda chekda bosilgan
/// ma'lumotdan ortiq narsa yo'q, va IP bo'yicha tezlik chegarasi bor (<c>survey-read</c>: 60/daqiqa).
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/receipts")]
public partial class PublicReceiptController(
    IPaymentService payments,
    IInvoiceService invoices,
    IAppDbContext db) : ControllerBase
{
    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TokenShape();

    [HttpGet("{token}")]
    [EnableRateLimiting("survey-read")]
    public async Task<ActionResult<PublicReceiptDto>> Get(string token, CancellationToken ct)
    {
        var notFound = NotFound(new { message = "Chek topilmadi" });
        if (!TokenShape().IsMatch(token)) return notFound;

        var paymentId = await db.Payments.AsNoTracking()
            .Where(p => p.ReceiptToken == token).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (paymentId is null) return notFound;

        var r = await ReceiptPrintQuery.GetAsync(paymentId.Value, payments, invoices, db, ct);
        if (r is null) return notFound;

        var status = r.IsReversal ? "reversal" : r.CancelledAt is null ? "valid" : "cancelled";
        return new PublicReceiptDto(
            r.ReceiptNo, r.SchoolName, r.ReceivedAtText, r.StudentName, r.ClassName,
            [.. r.Lines.Select(l => new PublicReceiptLineDto(l.CategoryName, l.PeriodText, l.AmountText, l.StatusText))],
            r.TotalText, r.MethodText, r.CashierName, status, r.CancelledAtText);
    }
}
