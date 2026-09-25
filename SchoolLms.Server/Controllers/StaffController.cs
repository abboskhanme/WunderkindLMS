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
/// ("Xodimlar va rollar" bo'limi / <see cref="SetPermissions"/>) o'zgartiradi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("staff")]
[Route("api/admin/staff")]
public class StaffController(AppDbContext db) : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";


    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaffDto>>> GetAll()
    {
        var roleNames = await RoleNamesAsync();
        return (await db.Users.Where(u => u.Role == Roles.Staff).OrderBy(u => u.FullName).ToListAsync())
            .Select(u => ToDto(u, roleNames)).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StaffDto>> Create(StaffPayload p)
    {
        if (string.IsNullOrWhiteSpace(p.FullName)) return BadRequest(new { message = "F.I.SH kerak" });
        var user = AccountFactory.CreateAccountFor(db, Roles.Staff, p.FullName.Trim());
        user.Position = (p.Position ?? "").Trim();
        if (ApplyAvatar(user, p.AvatarUrl) is { } avatarError) return BadRequest(new { message = avatarError });
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            if (p.NewPassword.Trim().Length < MinPasswordLength)
                return BadRequest(new { message = WeakPasswordMessage });
            user.SetInitialPassword(p.NewPassword.Trim());
        }
        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<StaffDto>> Update(string id, StaffPayload p)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        user.FullName = p.FullName.Trim();
        user.Position = (p.Position ?? "").Trim();
        if (ApplyAvatar(user, p.AvatarUrl) is { } avatarError) return BadRequest(new { message = avatarError });
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            if (p.NewPassword.Trim().Length < MinPasswordLength)
                return BadRequest(new { message = WeakPasswordMessage });
            user.SetInitialPassword(p.NewPassword.Trim());
        }
        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
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

    /// <summary>Xodim akkaunti logini. Parol xavfsizlik uchun saqlanmaydi — bo'sh qaytadi
    /// (ko'rsatish kerak bo'lsa <see cref="ResetPassword"/> orqali yangisini yarating).</summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        return new CredentialsDto(user.Email, user.InitialPassword ?? "", user.Role);
    }

    /// <summary>Xodimga yangi tasodifiy parol generatsiya qiladi va uni BIR MARTA qaytaradi
    /// (DB'da faqat hash saqlanadi).</summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        var pwd = AccountFactory.GeneratePassword();
        user.SetInitialPassword(pwd);
        await db.SaveChangesAsync();
        return new CredentialsDto(user.Email, pwd, user.Role);
    }

    /// <summary>Xodimning admin bo'lim ruxsatlari (Rollar) — FAQAT superadmin.</summary>
    [HttpPut("{id}/permissions")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult<StaffDto>> SetPermissions(string id, SetStaffPermissionsRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        user.Permissions = req.Permissions ?? new();
        // Hand-picked permissions no longer follow a role.
        user.AccessRoleId = null;
        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
    }

    /// <summary>
    /// Assigns an access role (Boshqaruv → Rollar) — FAQAT superadmin, like permissions.
    /// The member's permissions become a copy of the role's; removing the role clears them.
    /// </summary>
    [HttpPut("{id}/role")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult<StaffDto>> SetRole(string id, SetStaffRoleRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();

        if (req.AccessRoleId is { } roleId)
        {
            var role = await db.AccessRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId);
            if (role is null) return BadRequest(new { message = "Rol topilmadi" });
            user.AccessRoleId = role.Id;
            user.Permissions = [.. role.Permissions];
        }
        else
        {
            user.AccessRoleId = null;
            user.Permissions = new();
        }

        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
    }

    private Task<Dictionary<Guid, string>> RoleNamesAsync() =>
        db.AccessRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);

    private static StaffDto ToDto(AppUser u, IReadOnlyDictionary<Guid, string> roleNames) =>
        new(u.Id, u.FullName, u.Position, u.Email, u.Permissions, u.AvatarUrl,
            u.AccessRoleId,
            u.AccessRoleId is { } rid ? roleNames.GetValueOrDefault(rid) : null,
            u.LastLoginAt);

    /// <summary>
    /// Rasm manzilini qo'llaydi. Faqat o'zimizning yuklangan fayl (<c>/uploads/…</c>) —
    /// begona URL (kuzatuv pikseli, boshqa sayt) avatar sifatida ko'rsatilmasin.
    /// </summary>
    private static string? ApplyAvatar(AppUser user, string? url)
    {
        if (url is null) return null;
        var v = url.Trim();
        if (v.Length == 0) { user.AvatarUrl = null; return null; }
        if (!v.StartsWith("/uploads/", StringComparison.Ordinal) || v.Contains("..") || v.Length > 300)
            return "Rasm manzili noto'g'ri";
        user.AvatarUrl = v;
        return null;
    }
}
