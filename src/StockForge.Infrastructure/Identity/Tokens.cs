using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StockForge.Infrastructure.Identity;

public static class StockForgeClaims
{
    public const string UserId = JwtRegisteredClaimNames.Sub;
    public const string SessionId = JwtRegisteredClaimNames.Sid;
    public const string TenantId = "tid";
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string Name = JwtRegisteredClaimNames.Name;
    public const string Role = "role";
    public const string PlatformAdmin = "pa";
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "stockforge";
    public string Audience { get; set; } = "stockforge-api";

    /// <summary>HMAC-SHA256 signing key, at least 32 bytes. Supply via environment/secret store in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Sliding lifetime of a refresh token / session.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Absolute maximum session lifetime regardless of refreshes.</summary>
    public int SessionMaxDays { get; set; } = 30;

    /// <summary>Window in which reusing a just-rotated refresh token is treated as a benign race (two tabs).</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 15;

    public SymmetricSecurityKey GetSigningKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues short-lived access tokens. Authorization data (permissions) is not embedded: it is
/// resolved per request from the membership snapshot, so role changes and revocations apply immediately.</summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(ApplicationUser user, Guid sessionId, Guid? tenantId, IEnumerable<string> roles, bool isPlatformAdmin)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(StockForgeClaims.UserId, user.Id.ToString()),
            new(StockForgeClaims.SessionId, sessionId.ToString()),
            new(StockForgeClaims.Email, user.Email ?? string.Empty),
            new(StockForgeClaims.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (tenantId is { } tid)
        {
            claims.Add(new Claim(StockForgeClaims.TenantId, tid.ToString()));
        }

        if (isPlatformAdmin)
        {
            claims.Add(new Claim(StockForgeClaims.PlatformAdmin, "true"));
        }

        claims.AddRange(roles.Select(r => new Claim(StockForgeClaims.Role, r)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.GetSigningKey(), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }

    public TokenValidationParameters CreateValidationParameters()
    {
        var jwt = options.Value;
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.GetSigningKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = StockForgeClaims.Name,
            RoleClaimType = StockForgeClaims.Role,
        };
    }
}
