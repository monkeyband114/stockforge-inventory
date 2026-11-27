using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace StockForge.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>The organization selected at last sign-in, used as the default next time.</summary>
    public Guid? LastTenantId { get; set; }

    public string DisplayName => $"{FirstName} {LastName}".Trim();
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ApplicationRole(string name, string description, bool isPlatformRole) : this()
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
        Description = description;
        IsPlatformRole = isPlatformRole;
    }

    public string? Description { get; set; }

    /// <summary>Platform roles (SuperAdmin) are granted globally; others are assigned per organization membership.</summary>
    public bool IsPlatformRole { get; set; }
}

/// <summary>
/// A signed-in device/browser. Access tokens carry the session id (<c>sid</c>); revoking the session
/// invalidates its access tokens within seconds (membership snapshot cache) and its refresh tokens immediately.
/// </summary>
public sealed class UserSession
{
    private UserSession() { }

    public UserSession(Guid userId, Guid? tenantId, string? ipAddress, string? userAgent, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TenantId = tenantId;
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent;
        CreatedAt = now;
        LastSeenAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Touch(DateTimeOffset now, DateTimeOffset expiresAt)
    {
        LastSeenAt = now;
        ExpiresAt = expiresAt;
    }

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is null)
        {
            RevokedAt = now;
            RevokedReason = reason;
        }
    }
}

/// <summary>
/// Opaque rotating refresh token. Only a SHA-256 hash is stored, so a database leak does not expose usable
/// tokens. Each use rotates the token; presenting an already-rotated token revokes the whole session.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { }

    private RefreshToken(Guid sessionId, Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? ip)
    {
        Id = Guid.CreateVersion7();
        SessionId = sessionId;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = ip;
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? CreatedByIp { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public static (string Token, RefreshToken Entity) Issue(Guid sessionId, Guid userId, DateTimeOffset now, DateTimeOffset expiresAt, string? ip)
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        return (token, new RefreshToken(sessionId, userId, Hash(token), now, expiresAt, ip));
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
