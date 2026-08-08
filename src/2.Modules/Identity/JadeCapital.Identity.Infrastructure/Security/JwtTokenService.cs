using JadeCapital.Identity.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace JadeCapital.Identity.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _opts;
    private readonly IClock _clock;

    public JwtTokenService(IOptions<JwtOptions> opts, IClock clock)
    {
        _opts = opts.Value;
        _clock = clock;
        Validate();
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_opts.AccessTokenSecret) || _opts.AccessTokenSecret.Length < 32)
            throw new InvalidOperationException("Jwt.AccessTokenSecret must be >= 32 chars.");
        if (string.IsNullOrWhiteSpace(_opts.RefreshTokenSecret) || _opts.RefreshTokenSecret.Length < 32)
            throw new InvalidOperationException("Jwt.RefreshTokenSecret must be >= 32 chars.");
        if (string.Equals(_opts.AccessTokenSecret, _opts.RefreshTokenSecret, StringComparison.Ordinal))
            throw new InvalidOperationException("Jwt.AccessTokenSecret and RefreshTokenSecret must differ.");
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(
        Guid userId, string email, string role, IEnumerable<string>? extraClaims = null)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_opts.AccessTokenTtlMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };
        if (extraClaims is not null)
            foreach (var c in extraClaims)
                claims.Add(new Claim("scope", c));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.AccessTokenSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string CreateOpaqueRefreshToken()
    {
        var bytes = new byte[48];
        RandomNumberGenerator.Fill(bytes);
        // URL-safe base64 sin padding.
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string HashToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}