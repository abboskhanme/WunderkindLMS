using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Admin: Telegram Mini App bog'lanishlari. SPEC §6 Faza 3.
// ===========================================================================
//
//  Bog'lanishni MAKTAB boshlaydi: xodim kerakli foydalanuvchi uchun bir
//  martalik kod chiqaradi, egasiga aytadi, egasi Mini App'da kiritadi.
//  Sabab va rad etilgan variantlar: `TelegramLinkService` fayl boshidagi
//  izoh va docs/ASSUMPTIONS.md.
//
//  KOD JAVOBDA BIR MARTA KO'RINADI. Bazada faqat hash'i saqlanadi, ya'ni
//  ekranni yopgandan keyin uni qayta ko'rsatib bo'lmaydi — yangisini
//  chiqarish kerak (eskisi shu zahoti kuchini yo'qotadi).
// ===========================================================================

/// <summary>Admin "Telegram" bo'limi (`/api/admin/telegram`).</summary>
[ApiController]
[Authorize]
[AdminPerm("app")]
[Route("api/admin/telegram")]
public sealed class AdminTelegramController(AppDbContext db) : ControllerBase
{
    /// <summary>Ro'yxat uzunligining yuqori chegarasi.</summary>
    private const int PageLimit = 200;

    private TelegramLinkService Links => new(db);

    /// <summary>Bir martalik bog'lanish kodi. Kod javobda FAQAT shu safar ko'rinadi.</summary>
    [HttpPost("link-codes")]
    public async Task<ActionResult<LinkCodeDto>> IssueCode(IssueLinkCodeRequest req, CancellationToken ct)
    {
        var actor = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (actor is null) return Unauthorized();

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UserId, ct);
        if (target is null) return NotFound(new { message = "Foydalanuvchi topilmadi" });

        var already = await db.TelegramAccounts.AsNoTracking().AnyAsync(a => a.UserId == target.Id, ct);
        if (already)
            return Conflict(new
            {
                code = "user_already_linked",
                message = "Bu foydalanuvchiga Telegram akkaunti allaqachon bog'langan. Avval uni uzing.",
            });

        var issued = await Links.IssueAsync(target.Id, actor, ct);
        return new LinkCodeDto(
            issued.Code, target.Id, target.FullName, target.Role, issued.ExpiresAt.ToString("o"));
    }

    /// <summary>Mavjud bog'lanishlar (ism, login yoki Telegram nomi bo'yicha qidiruv bilan).</summary>
    [HttpGet("links")]
    public async Task<ActionResult<IEnumerable<TelegramLinkDto>>> Links_(
        [FromQuery] string? search, CancellationToken ct)
    {
        var rows = await (from a in db.TelegramAccounts.AsNoTracking()
                          join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                          select new
                          {
                              a.TelegramUserId, a.DisplayName, a.Username, a.LinkedAt, a.LastSeenAt,
                              UserId = u.Id, u.FullName, u.Role, u.Email,
                          })
            .ToListAsync(ct);

        var term = (search ?? "").Trim();
        if (term.Length > 0)
            rows = [.. rows.Where(r =>
                r.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.Email.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.Username ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))];

        return rows
            .OrderByDescending(r => r.LastSeenAt)
            .Take(PageLimit)
            .Select(r => new TelegramLinkDto(
                r.TelegramUserId.ToString(), r.DisplayName, r.Username,
                r.UserId, r.FullName, r.Role,
                r.LinkedAt.ToString("o"), r.LastSeenAt.ToString("o")))
            .ToList();
    }

    /// <summary>
    /// Bog'lanishni uzish (telefon almashdi, akkaunt boshqa odamga o'tdi).
    /// Foydalanuvchining o'zi ham uza oladi — <c>DELETE /api/tg/link</c>.
    /// </summary>
    [HttpDelete("links/{telegramUserId:long}")]
    public async Task<IActionResult> Unlink(long telegramUserId, CancellationToken ct)
    {
        var removed = await Links.UnlinkAsync(telegramUserId, ct);
        return removed ? NoContent() : NotFound(new { message = "Bog'lanish topilmadi" });
    }
}
