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
        if (await SessionFactory.IsBlockedAsync(db, user))
            return Unauthorized(new { message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        // Kirish qaydi va token — YAGONA joyda (SessionFactory). Telegram Mini App
        // (`/api/tg/auth`) ham aynan shu yo'ldan yuradi, ya'ni ikkita "login"
        // xatti-harakati bir-biridan uzoqlashib keta olmaydi.
        return await SessionFactory.IssueAsync(db, jwt, user);
    }

    /// <summary>Ruxsat etilgan bo'limlar — <see cref="SessionFactory.PermissionsAsync"/> ning qisqartmasi.</summary>
    private Task<List<string>?> PermsFor(AppUser user) => SessionFactory.PermissionsAsync(db, user);

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
        // Ruxsatlar ham qaytadi: klient javobni `updateUser` bilan saqlaydi — ro'yxatsiz
        // qaytsa, xodimning menyusi "hammasi ochiq" bo'lib ko'rinib qolardi.
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user));
    }

    /// <summary>Profil rasmi uchun ruxsat etilgan eng katta hajm.</summary>
    private const long MaxAvatarBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AvatarExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".heic" };

    /// <summary>
    /// O'z profil rasmini yuklash — tizimga kiradigan HAR QANDAY foydalanuvchi uchun
    /// (mijoz, 2026-09-22: "barcha uchun profile image yuklash mumkin bo'lsin"). Faqat
    /// rasm; parol so'ralmaydi, chunki rasm kirish ma'lumoti emas.
    /// </summary>
    [HttpPost("avatar")]
    [Authorize]
    [RequestSizeLimit(MaxAvatarBytes + 64 * 1024)]
    public async Task<ActionResult<UserDto>> UploadAvatar(IFormFile file, [FromServices] IWebHostEnvironment env)
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await db.Users.FindAsync(id);
        if (user is null) return Unauthorized();

        if (Application.Services.UploadGuard.Validate(file) is { } error)
            return BadRequest(new { message = error });
        if (!AvatarExtensions.Contains(System.IO.Path.GetExtension(file.FileName)))
            return BadRequest(new { message = "Faqat rasm yuklash mumkin (jpg, png, webp, heic)" });
        if (file.Length > MaxAvatarBytes)
            return BadRequest(new { message = "Rasm 5 MB dan katta bo'lmasin" });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        user.AvatarUrl = $"/uploads/{stored}";
        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user));
    }

    /// <summary>Profil rasmini olib tashlash (fayl diskda qoladi — boshqa joyda ishlatilgan bo'lishi mumkin).</summary>
    [HttpDelete("avatar")]
    [Authorize]
    public async Task<ActionResult<UserDto>> RemoveAvatar()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await db.Users.FindAsync(id);
        if (user is null) return Unauthorized();
        user.AvatarUrl = null;
        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user));
    }
}
