using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtTokenService jwt, ITenantContext tenant, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        // Loginlar BUTUN baza bo'ylab unikal (AccountFactory). Shuning uchun maktab kodi (X-Tenant/
        // subdomen) berilmasa ham — login bo'yicha foydalanuvchini va uning maktabini topa olamiz.
        // Maktab kodi berilgan bo'lsa (masalan ota-ona telefoni bir nechta maktabda) — shu maktab ichida.
        var candidates = tenant.IsPlatform
            ? await db.Users.IgnoreQueryFilters().Where(u => u.Email == req.Email).ToListAsync()
            : await db.Users.Where(u => u.Email == req.Email).ToListAsync();
        var matched = candidates.Where(u => PasswordHasher.Verify(req.Password, u.PasswordHash)).ToList();

        if (matched.Count == 0)
        {
            // Brute-force/credential-stuffing kuzatuvi uchun — IP va urinilgan login bilan.
            logger.LogWarning("Muvaffaqiyatsiz login urinishi: login={Login}, IP={IP}",
                req.Email, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?");
            return Unauthorized(new { message = "Login yoki parol noto'g'ri" });
        }
        if (matched.Count > 1)
        {
            // Kamdan-kam: bir xil login+parol bir nechta maktabda. Maktabni so'raymiz (X-Tenant bilan qayta login).
            var ids = matched.Select(u => (string?)db.Entry(u).Property("TenantId").CurrentValue).ToList();
            var schools = await db.Tenants.IgnoreQueryFilters()
                .Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Slug, t.Name }).ToListAsync();
            return Conflict(new { message = "Bu login bir nechta maktabda mavjud. Maktabni tanlang.", schools });
        }

        var user = matched[0];

        // Arxivlangan o'qituvchi/o'quvchi qayta kira olmasin (token revocation bilan bir xil mantiq).
        if (await IsBlockedAsync(user))
            return Unauthorized(new { message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        // Foydalanuvchining maktabini aniqlab, joriy so'rov kontekstini shunga sozlaymiz
        // (PermsFor va token claim to'g'ri maktabga tegishli bo'lishi uchun).
        var tid = (string?)db.Entry(user).Property("TenantId").CurrentValue;
        var trow = string.IsNullOrEmpty(tid) ? null
            : await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tid);
        if (trow is not null) tenant.SetTenant(trow.Id, trow.Slug);

        // Login kuzatuvi: birinchi marta kirayotgan bo'lsa FirstLoginAt ham, har safar LastLoginAt yoziladi.
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (string.IsNullOrEmpty(user.FirstLoginAt)) user.FirstLoginAt = now;
        user.LastLoginAt = now;
        // Foydalanuvchi parolni ishlatdi — dastlabki ochiq parol endi superadmin'ga ko'rsatilmaydi.
        user.InitialPassword = null;
        await db.SaveChangesAsync();

        var token = jwt.CreateToken(user, trow?.Slug);
        var modules = trow is { EnabledModules.Count: > 0 } ? trow.EnabledModules : null;
        return new LoginResponse(token, new UserDto(
            user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user), modules));
    }

    /// <summary>Akkaunt arxivlangan (o'qituvchi/o'quvchi) bo'lsa true — login va token rad etiladi.
    /// Filtrlardan xoli: tenant kontekstidan mustaqil, GUID id'lar global unikal.</summary>
    private async Task<bool> IsBlockedAsync(AppUser user) => user.Role switch
    {
        Roles.Teacher => !await db.Teachers.IgnoreQueryFilters()
            .AnyAsync(t => t.UserId == user.Id && !t.IsArchived),
        Roles.Student => !await db.Students.IgnoreQueryFilters()
            .AnyAsync(s => s.UserId == user.Id && !s.IsArchived),
        _ => false,
    };

    /// <summary>Joriy maktab obunasida ochilgan bo'limlar (null = cheklovsiz yoki platform).</summary>
    private async Task<List<string>?> CurrentTenantModules()
    {
        if (tenant.IsPlatform || string.IsNullOrEmpty(tenant.TenantId)) return null;
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == tenant.TenantId);
        return t is { EnabledModules.Count: > 0 } ? t.EnabledModules : null;
    }

    /// <summary>
    /// Ruxsat etilgan bo'limlar: o'qituvchi → Teacher.Permissions; xodim → AppUser.Permissions
    /// (admin bo'limlari); admin/superadmin/o'quvchi → null (cheklov yo'q / kerak emas).
    /// </summary>
    private async Task<List<string>?> PermsFor(AppUser user) => user.Role switch
    {
        Roles.Teacher => (await db.Teachers.FirstOrDefaultAsync(t => t.UserId == user.Id))?.Permissions,
        Roles.Staff => user.Permissions,
        _ => null,
    };

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await db.Users.FindAsync(id);
        if (user is null) return Unauthorized();
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl,
            await PermsFor(user), await CurrentTenantModules());
    }

    /// <summary>Joriy foydalanuvchi o'z login (email) va/yoki parolini o'zgartiradi.
    /// Joriy parol bilan tasdiqlanadi. Email/Id o'zgarmagani uchun token amal qilaveradi.</summary>
    [HttpPut("account")]
    [Authorize]
    public async Task<ActionResult<UserDto>> UpdateAccount(UpdateAccountRequest req)
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await db.Users.FindAsync(id);
        if (user is null) return Unauthorized();

        if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Joriy parol noto'g'ri" });

        var newEmail = req.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(newEmail) && newEmail != user.Email)
        {
            // Login BUTUN baza bo'ylab unikal (maktab kodisiz login uchun).
            var taken = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == newEmail && u.Id != user.Id);
            if (taken) return BadRequest(new { message = "Bu login allaqachon band" });
            user.Email = newEmail;
        }

        if (!string.IsNullOrEmpty(req.NewPassword))
        {
            if (req.NewPassword.Length < 8)
                return BadRequest(new { message = "Yangi parol kamida 8 belgidan iborat bo'lsin" });
            user.SetOwnPassword(req.NewPassword);
        }

        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl);
    }
}
