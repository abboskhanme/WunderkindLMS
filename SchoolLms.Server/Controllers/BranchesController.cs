using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Filiallar (branches) — faqat superadmin (tizim egasi). Filial nomi, manzil, GPS joylashuv va
/// radius saqlanadi (kelajakda mobil ilovada xodim/o'quvchi geo-yo'qlamasi uchun).
/// </summary>
[ApiController]
[Authorize(Roles = "superadmin")]
[Route("api/admin/branches")]
public class BranchesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<BranchDto>>> GetAll() =>
        (await db.Branches.OrderBy(b => b.Name).ToListAsync()).Select(ToDto).ToList();

    [HttpPost]
    public async Task<ActionResult<BranchDto>> Create(BranchPayload p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return BadRequest(new { message = "Filial nomi kerak" });
        var branch = new Branch
        {
            Name = p.Name.Trim(),
            Address = (p.Address ?? "").Trim(),
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            RadiusMeters = p.RadiusMeters,
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        return ToDto(branch);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<BranchDto>> Update(string id, BranchPayload p)
    {
        var branch = await db.Branches.FindAsync(id);
        if (branch is null) return NotFound();
        branch.Name = p.Name.Trim();
        branch.Address = (p.Address ?? "").Trim();
        branch.Latitude = p.Latitude;
        branch.Longitude = p.Longitude;
        branch.RadiusMeters = p.RadiusMeters;
        await db.SaveChangesAsync();
        return ToDto(branch);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var branch = await db.Branches.FindAsync(id);
        if (branch is null) return NotFound();
        db.Branches.Remove(branch);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static BranchDto ToDto(Branch b) =>
        new(b.Id, b.Name, b.Address, b.Latitude, b.Longitude, b.RadiusMeters, b.CreatedAt.ToString("o"));
}
