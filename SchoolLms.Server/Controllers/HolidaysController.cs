using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Bayram / dam olish kunlari (butun maktab). Belgilangan sanalarda hech bir sinfda dars bo'lmaydi —
/// dars jadvali ko'rinishlarida "Bayram" deb ko'rsatiladi va jurnal ustunlaridan chiqarib tashlanadi.
/// Faqat admin/superadmin boshqaradi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/holidays")]
public class HolidaysController(AppDbContext db) : ControllerBase
{
    /// <summary>Barcha bayram kunlari (ixtiyoriy ?year=2026 bo'yicha filtr).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HolidayDto>>> GetAll([FromQuery] int? year)
    {
        var q = db.Holidays.AsQueryable();
        if (year is int y) q = q.Where(h => h.Date.StartsWith(y.ToString() + "-"));
        return await q.OrderBy(h => h.Date)
            .Select(h => new HolidayDto(h.Date, h.Name)).ToListAsync();
    }

    /// <summary>Bayram kunini qo'shadi yoki nomini yangilaydi (sana bo'yicha upsert).</summary>
    [HttpPut]
    public async Task<ActionResult<HolidayDto>> Save(SaveHolidayRequest req)
    {
        var date = (req.Date ?? "").Trim();
        if (date.Length != 10) return BadRequest(new { message = "Sana 'YYYY-MM-DD' formatida bo'lsin" });
        var h = await db.Holidays.FirstOrDefaultAsync(x => x.Date == date);
        if (h is null)
        {
            h = new Holiday { Date = date };
            db.Holidays.Add(h);
        }
        h.Name = (req.Name ?? "").Trim();
        await db.SaveChangesAsync();
        return new HolidayDto(h.Date, h.Name);
    }

    /// <summary>Bayram kunini olib tashlaydi (sana bo'yicha).</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string date)
    {
        var h = await db.Holidays.FirstOrDefaultAsync(x => x.Date == date);
        if (h is not null)
        {
            db.Holidays.Remove(h);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}
