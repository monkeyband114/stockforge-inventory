using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using StockForge.Application.Security;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Identity;

/// <summary>
/// Everything needed to authorize a request, resolved from the database (and cached briefly) rather than
/// trusted from the token: session validity, user/tenant/membership status, roles, permissions and
/// warehouse scope.
/// </summary>
public sealed record MembershipSnapshot(
    Guid UserId,
    Guid SessionId,
    Guid? TenantId,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin,
    string[] Roles,
    string[] Permissions,
    Guid[]? WarehouseIds);

public interface IMembershipSnapshotProvider
{
    Task<MembershipSnapshot?> GetAsync(Guid sessionId, Guid userId, Guid? tenantId, CancellationToken cancellationToken);

    Task<MembershipSnapshot?> GetForPrincipalAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    Task InvalidateSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken);
}

internal sealed class MembershipSnapshotProvider(
    AppDbContext db,
    IDistributedCache cache,
    TimeProvider clock) : IMembershipSnapshotProvider, Application.Abstractions.ISessionCache
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) };

    public async Task<MembershipSnapshot?> GetForPrincipalAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(StockForgeClaims.UserId), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue(StockForgeClaims.SessionId), out var sessionId))
        {
            return null;
        }

        Guid? tenantId = null;
        var tid = principal.FindFirstValue(StockForgeClaims.TenantId);
        if (tid is not null)
        {
            if (!Guid.TryParse(tid, out var parsed))
            {
                return null;
            }

            tenantId = parsed;
        }

        return await GetAsync(sessionId, userId, tenantId, cancellationToken);
    }

    public async Task<MembershipSnapshot?> GetAsync(Guid sessionId, Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var key = Key(sessionId);
        var cached = await cache.GetStringAsync(key, cancellationToken);
        var snapshot = cached is null ? null : JsonSerializer.Deserialize<MembershipSnapshot>(cached);
        if (snapshot is null)
        {
            snapshot = await LoadAsync(sessionId, cancellationToken);
            if (snapshot is not null)
            {
                await cache.SetStringAsync(key, JsonSerializer.Serialize(snapshot), CacheOptions, cancellationToken);
            }
        }

        // The token must match the session it claims to belong to.
        return snapshot is not null && snapshot.UserId == userId && snapshot.TenantId == tenantId ? snapshot : null;
    }

    public Task InvalidateSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(Key(sessionId), cancellationToken);

    public async Task InvalidateUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sessionIds = await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in sessionIds)
        {
            await cache.RemoveAsync(Key(id), cancellationToken);
        }
    }

    public async Task InvalidateTenantSessionsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var sessionIds = await db.UserSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.RevokedAt == null)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in sessionIds)
        {
            await cache.RemoveAsync(Key(id), cancellationToken);
        }
    }

    private async Task<MembershipSnapshot?> LoadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = await db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || !session.IsActive(now))
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null || !user.IsActive || (user.LockoutEnd is { } lockout && lockout > now))
        {
            return null;
        }

        var platformRoles = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                where ur.UserId == user.Id && r.IsPlatformRole
                select r.Name!)
            .ToListAsync(cancellationToken);
        var isPlatformAdmin = platformRoles.Contains(RoleNames.SuperAdmin);

        var roles = new List<string>(platformRoles);
        var granted = new List<string>();
        Guid[]? warehouseIds = null;

        if (session.TenantId is { } tenantId)
        {
            var membership = await db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
                .Include(m => m.WarehouseAssignments)
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == user.Id, cancellationToken);
            var tenantActive = await db.Tenants.AsNoTracking()
                .AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, cancellationToken);
            if (membership is null || !membership.IsActive || !tenantActive)
            {
                return null;
            }

            roles.AddRange(membership.Roles);
            granted.AddRange(membership.GrantedPermissions);
            warehouseIds = membership.WarehouseAssignments.Select(a => a.WarehouseId).ToArray();
        }

        var permissions = await (
                from rc in db.RoleClaims
                join r in db.Roles on rc.RoleId equals r.Id
                where roles.Contains(r.Name!) && rc.ClaimType == Permissions.ClaimType
                select rc.ClaimValue!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var allPermissions = permissions.Concat(granted).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        // Members who may see every warehouse are not restricted by assignments.
        if (allPermissions.Contains(Permissions.Inventory.ViewAllWarehouses) || session.TenantId is null)
        {
            warehouseIds = null;
        }

        return new MembershipSnapshot(user.Id, session.Id, session.TenantId, user.Email ?? string.Empty, user.DisplayName,
            isPlatformAdmin, roles.Distinct().ToArray(), allPermissions, warehouseIds);
    }

    private static string Key(Guid sessionId) => $"sf:session:{sessionId:N}";
}

/// <summary>Display-name lookups and member queries backed by the Identity store.</summary>
internal sealed class UserDirectory(AppDbContext db, Application.Abstractions.ITenantContext tenantContext) : Application.Abstractions.IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);
    }

    public Task<int> CountUsersAsync(CancellationToken cancellationToken) => db.Users.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetMemberEmailsAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequiredTenantId;
        var roleArray = roles.ToArray();
        return await (
                from m in db.TenantMemberships.IgnoreQueryFilters()
                join u in db.Users on m.UserId equals u.Id
                where m.TenantId == tenantId && m.IsActive && u.IsActive && u.Email != null && m.Roles.Any(r => roleArray.Contains(r))
                select u.Email!)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}

internal static class IdentityResultExtensions
{
    public static void EnsureSucceeded(this IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new FluentValidation.ValidationException(result.Errors.Select(e =>
                new FluentValidation.Results.ValidationFailure(e.Code.Contains("Password", StringComparison.Ordinal) ? "Password" : "User", e.Description)));
        }
    }
}
