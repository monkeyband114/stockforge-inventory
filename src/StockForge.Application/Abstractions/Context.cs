namespace StockForge.Application.Abstractions;

/// <summary>
/// The tenant of the current execution scope. In the API it comes exclusively from the validated access token
/// and membership snapshot; in the worker it is set per job from the outbox message.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    bool HasTenant => TenantId.HasValue;

    /// <summary>The tenant id, or an exception if the scope has no tenant (fails closed).</summary>
    Guid RequiredTenantId => TenantId ?? throw new ForbiddenException("No organization is selected for this request.");
}

/// <summary>The authenticated principal of the current execution scope.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    Guid? SessionId { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    IReadOnlySet<string> Permissions { get; }

    /// <summary>Warehouses the user may operate on; <c>null</c> means all warehouses of the tenant.</summary>
    IReadOnlySet<Guid>? AccessibleWarehouseIds { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }

    bool HasPermission(string permission) => Permissions.Contains(permission);

    Guid RequiredUserId => UserId ?? throw new ForbiddenException("Authentication is required.");
}

/// <summary>Mutable implementation populated by the API middleware or by the worker's job scope.</summary>
public sealed class ScopeContext : ITenantContext, ICurrentUser
{
    private static readonly IReadOnlySet<string> NoPermissions = new HashSet<string>();

    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? DisplayName { get; private set; }
    public Guid? SessionId { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;
    public bool IsPlatformAdmin { get; private set; }
    public IReadOnlySet<string> Permissions { get; private set; } = NoPermissions;
    public IReadOnlySet<Guid>? AccessibleWarehouseIds { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }

    /// <summary>Whether the scope runs on behalf of the system (worker, seeding) rather than a user.</summary>
    public bool IsSystem { get; private set; }

    public void SetRequestInfo(string? ipAddress, string? userAgent, string? correlationId)
    {
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CorrelationId = correlationId;
    }

    public void SetUser(
        Guid userId,
        string? email,
        string? displayName,
        Guid? sessionId,
        bool isPlatformAdmin,
        Guid? tenantId,
        IReadOnlySet<string> permissions,
        IReadOnlySet<Guid>? accessibleWarehouseIds)
    {
        UserId = userId;
        Email = email;
        DisplayName = displayName;
        SessionId = sessionId;
        IsPlatformAdmin = isPlatformAdmin;
        TenantId = tenantId;
        Permissions = permissions;
        AccessibleWarehouseIds = accessibleWarehouseIds;
    }

    /// <summary>
    /// Enters a tenant for trusted server-side flows (onboarding, worker jobs, seeding). Refuses to switch a
    /// scope that already belongs to a different tenant.
    /// </summary>
    public void EnterTenant(Guid tenantId)
    {
        if (TenantId.HasValue && TenantId != tenantId)
        {
            throw new InvalidOperationException("The execution scope already belongs to another tenant.");
        }

        TenantId = tenantId;
    }

    public void SetSystem(Guid? tenantId, string? correlationId)
    {
        IsSystem = true;
        TenantId = tenantId;
        CorrelationId = correlationId;
        AccessibleWarehouseIds = null;
    }
}
