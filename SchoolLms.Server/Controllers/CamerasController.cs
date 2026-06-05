using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Maktab kameralari (videokuzatuv) — Boshqaruv → Kameralar. CRUD + jonli oqim (HLS, media-shlyuz
/// orqali proksilanadi) + playback/qirqib yuklab olish (yozuvdan MP4). Shlyuz (MediaMTX) ichki
/// tarmoqda; brauzer faqat shu autentifikatsiyalangan endpointlar orqali ko'radi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/cameras")]
public class CamerasController(AppDbContext db, CameraGateway gateway) : ControllerBase
{
    private static CameraDto Dto(Camera c) =>
        new(c.Id, c.Name, c.Location, c.RtspUrl, c.RtspSubUrl, c.RetentionDays, c.IsActive, c.Note);

    private static int Retention(int days) => days < 0 ? 0 : days > 365 ? 365 : days;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CameraDto>>> List() =>
        (await db.Cameras.OrderBy(c => c.Name).ToListAsync()).Select(Dto).ToList();

    [HttpPost]
    public async Task<ActionResult<CameraDto>> Create(SaveCameraRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Nomi shart" });
        if (string.IsNullOrWhiteSpace(req.RtspUrl)) return BadRequest(new { message = "RTSP manzili shart" });
        var c = new Camera
        {
            Name = req.Name.Trim(),
            Location = (req.Location ?? "").Trim(),
            RtspUrl = req.RtspUrl.Trim(),
            RtspSubUrl = (req.RtspSubUrl ?? "").Trim(),
            RetentionDays = Retention(req.RetentionDays),
            IsActive = req.IsActive,
            Note = (req.Note ?? "").Trim(),
        };
        db.Cameras.Add(c);
        await db.SaveChangesAsync();
        await gateway.EnsureAsync(c);
        return Dto(c);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CameraDto>> Update(string id, SaveCameraRequest req)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Nomi shart" });
        if (string.IsNullOrWhiteSpace(req.RtspUrl)) return BadRequest(new { message = "RTSP manzili shart" });
        c.Name = req.Name.Trim();
        c.Location = (req.Location ?? "").Trim();
        c.RtspUrl = req.RtspUrl.Trim();
        c.RtspSubUrl = (req.RtspSubUrl ?? "").Trim();
        c.RetentionDays = Retention(req.RetentionDays);
        c.IsActive = req.IsActive;
        c.Note = (req.Note ?? "").Trim();
        await db.SaveChangesAsync();
        await gateway.EnsureAsync(c);
        return Dto(c);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        db.Cameras.Remove(c);
        await db.SaveChangesAsync();
        await gateway.RemoveAsync(id);
        return NoContent();
    }

    /// <summary>Jonli HLS pleylisti — shlyuzda kamerani yoqib, index.m3u8 ni proksilaydi.</summary>
    [HttpGet("{id}/index.m3u8")]
    public async Task<IActionResult> HlsIndex(string id)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        await gateway.EnsureAsync(c);
        return await ProxyAsync(await gateway.HlsAsync(id, "index.m3u8" + Request.QueryString.Value),
            "application/vnd.apple.mpegurl");
    }

    /// <summary>HLS segmenti (.ts) — proksilanadi. (index.m3u8/clip — alohida literal yo'llar.)</summary>
    [HttpGet("{id}/{file}")]
    public async Task<IActionResult> HlsSegment(string id, string file)
    {
        if (!file.EndsWith(".ts") && !file.EndsWith(".m3u8") && !file.EndsWith(".mp4"))
            return NotFound();
        return await ProxyAsync(await gateway.HlsAsync(id, file + Request.QueryString.Value), null);
    }

    /// <summary>Playback / qirqib olish — yozuvdan MP4. start ("yyyy-MM-ddTHH:mm:ss"), duration soniyada.
    /// download=1 bo'lsa fayl sifatida yuklab beriladi.</summary>
    [HttpGet("{id}/clip")]
    public async Task<IActionResult> Clip(
        string id, [FromQuery] string start, [FromQuery] int duration, [FromQuery] int download = 0)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        if (string.IsNullOrEmpty(start) || duration <= 0)
            return BadRequest(new { message = "start va duration kerak" });
        if (duration > 3600) duration = 3600; // himoya: maks 1 soat

        var resp = await gateway.PlaybackAsync(id, start, duration);
        if (!resp.IsSuccessStatusCode)
            return StatusCode(502, new { message = "Yozuv topilmadi yoki shlyuz javob bermadi" });

        var stream = await resp.Content.ReadAsStreamAsync();
        if (download == 1)
        {
            var fileName = $"{c.Name}_{start.Replace(":", "-")}_{duration}s.mp4";
            return File(stream, "video/mp4", fileName);
        }
        return File(stream, "video/mp4");
    }

    private async Task<IActionResult> ProxyAsync(HttpResponseMessage resp, string? contentType)
    {
        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode);
        var stream = await resp.Content.ReadAsStreamAsync();
        var ct = contentType ?? resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        return File(stream, ct);
    }
}
