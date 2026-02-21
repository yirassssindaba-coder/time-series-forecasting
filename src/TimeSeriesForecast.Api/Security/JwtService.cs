using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Core.Security;

namespace TimeSeriesForecast.Api.Security;

public sealed class JwtService
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _opt;

    public JwtService(AppDbContext db, IOptions<JwtOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public async Task<string> CreateAccessTokenAsync(AppUser user, CancellationToken ct, TimeSpan? lifetime = null)
    {
        // Avoid relying on EF navigation fixups; join explicitly.
        var roles = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .ToListAsync(ct);

        var permNames = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (_, p) => p.Name)
            .Distinct()
            .ToListAsync(ct);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("uid", user.Id.ToString()),
            new("email", user.Email),
        };

        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        foreach (var p in permNames) claims.Add(new Claim("perm", p));

        var keyString = string.IsNullOrWhiteSpace(_opt.Key) ? "CHANGE_ME" : _opt.Key;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(30));
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string refreshToken, string refreshHash) CreateRefreshToken(int bytes = 48)
    {
        var token = NewOpaqueToken(bytes);
        return (token, Sha256(token));
    }

    public string HashRefreshToken(string refreshToken) => Sha256(refreshToken);

    private static string NewOpaqueToken(int bytes)
    {
        var data = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Sha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
