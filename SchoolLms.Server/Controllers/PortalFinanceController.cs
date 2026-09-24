using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Ota-ona / o'quvchi uchun moliya ko'rinishi — SPEC §3.7, §4.7. Vazifa: P1-19.
// ===========================================================================
//
//  NEGA ALOHIDA ENDPOINT KERAK BO'LDI
//  ----------------------------------
//  `IInvoiceService.ForStudentAsync` o'quvchining butun kartochkasini beradi,
//  lekin uni HTTP orqali ochadigan endpoint yo'q edi: `/api/admin/billing/*`
//  butunlay `Roles.FinanceStaff` (admin + direktor) ostida, chek PDF'i esa
//  `FinanceAction.AcceptPayment` (kassir + admin + direktor) ostida. Ya'ni
//  ota-ona na hisob-fakturasini, na o'z chekini ko'ra olardi — 403.
//
//  Shu bilan birga ota-onaga ADMIN javobini ko'rsatib bo'lmaydi: unda uuid,
//  kassir id'si, smena id'si va boshqa ichki maydonlar bor (qabul mezoni:
//  ekranda ichki id ko'rinmaydi). Shuning uchun bu yerda ALOHIDA, tor javob
//  shakli bor — faqat ota-ona ko'rishi kerak bo'lgan maydonlar.
//
//  PULNI SERVER HISOBLAYDI
//  -----------------------
//  Oy bo'yicha jamlar (`Amount`, `Discount`, `Payable`, `Paid`, `Remaining`)
//  shu yerda `decimal` da qo'shiladi. Frontend hech narsani `reduce` qilmaydi
//  — `types/index.ts` dagi muzlatilgan shartnoma ham shuni talab qiladi
//  (JavaScript `number` — float64, tiyin yo'qoladi).
//
//  DI GA TEGILMAGAN
//  ----------------
//  Controller `AppDbContext` dan xizmatlarni O'ZI yasaydi — aynan
//  `FinanceReportsController` dagidek (`docs/PENDING_WIRING.md` §3a). Sabab:
//  `Program.cs` — umumiy fayl va P1-15 ning ishi; unga tegmasdan ham bu ikki
//  endpoint tirik bo'lishi kerak. Yasaladigan xizmatlar holatsiz (stateless),
//  yagona bog'liqligi so'rov doirasidagi `AppDbContext`.
// ===========================================================================

/// <summary>Bitta oy ichidagi BITTA toifa qatori (o'qish, avtobus, ovqat, ...).</summary>
/// <param name="Amount">To'liq summa, chegirmasiz.</param>
/// <param name="Discount">Qo'llangan (tasdiqlangan) chegirma.</param>
/// <param name="Payable">To'lash kerak = Amount − Discount.</param>
/// <param name="Paid">To'langan (storno qilingan pul hisobga olinmaydi).</param>
/// <param name="Remaining">Qoldiq = Payable − Paid.</param>
/// <param name="Status">open | partial | paid | void.</param>
public sealed record PortalCategoryLineDto(
    string CategoryCode, string CategoryName,
    decimal Amount, decimal Discount, decimal Payable, decimal Paid, decimal Remaining,
    string Status, bool IsOverdue, DateOnly DueOn);

/// <summary>
/// Bitta oy — ichida toifalar kesimi. Jamlar SERVERDA qo'shiladi; bekor
/// qilingan (<c>void</c>) hisob-faktura jamga KIRMAYDI, lekin qatori
/// ko'rinib turadi.
/// </summary>
/// <param name="PeriodMonth">"YYYY-MM".</param>
public sealed record PortalMonthDto(
    string PeriodMonth,
    decimal Amount, decimal Discount, decimal Payable, decimal Paid, decimal Remaining,
    bool HasOverdue,
    List<PortalCategoryLineDto> Categories);

/// <summary>
/// Qarzning bitta qatori: qaysi oy, qaysi toifa, qancha. Qabul mezoni —
/// "qarz bo'lsa aniq ko'rinsin: qancha, qaysi oy uchun, qaysi toifada".
/// </summary>
public sealed record PortalDebtLineDto(
    string PeriodMonth, string CategoryCode, string CategoryName,
    decimal Remaining, bool IsOverdue, DateOnly DueOn);

/// <summary>To'lovning bitta toifaga tushgan qismi ("sentabr · avtobus — 450 000").</summary>
public sealed record PortalPaymentPartDto(
    string PeriodMonth, string CategoryCode, string CategoryName, decimal Amount);

/// <summary>
/// Storno (bekor qilish) yozuvi. Sababi MAJBURIY — <c>PaymentService.ReverseAsync</c>
/// uni bo'sh qoldirmaydi va aynan storno qatorining <c>note</c> ustuniga yozadi.
/// </summary>
/// <param name="ReceiptNo">Bekor qilish chekining raqami (uuid emas — ota-ona ko'radi).</param>
public sealed record PortalReversalDto(
    long ReceiptNo, DateTimeOffset ReversedAt, string Reason);

/// <summary>
/// Ota-ona ko'radigan to'lov qatori.
///
/// <para>
/// <b>Ichki id'lardan faqat <see cref="PaymentId"/> bor</b> va u ekranda
/// KO'RSATILMAYDI — chek PDF'ining manzilini yig'ish uchun kerak. Kassir id'si,
/// smena id'si, hisob-faktura id'si bu yerga umuman chiqmaydi.
/// </para>
/// <para>
/// <b>Storno qatorining o'zi ro'yxatda alohida turmaydi</b>: u bekor qilgan
/// to'lovning ichiga <see cref="Reversal"/> bo'lib joylashadi. Aks holda
/// ota-ona bitta summani ikki marta ko'rib, ikki marta to'langan deb o'ylardi.
/// Bekor qilingan to'lovning O'ZI yashirilmaydi — u ro'yxatda qoladi va UI uni
/// ustidan chizib ko'rsatadi.
/// </para>
/// </summary>
public sealed record PortalPaymentDto(
    Guid PaymentId, long ReceiptNo, DateTimeOffset ReceivedAt,
    decimal Amount, string Method, string? Note,
    decimal Unallocated,
    PortalReversalDto? Reversal,
    List<PortalPaymentPartDto> Parts);

/// <summary>Ota-ona / o'quvchi moliya ekranining butun ma'lumoti.</summary>
/// <param name="Debt">Jami qarz (musbat son). 0 = qarzsiz.</param>
/// <param name="Credit">Taqsimlanmagan avans.</param>
/// <param name="Months">Yangi oydan eskisiga.</param>
/// <param name="DebtLines">Eski oydan yangisiga — avval nimani to'lash kerakligi.</param>
/// <param name="Payments">Yangisidan eskisiga; storno qatorlarisiz.</param>
public sealed record PortalFinanceDto(
    string StudentName, string ClassName,
    decimal Debt, decimal Credit,
    List<PortalMonthDto> Months,
    List<PortalDebtLineDto> DebtLines,
    List<PortalPaymentDto> Payments);

/// <summary>
/// O'quvchi/ota-ona moliya ko'rinishi (P1-19) va uning cheki.
///
/// <para>
/// <b>Kimga ochiq.</b> <c>student</c> — o'zining, <c>parent</c> — farzandining,
/// <c>admin</c>/<c>superadmin</c>/<c>staff</c> — <c>?studentId=</c> orqali
/// istalgan o'quvchining (o'quvchi kartochkasi sahifasi shu javobni o'qiydi).
/// Boshqa hech kim: <c>teacher</c> va <c>cashier</c> bu yerga kira olmaydi —
/// kassirning ekrani <c>/api/cash/*</c>, o'qituvchiniki esa moliyaga umuman
/// aloqasiz.
/// </para>
/// <para>
/// <b>Faqat O'QISH.</b> Bu controller'da POST/PUT/DELETE yo'q va bo'lmaydi:
/// ota-ona ekrani pulga tegmaydi.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = PortalFinanceController.AllowedRoles)]
[Route("api/student")]
public sealed class PortalFinanceController(
    AppDbContext db,
    TelegramService telegram,
    ILogger<ReceiptService> receiptLogger,
    IConfiguration config) : ControllerBase
{
    internal const string AllowedRoles =
        Roles.Student + ",parent," + Roles.Admin + "," + Roles.SuperAdmin + "," + Roles.Staff;

    private const string PdfMime = "application/pdf";

    /// <summary>Ota-onaning o'z farzandining moliyaviy kartochkasi.</summary>
    [HttpGet("billing")]
    [Produces("application/json")]
    public async Task<ActionResult<PortalFinanceDto>> Billing(
        [FromQuery] string? studentId, CancellationToken ct)
    {
        var target = await ResolveAsync(studentId, ct);
        if (target.Error is not null) return target.Error;

        var card = await Invoices.ForStudentAsync(target.StudentId!, ct);
        if (card is null) return NotFound(new { message = "O'quvchi topilmadi" });

        return Ok(ToPortal(card));
    }

    /// <summary>
    /// Chek PDF'i — SPEC §4.7 dagi "ota-onada mustaqil nusxa" ning web ko'rinishi.
    ///
    /// <para>
    /// <c>/api/receipts/{id}.pdf</c> dan farqi FAQAT ruxsatda: u yerda
    /// <see cref="FinanceAction.AcceptPayment"/> turadi (kassir/admin/direktor),
    /// bu yerda esa egalik tekshiriladi — to'lov chaqiruvchining o'quvchisiga
    /// tegishli bo'lmasa <b>404</b> (403 emas: begona chekning MAVJUDLIGI ham
    /// ma'lumot). PDF ni ikkalasi ham bitta <see cref="ReceiptService"/> dan
    /// oladi, ya'ni ota-onadagi nusxa kassirdagisi bilan bir xil hujjat.
    /// </para>
    /// </summary>
    [HttpGet("receipts/{paymentId:guid}.pdf")]
    public async Task<IActionResult> Receipt(
        Guid paymentId, [FromQuery] string? studentId, CancellationToken ct)
    {
        var target = await ResolveAsync(studentId, ct);
        if (target.Error is not null) return target.Error;

        var owns = await db.Payments.AsNoTracking()
            .AnyAsync(p => p.Id == paymentId && p.StudentId == target.StudentId, ct);
        if (!owns) return NotFound(new { message = "Chek topilmadi" });

        byte[] pdf;
        try
        {
            pdf = await Receipts.RenderPdfAsync(paymentId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Chek topilmadi" });
        }

        Response.Headers.ContentDisposition = $"inline; filename=\"chek-{paymentId}.pdf\"";
        return File(pdf, PdfMime);
    }

    // =====================================================================
    //  Javobni yig'ish
    // =====================================================================

    /// <summary>
    /// Admin javobini (<see cref="StudentBillingDto"/>) ota-ona ko'radigan
    /// shaklga o'giradi: oylar bo'yicha guruhlaydi, jamlarni SERVERDA qo'shadi
    /// va ichki id'larni tashlab yuboradi.
    ///
    /// <para>
    /// <c>internal</c> — Telegram Mini App'ning ota-ona moliya ekrani
    /// (<c>GET /api/tg/parent/children/{id}/finance</c>) AYNAN shu javob shaklini
    /// qaytaradi. Ikkinchi nusxa yozilsa ikkita "ota-ona moliyasi" paydo bo'lardi
    /// va ular jamlarni bir xil qo'shishiga hech qanday kafolat qolmasdi.
    /// Mini App'ga alohida endpoint kerak bo'lgani BOSHQA sabab: bu yerdagi
    /// <see cref="ResolveAsync"/> ota-ona uchun <c>?studentId=</c> ni ATAYLAB
    /// e'tiborsiz qoldiradi va telefon bo'yicha BIRINCHI farzandni topadi —
    /// ikki farzandli ota-onaga ikkinchisi ko'rinmaydi.
    /// </para>
    /// </summary>
    internal static PortalFinanceDto ToPortal(StudentBillingDto card)
    {
        // ---- Oylar × toifalar ----
        // Bekor qilingan (void) hisob-faktura jamga kirmaydi, lekin qatori
        // ko'rinadi: ota-ona "u oy nega yo'qoldi" deb so'ramasligi kerak.
        var months = card.Invoices
            .GroupBy(i => i.PeriodMonth)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var counted = g.Where(i => i.Status != InvoiceStatus.Void).ToList();
                return new PortalMonthDto(
                    Month(g.Key),
                    counted.Sum(i => i.Amount),
                    counted.Sum(i => i.Discount),
                    counted.Sum(i => i.Payable),
                    counted.Sum(i => i.Paid),
                    counted.Sum(i => i.Remaining),
                    counted.Any(i => i.IsOverdue && i.Remaining > 0m),
                    [.. g.OrderBy(i => i.CategoryName, StringComparer.Ordinal)
                         .Select(i => new PortalCategoryLineDto(
                             i.CategoryCode, i.CategoryName,
                             i.Amount, i.Discount, i.Payable, i.Paid, i.Remaining,
                             i.Status, i.IsOverdue && i.Remaining > 0m, i.DueOn))]);
            })
            .ToList();

        // ---- Qarz qatorlari: eng eski oydan boshlab ----
        var debtLines = card.Invoices
            .Where(i => i.Status != InvoiceStatus.Void && i.Remaining > 0m)
            .OrderBy(i => i.PeriodMonth)
            .ThenBy(i => i.CategoryName, StringComparer.Ordinal)
            .Select(i => new PortalDebtLineDto(
                Month(i.PeriodMonth), i.CategoryCode, i.CategoryName,
                i.Remaining, i.IsOverdue, i.DueOn))
            .ToList();

        // ---- To'lovlar: storno qatori originalning ICHIGA ko'chiriladi ----
        var stornoOf = card.Payments
            .Where(p => p.ReversalOf is not null)
            .GroupBy(p => p.ReversalOf!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var payments = card.Payments
            .Where(p => p.ReversalOf is null)
            .OrderByDescending(p => p.ReceivedAt)
            .Select(p => new PortalPaymentDto(
                p.Id, p.ReceiptNo, p.ReceivedAt, p.Amount, p.Method, p.Note, p.Unallocated,
                stornoOf.TryGetValue(p.Id, out var storno)
                    ? new PortalReversalDto(
                        storno.ReceiptNo, storno.ReceivedAt,
                        // Sabab bo'sh bo'la olmaydi (ReverseAsync uni majburlaydi),
                        // lekin qo'lda yozilgan eski qator uchun ham matn qoladi.
                        string.IsNullOrWhiteSpace(storno.Note)
                            ? "Sabab ko'rsatilmagan"
                            : storno.Note)
                    : null,
                [.. p.Allocations
                     .OrderBy(a => a.PeriodMonth)
                     .ThenBy(a => a.CategoryName, StringComparer.Ordinal)
                     .Select(a => new PortalPaymentPartDto(
                         Month(a.PeriodMonth), a.CategoryCode, a.CategoryName, a.Amount))]))
            .ToList();

        return new PortalFinanceDto(
            card.StudentName, card.ClassName, card.Debt, card.Credit,
            months, debtLines, payments);
    }

    /// <summary>Oyning birinchi kuni → "YYYY-MM" (ekranda "Sen 2026" bo'lib chiqadi).</summary>
    private static string Month(DateOnly first) =>
        first.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // =====================================================================
    //  Kim kimning ma'lumotini ko'radi
    // =====================================================================

    /// <summary>Aniqlangan o'quvchi yoki tayyor xato javobi (ikkalasidan biri null).</summary>
    private readonly record struct Target(string? StudentId, ActionResult? Error);

    /// <summary>
    /// SPEC §4.4 ruhida: MA'LUMOT EGASINI SERVER ANIQLAYDI. O'quvchi va ota-ona
    /// uchun <c>?studentId=</c> butunlay E'TIBORGA OLINMAYDI — aks holda ota-ona
    /// begona bola id'sini yozib, uning qarzini ko'rardi.
    ///
    /// <para>
    /// Ota-ona bog'lanishi bugungi kunda telefon raqami orqali
    /// (<c>users.email</c> = login = telefon ↔ <c>students.parent_phone</c>) —
    /// aynan <c>StudentPortalController.TargetAsync</c> dagi qoida. Ikki nusxa
    /// bo'lib qolgani <c>docs/PENDING_WIRING.md</c> da yozilgan: SPEC §3.2
    /// dagi ko'p-ko'pga bog'lanish kelganda ikkalasi ham shu yagona joyga
    /// o'tishi kerak.
    /// </para>
    /// </summary>
    private async Task<Target> ResolveAsync(string? studentId, CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Staff))
        {
            if (string.IsNullOrWhiteSpace(studentId))
                return new Target(null, BadRequest(new { message = "?studentId=... kerak" }));

            var exists = await db.Students.AsNoTracking()
                .AnyAsync(s => s.Id == studentId, ct);
            return exists
                ? new Target(studentId, null)
                : new Target(null, NotFound(new { message = "O'quvchi topilmadi" }));
        }

        if (uid is null) return new Target(null, Unauthorized());

        if (User.IsInRole("parent"))
        {
            var login = await db.Users.AsNoTracking()
                .Where(u => u.Id == uid).Select(u => u.Email).FirstOrDefaultAsync(ct);
            var phone = OnlyDigits(login);
            if (phone.Length == 0)
                return new Target(null, NotFound(new { message = "Farzand topilmadi" }));

            var child = await db.Students.AsNoTracking()
                .Where(s => !s.IsArchived)
                .Select(s => new { s.Id, s.ParentPhone })
                .ToListAsync(ct);
            var match = child.FirstOrDefault(s => OnlyDigits(s.ParentPhone) == phone);
            return match is null
                ? new Target(null, NotFound(new { message = "Farzand topilmadi" }))
                : new Target(match.Id, null);
        }

        var own = await db.Students.AsNoTracking()
            .Where(s => s.UserId == uid).Select(s => s.Id).FirstOrDefaultAsync(ct);
        return own is null
            ? new Target(null, NotFound(new { message = "O'quvchi topilmadi" }))
            : new Target(own, null);
    }

    private static string OnlyDigits(string? value) =>
        new([.. (value ?? string.Empty).Where(char.IsDigit)]);

    // =====================================================================
    //  Xizmatlar — DI'siz (docs/PENDING_WIRING.md §3a namunasi)
    // =====================================================================

    private IInvoiceService Invoices => new InvoiceService(db, new LedgerService(db));

    private IReceiptService Receipts => new ReceiptService(
        new PaymentService(db, new LedgerService(db)),
        db, telegram, receiptLogger, config);
}
