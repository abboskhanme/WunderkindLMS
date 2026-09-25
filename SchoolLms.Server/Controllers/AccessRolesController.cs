using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Boshqaruv → Rollar: staff access roles. Permissions are granted to a role; staff
/// members are only assigned one (<see cref="StaffController.SetRole"/>).
///
/// <para>
/// Reading is open to whoever may open the staff section (the staff page needs the
/// role list). Creating, changing and deleting a role is superadmin-only — exactly
/// as editing per-user permissions was — because a role IS a grant of access.
/// </para>
/// <para>
/// Saving a role rewrites <see cref="AppUser.Permissions"/> of every member in the
/// same transaction, so the change applies on their next request (claims are
/// loaded from the DB per request), with no re-login.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("staff")]
[Route("api/admin/roles")]
public class AccessRolesController(AppDbContext db) : ControllerBase
{
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxPermissionLength = 64;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccessRoleDto>>> GetAll()
    {
        var counts = await db.Users
            .Where(u => u.Role == Roles.Staff && u.AccessRoleId != null)
            .GroupBy(u => u.AccessRoleId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return (await db.AccessRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync())
            .Select(r => ToDto(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
    }

    [HttpPost]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<ActionResult<AccessRoleDto>> Create(AccessRolePayload p)
    {
        if (Validate(p) is { } error) return BadRequest(new { message = error });
        var name = p.Name.Trim();
        if (await db.AccessRoles.AnyAsync(r => r.Name == name))
            return Conflict(new { message = $"\"{name}\" nomli rol allaqachon bor" });

        var role = new AccessRole
        {
            Name = name,
            Description = (p.Description ?? "").Trim(),
            Permissions = Clean(p.Permissions),
        };
        db.AccessRoles.Add(role);
        await db.SaveChangesAsync();
        return ToDto(role, 0);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<ActionResult<AccessRoleDto>> Update(Guid id, AccessRolePayload p)
    {
        var role = await db.AccessRoles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null) return NotFound();
        if (Validate(p) is { } error) return BadRequest(new { message = error });
        var name = p.Name.Trim();
        if (await db.AccessRoles.AnyAsync(r => r.Name == name && r.Id != id))
            return Conflict(new { message = $"\"{name}\" nomli rol allaqachon bor" });

        role.Name = name;
        role.Description = (p.Description ?? "").Trim();
        role.Permissions = Clean(p.Permissions);

        // Members get the new permission set in the same save.
        var members = await db.Users.Where(u => u.Role == Roles.Staff && u.AccessRoleId == id).ToListAsync();
        foreach (var m in members) m.Permissions = [.. role.Permissions];

        await db.SaveChangesAsync();
        return ToDto(role, members.Count);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var role = await db.AccessRoles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null) return NotFound();

        var members = await db.Users.CountAsync(u => u.AccessRoleId == id);
        if (members > 0)
            return Conflict(new { message = $"Bu rolda {members} ta xodim bor. Avval ularga boshqa rol bering." });

        db.AccessRoles.Remove(role);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string? Validate(AccessRolePayload p)
    {
        var name = (p.Name ?? "").Trim();
        if (name.Length == 0) return "Rol nomi kerak";
        if (name.Length > MaxNameLength) return $"Rol nomi {MaxNameLength} belgidan oshmasin";
        if ((p.Description ?? "").Trim().Length > MaxDescriptionLength)
            return $"Izoh {MaxDescriptionLength} belgidan oshmasin";
        return null;
    }

    /// <summary>Trimmed, non-empty, distinct keys, in the order given.</summary>
    private static List<string> Clean(List<string>? keys) =>
        (keys ?? [])
            .Select(k => (k ?? "").Trim())
            .Where(k => k.Length is > 0 and <= MaxPermissionLength)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static AccessRoleDto ToDto(AccessRole r, int staffCount) =>
        new(r.Id, r.Name, r.Description, r.Permissions, staffCount);
}
