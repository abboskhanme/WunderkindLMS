using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtTokenService jwt, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
        {
            // Brute-force/credential-stuffing kuzatuvi uchun — IP va urinilgan login bilan.
            logger.LogWarning("Muvaffaqiyatsiz login urinishi: login={Login}, IP={IP}",
                req.Email, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?");
            return Unauthorized(new { message = "Login yoki parol noto'g'ri" });
        }

        // Arxivlangan o'qituvchi/o'quvchi qayta kira olmasin (token revocation bilan bir xil mantiq).
        if (await IsBlockedAsync(user))
            return Unauthorized(new { message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        // Login kuzatuvi: birinchi marta kirayotgan bo'lsa FirstLoginAt ham, har safar LastLoginAt yoziladi.
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (string.IsNullOrEmpty(user.FirstLoginAt)) user.FirstLoginAt = now;
        user.LastLoginAt = now;
        // Foydalanuvchi parolni ishlatdi — dastlabki ochiq parol endi superadmin'ga ko'rsatilmaydi.
        user.InitialPassword = null;
        await db.SaveChangesAsync();

        var token = jwt.CreateToken(user);
        return new LoginResponse(token, new UserDto(
            user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user)));
    }

    /// <summary>Akkaunt arxivlangan (o'qituvchi/o'quvchi) bo'lsa true — login va token rad etiladi.</summary>
    private async Task<bool> IsBlockedAsync(AppUser user) => user.Role switch
    {
        Roles.Teacher => !await db.Teachers.AnyAsync(t => t.UserId == user.Id && !t.IsArchived),
        Roles.Student => !await db.Students.AnyAsync(s => s.UserId == user.Id && !s.IsArchived),
        _ => false,
    };

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
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user));
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
            var taken = await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != user.Id);
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
