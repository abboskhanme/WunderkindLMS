using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Telegram Mini App — chekni o'z Telegram chatiga olish.
// ===========================================================================
//
//  NEGA YUKLAB OLISH EMAS
//  ----------------------
//  Mini App chekni `blob` + `<a download>` bilan yuklardi. Telegram'ning
//  Android WebView'i blob yuklashni umuman qabul qilmaydi (tugma jim turadi),
//  iOS esa PDF'ni ilovaning USTIDA ochib, ortga qaytish yo'lini yopadi.
//  Telegram'ning o'z `downloadFile` usuli esa sarlavhasiz ochiq URL talab
//  qiladi — chekni tokensiz ochib qo'yish bo'lardi.
//
//  Shuning uchun bot chekni foydalanuvchining O'Z chatiga hujjat qilib
//  yuboradi: har qanday telefonda ishlaydi, fayl chatda saqlanib qoladi va
//  "hamma narsa Telegram orqali" qoidasiga to'g'ri keladi (CLAUDE.md).
//
//  KIMGA: faqat so'rovchining o'ziga bog'langan Telegram akkaunt(lar)iga
//  (`telegram_accounts`). Boshqa chatga yuborish yo'li bu yerda yo'q.
//  NIMA: faqat o'z farzandining (ota-ona — vasiylik yoki telefon, o'quvchi —
//  o'zi) to'lovi. Begona to'lov — 404 (mavjudligi ham ma'lumot).
// ===========================================================================

/// <summary>Chekni Mini App foydalanuvchisining Telegram chatiga yuborish.</summary>
[ApiController]
[Authorize(Roles = "parent,student")]
[Route("api/tg/receipts")]
public sealed class TelegramReceiptController(
    AppDbContext db,
    IReceiptService receipts,
    TelegramService telegram,
    ILogger<TelegramReceiptController> logger) : ControllerBase
{
    /// <summary>
    /// Chek PDF'ini so'rovchining Telegram chatiga yuboradi.
    /// 409 — Telegram akkaunti bog'lanmagan; 404 — to'lov yo'q yoki begona;
    /// 503 — bot sozlanmagan; 502 — Telegram qabul qilmadi.
    /// </summary>
    [HttpPost("{paymentId:guid}/send")]
    [EnableRateLimiting("telegram")]
    public async Task<IActionResult> Send(Guid paymentId, CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();

        // Shaxsiy chatda chat id = Telegram foydalanuvchi id'si.
        var chats = await db.TelegramAccounts.AsNoTracking()
            .Where(a => a.UserId == uid)
            .Select(a => a.TelegramUserId)
            .Distinct()
            .ToListAsync(ct);
        if (chats.Count == 0)
            return Conflict(new
            {
                code = "telegram_not_linked",
                message = "Telegram hisobingiz ulanmagan. Ilovani Telegram ichidan oching.",
            });

        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => new { p.StudentId, p.ReceiptNo })
            .FirstOrDefaultAsync(ct);
        if (payment is null || !await OwnsAsync(uid, payment.StudentId, ct))
            return NotFound(new { message = "Chek topilmadi" });

        if (!telegram.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "telegram_not_configured",
                message = "Telegram bot sozlanmagan. Maktab ma'muriyatiga murojaat qiling.",
            });

        byte[] pdf;
        try { pdf = await receipts.RenderPdfAsync(paymentId, ct); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Chek topilmadi" }); }

        var caption = $"To'lov cheki № {payment.ReceiptNo}";
        var delivered = false;
        foreach (var chatId in chats)
            delivered |= await telegram.SendDocumentAsync(
                chatId, pdf, ReceiptText.FileName(payment.ReceiptNo), caption, ct);

        if (!delivered)
        {
            logger.LogWarning("Chek № {ReceiptNo} Mini App so'rovi bilan yuborilmadi: user={UserId}",
                payment.ReceiptNo, uid);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                code = "telegram_send_failed",
                message = "Telegram chekni qabul qilmadi. Botga /start yozib, qayta urinib ko'ring.",
            });
        }

        return Ok(new { sent = true });
    }

    /// <summary>Ota-ona — vasiylik yoki eski telefon bog'lanishi; o'quvchi — faqat o'zi.</summary>
    private async Task<bool> OwnsAsync(string uid, string studentId, CancellationToken ct) =>
        User.IsInRole("parent")
            ? await new GuardianAccess(db).ParentOwnsAsync(uid, studentId, ct)
            : await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId && s.UserId == uid, ct);
}
