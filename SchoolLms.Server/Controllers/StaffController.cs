using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Xodimlar — o'qituvchi BO'LMAGAN ishchilar (kassir, administrator, ...). Har biriga admin
/// paneliga kiruvchi tizim akkaunti (role="staff") generatsiya qilinadi. Qaysi bo'limlarni
/// ko'rishi <see cref="AppUser.Permissions"/> bilan boshqariladi — uni FAQAT superadmin
/// (Rollar bo'limi / <see cref="SetPermissions"/>) o'zgartiradi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/staff")]
public class StaffController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaffDto>>> GetAll() =>
        (await db.Users.Where(u => u.Role == Roles.Staff).OrderBy(u => u.FullName).ToListAsync())
            .Select(ToDto).ToList();

    [HttpPost]
    public async Task<ActionResult<StaffDto>> Create(StaffPayload p)
    {
        if (string.IsNullOrWhiteSpace(p.FullName)) return BadRequest(new { message = "F.I.SH kerak" });
        var user = AccountFactory.CreateAccountFor(db, Roles.Staff, p.FullName.Trim());
        user.Position = (p.Position ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            if (p.NewPassword.Trim().Length < 4)
                return BadRequest(new { message = "Parol kamida 4 belgidan iborat bo'lsin" });
            user.PasswordHash = PasswordHasher.Hash(p.NewPassword.Trim());
            user.PlainPassword = p.NewPassword.Trim();
        }
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<StaffDto>> Update(string id, StaffPayload p)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        user.FullName = p.FullName.Trim();
        user.Position = (p.Position ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            if (p.NewPassword.Trim().Length < 4)
                return BadRequest(new { message = "Parol kamida 4 belgidan iborat bo'lsin" });
            user.PasswordHash = PasswordHasher.Hash(p.NewPassword.Trim());
            user.PlainPassword = p.NewPassword.Trim();
        }
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Xodim akkaunti (login/parol).</summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        return new CredentialsDto(user.Email, user.PlainPassword ?? "", user.Role);
    }

    /// <summary>Xodimning admin bo'lim ruxsatlari (Rollar) — FAQAT superadmin.</summary>
    [HttpPut("{id}/permissions")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult<StaffDto>> SetPermissions(string id, SetStaffPermissionsRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        user.Permissions = req.Permissions ?? new();
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    private static StaffDto ToDto(AppUser u) =>
        new(u.Id, u.FullName, u.Position, u.Email, u.Permissions);
}
