using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Jadval ko'rinishi sozlamalari — docs/modules/students-parity.md §2.11 (X-1):
/// katta ro'yxatlarda ustunlarni yashirish/tartiblash/qadash, foydalanuvchi ×
/// sahifa kesimida serverda saqlanadi.
///
/// <para>
/// <b>FAQAT joriy foydalanuvchining O'ZI.</b> Bu yerda "boshqa birovning
/// sozlamasi" degan tushuncha yo'q: <c>userId</c> so'rov tanasida ham,
/// query'da ham QABUL QILINMAYDI — u har doim tokendan (<c>NameIdentifier</c>)
/// olinadi. Shu bois bitta kalit (user_id, page) bo'yicha yozish/o'qish
/// boshqa qatorga hech qachon tegmaydi — IDOR imkonsiz, chunki so'rovda
/// "kimning qatori" degan parametrning o'zi yo'q.
/// </para>
/// <para>
/// <b>Ruxsat — admin panelidagi rollar.</b> Bu ekranlar (Guruhlar, Xonalar, ...)
/// admin panelida, shuning uchun <c>admin</c>/<c>superadmin</c>/<c>staff</c>.
/// <c>AdminPermAttribute</c> ishlatilmaydi: u bo'lim ruxsatini (masalan
/// <c>classes</c>) tekshiradi, bu yerda esa bo'lim degan narsa yo'q — har kim
/// FAQAT o'zining ko'rinish sozlamasini o'zgartiradi, bu boshqa xodimning
/// ishiga umuman ta'sir qilmaydi.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin + "," + Roles.Staff)]
[Route("api/user-table-settings")]
public class UserTableSettingsController(AppDbContext db) : ControllerBase
{
    public const string PageRequiredMessage = "Sahifa kaliti bo'sh bo'la olmaydi";

    private string UserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";

    /// <summary>
    /// Joriy foydalanuvchining shu sahifadagi ko'rinishi. Hali saqlanmagan
    /// bo'lsa — bo'sh sozlama (<c>{}</c>) bilan 200 (404 emas: "sozlanmagan"
    /// ekran uchun ham xato holati emas, ekran o'z sukut ko'rinishini beradi).
    /// </summary>
    [HttpGet("{page}")]
    public async Task<ActionResult<UserTableSettingsDto>> Get(string page, CancellationToken ct = default)
    {
        var key = (page ?? "").Trim();
        if (key.Length == 0) return BadRequest(new { message = PageRequiredMessage });

        var row = await db.UserTableSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == UserId && x.Page == key, ct);

        return ToDto(key, row);
    }

    /// <summary>
    /// Ko'rinishni saqlaydi (yo'q bo'lsa yaratadi, bor bo'lsa TO'LIQ
    /// almashtiradi — ekran har doim butun holatni yuboradi).
    /// </summary>
    [HttpPut("{page}")]
    public async Task<ActionResult<UserTableSettingsDto>> Save(
        string page, SaveUserTableSettingsRequest req, CancellationToken ct = default)
    {
        var key = (page ?? "").Trim();
        if (key.Length == 0) return BadRequest(new { message = PageRequiredMessage });

        var row = await db.UserTableSettings
            .FirstOrDefaultAsync(x => x.UserId == UserId && x.Page == key, ct);

        var json = req.Settings.GetRawText();
        if (row is null)
        {
            row = new UserTableSetting { UserId = UserId, Page = key, Settings = json };
            db.UserTableSettings.Add(row);
        }
        else
        {
            row.Settings = json;
        }
        row.UpdatedAt = AppClock.NowInstant;

        await db.SaveChangesAsync(ct);
        return ToDto(key, row);
    }

    private static UserTableSettingsDto ToDto(string page, UserTableSetting? row)
    {
        using var doc = JsonDocument.Parse(row?.Settings ?? "{}");
        return new UserTableSettingsDto(page, doc.RootElement.Clone(), row?.UpdatedAt);
    }
}
