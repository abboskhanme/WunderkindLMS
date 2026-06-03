using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

public record PlatformLoginRequest(string Email, string Password);
public record PlatformOwnerDto(string Id, string FullName, string Email);
public record PlatformLoginResponse(string Token, PlatformOwnerDto Owner);
public record UpdatePlatformAccountRequest(string? FullName, string? Email, string? CurrentPassword, string? NewPassword);

/// <summary>
/// Control Plane (asosiy domen) autentifikatsiyasi — loyiha boshlig'i (createadmin) logini.
/// Maktab <c>AuthController</c>'idan ALOHIDA: Platform DB'dagi PlatformOwner'larni tekshiradi.
/// </summary>
[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController(AppDbContext db, JwtTokenService jwt, ILogger<PlatformAuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<PlatformLoginResponse>> Login(PlatformLoginRequest req)
    {
        var owner = await db.Owners.FirstOrDefaultAsync(o => o.Email == req.Email);
        if (owner is null || !PasswordHasher.Verify(req.Password, owner.PasswordHash))
        {
            logger.LogWarning("Platform login muvaffaqiyatsiz: login={Login}, IP={IP}",
                req.Email, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?");
            return Unauthorized(new { message = "Login yoki parol noto'g'ri" });
        }

        owner.LastLoginAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        await db.SaveChangesAsync();

        return new PlatformLoginResponse(jwt.CreatePlatformToken(owner),
            new PlatformOwnerDto(owner.Id, owner.FullName, owner.Email));
    }

    [HttpGet("me")]
    [Authorize(Roles = Roles.PlatformOwner)]
    public async Task<ActionResult<PlatformOwnerDto>> Me()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var owner = await db.Owners.FindAsync(id);
        if (owner is null) return Unauthorized();
        return new PlatformOwnerDto(owner.Id, owner.FullName, owner.Email);
    }

    /// <summary>Loyiha boshlig'i o'z ismi, logini (email) va/yoki parolini o'zgartiradi.
    /// Parol almashtirilsa, joriy parol bilan tasdiqlanadi. Id o'zgarmagani uchun token amal qilaveradi.</summary>
    [HttpPut("account")]
    [Authorize(Roles = Roles.PlatformOwner)]
    public async Task<ActionResult<PlatformOwnerDto>> UpdateAccount(UpdatePlatformAccountRequest req)
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var owner = await db.Owners.FindAsync(id);
        if (owner is null) return Unauthorized();

        if (!string.IsNullOrWhiteSpace(req.FullName))
            owner.FullName = req.FullName.Trim();

        var newEmail = req.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(newEmail) && newEmail != owner.Email)
        {
            var taken = await db.Owners.AnyAsync(o => o.Email == newEmail && o.Id != owner.Id);
            if (taken) return BadRequest(new { message = "Bu login allaqachon band" });
            owner.Email = newEmail;
        }

        if (!string.IsNullOrEmpty(req.NewPassword))
        {
            if (string.IsNullOrEmpty(req.CurrentPassword) || !PasswordHasher.Verify(req.CurrentPassword, owner.PasswordHash))
                return BadRequest(new { message = "Joriy parol noto'g'ri" });
            if (req.NewPassword.Length < 8)
                return BadRequest(new { message = "Yangi parol kamida 8 belgidan iborat bo'lsin" });
            owner.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        }

        await db.SaveChangesAsync();
        return new PlatformOwnerDto(owner.Id, owner.FullName, owner.Email);
    }
}
