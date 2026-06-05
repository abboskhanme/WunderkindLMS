using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// GPS tracker / haydovchi ilovasi avtobus joylashuvini shu yerga yuboradi (webhook).
/// Tenant — subdomen (host) bo'yicha aniqlanadi; <c>deviceId</c> avtobusga bog'lanadi.
/// Tokenli himoya: SchoolMeta.GpsIngestToken o'rnatilgan bo'lsa, so'rovdagi token mos kelishi shart.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/devices/gps")]
public class GpsIngestController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Push(GpsPingRequest req)
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        if (meta is null || !meta.GpsEnabled)
            return BadRequest(new { message = "GPS integratsiyasi yoqilmagan" });
        if (!string.IsNullOrEmpty(meta.GpsIngestToken) && req.Token != meta.GpsIngestToken)
            return Unauthorized(new { message = "Token noto'g'ri" });
        if (string.IsNullOrWhiteSpace(req.DeviceId))
            return BadRequest(new { message = "deviceId shart" });
        if (req.Lat is < -90 or > 90 || req.Lng is < -180 or > 180)
            return BadRequest(new { message = "Koordinatalar noto'g'ri" });

        var bus = await db.Buses.FirstOrDefaultAsync(b => b.DeviceId == req.DeviceId);
        if (bus is null) return NotFound(new { message = "Bu deviceId ga avtobus bog'lanmagan" });

        var at = !string.IsNullOrEmpty(req.Time) && DateTime.TryParse(req.Time, out var t)
            ? t.ToString("yyyy-MM-ddTHH:mm:ss")
            : AppClock.Iso();

        db.BusLocations.Add(new BusLocation
        {
            BusId = bus.Id,
            Latitude = req.Lat,
            Longitude = req.Lng,
            Speed = req.Speed ?? 0,
            RecordedAt = at,
            CreatedAt = AppClock.Iso(),
        });
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
