using Microsoft.IdentityModel.Tokens;
using SchoolLms.Server.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SchoolLms.Server.Auth;

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

    public string CreateToken(AppUser user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
        };

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
