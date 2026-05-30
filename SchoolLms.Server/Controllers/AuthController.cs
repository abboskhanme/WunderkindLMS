using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtTokenService jwt) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Email yoki parol noto'g'ri" });

        // Login kuzatuvi: birinchi marta kirayotgan bo'lsa FirstLoginAt ham, har safar LastLoginAt
        // yoziladi. "Ilova aktivlashtirilgan" ota-onalar bo'limida shu maydon orqali aniqlanadi.
        var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (string.IsNullOrEmpty(user.FirstLoginAt)) user.FirstLoginAt = now;
        user.LastLoginAt = now;
        await db.SaveChangesAsync();

        var token = jwt.CreateToken(user);
        return new LoginResponse(token, new UserDto(
            user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user)));
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
            if (req.NewPassword.Length < 4)
                return BadRequest(new { message = "Yangi parol kamida 4 belgidan iborat bo'lsin" });
            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            user.PlainPassword = null; // o'zi tanlagan parol — boshqa joyda ko'rsatilmaydi
        }

        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl);
    }
}
