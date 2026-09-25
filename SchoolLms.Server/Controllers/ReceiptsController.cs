using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// To'lov cheki: PDF, 58 mm termal chek uchun JSON (2026-09-22) va Telegramga
/// yuborish. Vazifa: P1-12. SPEC §4.7.
///
/// <para>
/// <b>RBAC.</b> Uchala endpoint ham SPEC §4.3 jadvalining "To'lov qabul qilish
/// va chek berish" qatoriga bog'langan
/// (<see cref="FinanceAction.AcceptPayment"/> — kassir, admin, direktor).
/// Ruxsat matritsada, bu yerda emas: yangi qoida kerak bo'lsa
/// <see cref="FinanceMatrix.Rules"/> tahrirlanadi.
/// </para>
///
/// <para>
/// <b>Nega yuborish GET emas, POST.</b> Chekni ko'rish (GET) hech qanday nojo'ya
/// ta'sir bermasligi kerak: admin chekni ikki marta ochsa, ota-ona ikkita xabar
/// olmasligi shart. Shuning uchun Telegramga yuborish — alohida, ataylab
/// chaqiriladigan POST.
/// </para>
///
/// <para>
/// <b>DI hali ulanmagan.</b> <c>IReceiptService</c> ni <c>Program.cs</c> da P1-15
/// ro'yxatdan o'tkazadi (aniq satr <c>docs/PENDING_WIRING.md</c> da). Ungacha bu
/// endpoint'lar 401/403 ni to'g'ri qaytaradi (avtorizatsiya filtri controller
/// yaratilishidan OLDIN ishlaydi), ruxsat berilgan so'rov esa DI xatosiga uchraydi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/receipts")]
public class ReceiptsController(
    IReceiptService receipts,
    IPaymentService payments,
    // 58 mm termal chek (2026-09-22): "yopildi / qoldi" holati hisob-fakturalar
    // xizmatidan, sinf va storno holati bazadan o'qiladi. `IReceiptService` ga
    // metod QO'SHILMADI — testlardagi soxta implementatsiyalar buzilmasin.
    IInvoiceService invoices,
    IAppDbContext db,
    IConfiguration config) : ControllerBase
{
    private const string PdfMime = "application/pdf";

    /// <summary>
    /// 58 mm termal chek uchun ma'lumot (JSON). Brauzer uni yashirin iframe'da
    /// ikki nusxada (maktab + ota-ona) chop etadi — PDF'ni saqlash/ochish shart
    /// emas (mijoz so'rovi, 2026-09-22).
    ///
    /// <para>
    /// Ichida: PDF chekdagi hamma narsa (qatorlar AYNAN <c>ReceiptService.BuildModel</c>
    /// dan, matn <c>ReceiptText</c> dan) + har abonement oyi uchun "to'liq
    /// yopildi" yoki "qoldi: …" (serverda hisoblangan <c>InvoiceDto.Remaining</c>,
    /// chop etilgan paytdagi holat) + o'quvchining sinfi.
    /// </para>
    /// <para>
    /// RBAC — PDF bilan AYNAN bir xil: <see cref="FinanceAction.AcceptPayment"/>
    /// (kassir, admin, direktor) va kassir faqat o'z chekini ko'radi (F0.04,
    /// boshqasiniki 404). Endpoint faqat O'QIYDI.
    /// </para>
    /// </summary>
    [HttpGet("{paymentId:guid}")]
    [FinanceRole(FinanceAction.AcceptPayment)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReceiptPrintDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReceiptPrintDto>> Print(
        Guid paymentId, CancellationToken ct, [FromQuery] bool original = false)
    {
        if (await ForbiddenForOtherCashierAsync(paymentId, ct) is { } forbidden) return forbidden;

        // QR asosi: sozlangan ochiq manzil (prod), bo'lmasa shu so'rovning o'z manzili (lokal/dev).
        var baseUrl = config[ReceiptQr.BaseUrlKey] is { Length: > 0 } configured
            ? configured
            : $"{Request.Scheme}://{Request.Host}";
        // `original=true` — profilidan qayta chop etish: birinchi chek bilan bir xil (2026-09-25).
        var receipt = await ReceiptPrintQuery.GetAsync(paymentId, payments, invoices, db, ct, baseUrl, original);
        if (receipt is null) return NotFound(new { message = "To'lov topilmadi" });
        return receipt;
    }

    /// <summary>
    /// Chek PDF'i. Ichida: maktab nomi, chek raqami, sana-vaqt (Toshkent),
    /// o'quvchi, <b>har toifa alohida qatorda</b>, to'lov turi, jami va kassir.
    /// Storno qilingan to'lovda "BEKOR QILINGAN" shtampi va bekor qilingan sanasi.
    ///
    /// <para>
    /// Brauzerda ochilishi uchun <c>inline</c> — kassir uni to'g'ridan-to'g'ri
    /// chop etadi, avval diskka saqlamaydi.
    /// </para>
    /// </summary>
    // DIQQAT: bu yerda `[Produces("application/pdf")]` BO'LMASLIGI kerak.
    // U chiqish formatini butun amalga majburlaydi, shuning uchun 404 javobidagi
    // JSON tanani formatlay olmay, mijozga 406 Not Acceptable qaytarardi —
    // ya'ni "to'lov topilmadi" o'rniga tushunarsiz bo'sh xato. Muvaffaqiyatli
    // javobning turini `File(pdf, PdfMime)` ning o'zi belgilaydi.
    [HttpGet("{paymentId:guid}.pdf")]
    [FinanceRole(FinanceAction.AcceptPayment)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(Guid paymentId, CancellationToken ct)
    {
        if (await ForbiddenForOtherCashierAsync(paymentId, ct) is { } forbidden) return forbidden;

        byte[] pdf;
        try
        {
            pdf = await receipts.RenderPdfAsync(paymentId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "To'lov topilmadi" });
        }

        Response.Headers.ContentDisposition = $"inline; filename=\"chek-{paymentId}.pdf\"";
        return File(pdf, PdfMime);
    }

    /// <summary>
    /// Chekni ota-onaning Telegramiga yuboradi (mavjud bot orqali).
    ///
    /// <para>
    /// <b>Yetkazilmaslik — XATO EMAS.</b> Ota-ona botga ulanmagan bo'lsa yoki
    /// Telegram javob bermasa, javob baribir 200 bo'ladi va
    /// <c>delivered = false</c> qaytadi. Sabab: pul allaqachon qabul qilingan;
    /// 4xx/5xx qaytarish kassir ekranida "to'lov o'tmadi" degan taassurot
    /// qoldirardi, bu esa bir to'lovni ikki marta olishga olib kelardi.
    /// </para>
    /// </summary>
    [HttpPost("{paymentId:guid}/telegram")]
    [FinanceRole(FinanceAction.AcceptPayment)]
    public async Task<ActionResult<ReceiptDeliveryDto>> SendToTelegram(Guid paymentId, CancellationToken ct)
    {
        if (await ForbiddenForOtherCashierAsync(paymentId, ct) is { } forbidden) return forbidden;

        var delivered = await receipts.SendToGuardianAsync(paymentId, ct);

        return new ReceiptDeliveryDto(
            delivered,
            delivered
                ? "Chek ota-onaning Telegramiga yuborildi"
                : "Chek yuborilmadi: ota-ona Telegramda ro'yxatdan o'tmagan yoki Telegram javob bermadi. "
                  + "To'lov kuchda qoladi — chekni chop etib bering.");
    }

    /// <summary>
    /// finance-parity.md F0.04 — <c>PaymentsController.OnlyOwnPayments</c>
    /// bilan bir xil qoida (SPEC §4.3: kassirda "kassirlar kesimidagi
    /// ko'rinish" yo'q): kassir FAQAT o'zi qabul qilgan to'lovning chekini
    /// ko'ra yoki qayta yubora oladi, boshqa kassirning cheki uning uchun
    /// <b>mavjud emas</b> — 403 emas, 404 (chekning borligi ham ma'lumot).
    /// Admin va direktor cheklanmagan.
    /// </summary>
    private async Task<ActionResult?> ForbiddenForOtherCashierAsync(Guid paymentId, CancellationToken ct)
    {
        if (!OnlyOwnReceipts) return null;

        var payment = await payments.GetAsync(paymentId, ct);
        if (payment is null || payment.CashierId != FinanceActor.RequireUserId(User))
            return NotFound(new { message = "To'lov topilmadi" });

        return null;
    }

    private bool OnlyOwnReceipts =>
        User.IsInRole(Roles.Cashier)
        && !User.IsInRole(Roles.Admin)
        && !User.IsInRole(Roles.SuperAdmin);
}

/// <summary>
/// Telegramga yuborish natijasi.
///
/// <para>
/// Nega <c>Dtos/BillingDtos.cs</c> da emas: u fayl P1-06 da MUZLATILGAN va
/// Faza 1.C dagi beshta agent unga parallel tayanadi. Bitta endpoint'ning
/// javobi uchun umumiy faylni qo'zg'atish — keraksiz konflikt. Bu tip
/// frontendga ham kerak bo'lsa, P1-16 uni o'sha yerga ko'chiradi.
/// </para>
/// </summary>
/// <param name="Delivered">Kamida bitta chatga yetib bordimi.</param>
/// <param name="Message">Kassir ekranida ko'rsatiladigan o'zbekcha izoh.</param>
public sealed record ReceiptDeliveryDto(bool Delivered, string Message);
