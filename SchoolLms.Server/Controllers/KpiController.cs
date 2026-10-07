using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Xodimlar samaradorligi — KPI (client, 2026-10-07). Gated read: scores of colleagues are not for
/// every staff member to browse.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm(KpiService.Permission, GatedRead = true)]
[Route("api/admin/kpi")]
public sealed class KpiController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<KpiReportDto>> Get([FromQuery] string? month, CancellationToken ct)
    {
        var m = string.IsNullOrWhiteSpace(month) ? AppClock.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture) : month.Trim();
        if (!DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return BadRequest(new { message = "Oy noto'g'ri (yyyy-MM)" });
        return await KpiService.BuildAsync(db, m, ct);
    }

    [HttpGet("weights")]
    public async Task<ActionResult<KpiWeightsDto>> Weights(CancellationToken ct) =>
        KpiService.ToDto(await KpiService.SettingsAsync(db, ct));

    [HttpPut("weights")]
    public async Task<ActionResult<KpiWeightsDto>> SaveWeights(KpiWeightsDto weights, CancellationToken ct)
    {
        var error = await KpiService.SaveWeightsAsync(db, weights, User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "", ct);
        if (error is not null) return BadRequest(new { message = error });
        return KpiService.ToDto(await KpiService.SettingsAsync(db, ct));
    }
}
