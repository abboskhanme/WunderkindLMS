using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Maktab avtobuslarini GPS bo'yicha kuzatish (Boshqaruv → GPS). Avtobuslar ro'yxati + so'nggi
/// joylashuv (jonli xarita), kunlik iz (trail) va to'xtashlar (qayerda qancha turgan).
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/gps")]
public class GpsController(AppDbContext db) : ControllerBase
{
    private static BusDto Dto(Bus b) =>
        new(b.Id, b.Name, b.PlateNumber, b.DriverName, b.DriverPhone, b.DeviceId, b.Route, b.IsActive, b.Note);

    /// <summary>Avtobuslar + har birining so'nggi joylashuvi (jonli xarita uchun).</summary>
    [HttpGet("buses")]
    public async Task<ActionResult<IEnumerable<BusLiveDto>>> Buses()
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var onlineMin = meta?.GpsOnlineMinutes ?? 5;
        var now = AppClock.Now;

        var buses = await db.Buses.OrderBy(b => b.Name).ToListAsync();
        var ids = buses.Select(b => b.Id).ToList();

        // Har avtobusning eng so'nggi nuqtasi.
        var latest = (await db.BusLocations.Where(l => ids.Contains(l.BusId)).ToListAsync())
            .GroupBy(l => l.BusId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.RecordedAt, StringComparer.Ordinal).First());

        return buses.Select(b =>
        {
            latest.TryGetValue(b.Id, out var l);
            var online = l is not null && DateTime.TryParse(l.RecordedAt, out var t)
                         && (now - t).TotalMinutes <= onlineMin;
            return new BusLiveDto(Dto(b), l?.Latitude, l?.Longitude, l?.Speed, l?.RecordedAt, online);
        }).ToList();
    }

    [HttpPost("buses")]
    public async Task<ActionResult<BusDto>> Create(SaveBusRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Avtobus nomi shart" });
        var b = new Bus
        {
            Name = req.Name.Trim(),
            PlateNumber = (req.PlateNumber ?? "").Trim(),
            DriverName = (req.DriverName ?? "").Trim(),
            DriverPhone = (req.DriverPhone ?? "").Trim(),
            DeviceId = (req.DeviceId ?? "").Trim(),
            Route = (req.Route ?? "").Trim(),
            IsActive = req.IsActive,
            Note = (req.Note ?? "").Trim(),
        };
        db.Buses.Add(b);
        await db.SaveChangesAsync();
        return Dto(b);
    }

    [HttpPut("buses/{id}")]
    public async Task<ActionResult<BusDto>> Update(string id, SaveBusRequest req)
    {
        var b = await db.Buses.FindAsync(id);
        if (b is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Avtobus nomi shart" });
        b.Name = req.Name.Trim();
        b.PlateNumber = (req.PlateNumber ?? "").Trim();
        b.DriverName = (req.DriverName ?? "").Trim();
        b.DriverPhone = (req.DriverPhone ?? "").Trim();
        b.DeviceId = (req.DeviceId ?? "").Trim();
        b.Route = (req.Route ?? "").Trim();
        b.IsActive = req.IsActive;
        b.Note = (req.Note ?? "").Trim();
        await db.SaveChangesAsync();
        return Dto(b);
    }

    [HttpDelete("buses/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var b = await db.Buses.FindAsync(id);
        if (b is null) return NotFound();
        var locs = await db.BusLocations.Where(l => l.BusId == id).ToListAsync();
        db.BusLocations.RemoveRange(locs);
        db.Buses.Remove(b);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bitta avtobusning bir kunlik izi + to'xtashlari + jamlama.</summary>
    [HttpGet("buses/{id}/track")]
    public async Task<ActionResult<BusTrackDto>> Track(string id, [FromQuery] string? date)
    {
        var b = await db.Buses.FindAsync(id);
        if (b is null) return NotFound();
        var d = string.IsNullOrEmpty(date) || date.Length < 10 ? AppClock.Today.ToString("yyyy-MM-dd") : date[..10];

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var points = await db.BusLocations
            .Where(l => l.BusId == id && l.RecordedAt.StartsWith(d))
            .ToListAsync();

        return GpsService.Analyze(d, points, meta?.GpsStopRadiusM ?? 60, meta?.GpsStopMinMinutes ?? 3);
    }
}
