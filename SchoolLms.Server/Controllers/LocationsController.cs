using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin uchun o'quvchilar joylashuvi (xarita) ma'lumotlari. O'quvchilar mobil ilovadan
/// o'z joylashuvini PUT /api/student/location orqali yuborishadi; bu yerda jamlanma ko'rinish.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/locations")]
public class LocationsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Joylashuvi mavjud o'quvchilar (lat/lng to'ldirilgan). Arxivlangan o'quvchilar chiqarilmaydi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentLocationRowDto>>> GetAll(
        [FromQuery] string? className = null)
    {
        var q = db.Students
            .Where(s => !s.IsArchived && s.Latitude != null && s.Longitude != null);
        if (!string.IsNullOrWhiteSpace(className))
            q = q.Where(s => s.ClassName == className);
        return await q
            .OrderBy(s => s.ClassName).ThenBy(s => s.FullName)
            .Select(s => new StudentLocationRowDto(
                s.Id, s.FullName, s.ClassName,
                s.Latitude!.Value, s.Longitude!.Value, s.LocationAddress, s.LocationUpdatedAt))
            .ToListAsync();
    }
}
