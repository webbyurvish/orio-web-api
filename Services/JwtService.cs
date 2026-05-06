using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PKeetDashboard.API.Entities;

namespace PKeetDashboard.API.Services;

public class JwtService
{
    private readonly IConfiguration _configuration;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user)
    {
        var jwt = _configuration.GetSection("JwtSettings");
        var secretKey = (jwt["SecretKey"] ?? "").Trim();
        if (Encoding.UTF8.GetByteCount(secretKey) < 32)
            throw new InvalidOperationException(
                "JwtSettings:SecretKey must be at least 32 UTF-8 bytes for HS256. Lengthen JwtSettings__SecretKey in your environment.");

        var issuer = jwt["Issuer"] ?? "PKeetDashboardAPI";
        var audience = jwt["Audience"] ?? "PKeetDashboardClient";
        var expiryMinutes = int.Parse(jwt["ExpiryMinutes"] ?? "1440");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var list = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("is_admin", user.IsAdmin ? "true" : "false"),
        };
        var claims = list.ToArray();

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
