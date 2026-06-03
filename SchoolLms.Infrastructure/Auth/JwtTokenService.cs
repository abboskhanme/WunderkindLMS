using Microsoft.IdentityModel.Tokens;
using SchoolLms.Domain;
using SchoolLms.Domain.Platform;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SchoolLms.Infrastructure.Auth;

public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SchoolLms";
    public string Audience { get; set; } = "SchoolLms";
    public int ExpiresHours { get; set; } = 12;
}

public class JwtTokenService(JwtOptions options)
{
    private readonly JwtOptions _o = options;

    /// <summary>Maktab foydalanuvchisi uchun token. <paramref name="tenant"/> berilsa, tokenga
    /// <c>tenant</c> claim qo'shiladi — tenantlararo token qayta ishlatishni bloklash uchun
    /// (boshqa maktab subdomenida bu token rad etiladi).</summary>
    public string CreateToken(AppUser user, string? tenant = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
        };
        if (!string.IsNullOrEmpty(tenant)) claims.Add(new Claim("tenant", tenant));
        return Write(claims);
    }

    /// <summary>Loyiha boshlig'i (Control Plane) uchun token: role=platformowner, scope=platform.</summary>
    public string CreatePlatformToken(PlatformOwner owner)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, owner.Id),
            new(ClaimTypes.NameIdentifier, owner.Id),
            new(ClaimTypes.Name, owner.FullName),
            new(ClaimTypes.Email, owner.Email),
            new(ClaimTypes.Role, Roles.PlatformOwner),
            new("scope", "platform"),
        };
        return Write(claims);
    }

    private string Write(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _o.Issuer,
            audience: _o.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_o.ExpiresHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
