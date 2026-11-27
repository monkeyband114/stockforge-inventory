using StockForge.Domain.Common;

namespace StockForge.Domain.Tenancy;

/// <summary>
/// Links a user to an organization with a set of tenant-scoped roles, optional extra permission grants and
/// warehouse assignments. A user may belong to several organizations.
/// </summary>
public sealed class TenantMembership : AuditableEntity, ITenantOwned, IAuditable
{
    private readonly List<WarehouseAssignment> _warehouseAssignments = [];

    private TenantMembership() { }

    public TenantMembership(Guid tenantId, Guid userId, IEnumerable<string> roles, string? jobTitle, DateTimeOffset joinedAt)
    {
        TenantId = Guard.NotEmpty(tenantId, "Tenant");
        UserId = Guard.NotEmpty(userId, "User");
        SetRoles(roles);
        JobTitle = Guard.Optional(jobTitle, "Job title", 120);
        JoinedAt = joinedAt;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsActive { get; private set; }
    public string? JobTitle { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public List<string> Roles { get; private set; } = [];

    /// <summary>Permissions granted to this member in addition to those implied by their roles.</summary>
    public List<string> GrantedPermissions { get; private set; } = [];

    public IReadOnlyCollection<WarehouseAssignment> WarehouseAssignments => _warehouseAssignments;
    public uint Version { get; private set; }

    public void SetRoles(IEnumerable<string> roles)
    {
        var distinct = roles.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0)
        {
            throw new DomainValidationException("A member must have at least one role.");
        }

        Roles = distinct;
    }

    public void SetGrantedPermissions(IEnumerable<string> permissions) =>
        GrantedPermissions = permissions.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    public void UpdateJobTitle(string? jobTitle) => JobTitle = Guard.Optional(jobTitle, "Job title", 120);

    public void AssignWarehouses(IEnumerable<Guid> warehouseIds)
    {
        var wanted = warehouseIds.Distinct().ToHashSet();
        _warehouseAssignments.RemoveAll(a => !wanted.Contains(a.WarehouseId));
        foreach (var id in wanted.Where(id => _warehouseAssignments.All(a => a.WarehouseId != id)))
        {
            _warehouseAssignments.Add(new WarehouseAssignment(TenantId, Id, id));
        }
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new InvalidStateTransitionException("Membership", "Inactive", "Inactive");
        }

        IsActive = false;
    }

    public void Activate()
    {
        if (IsActive)
        {
            throw new InvalidStateTransitionException("Membership", "Active", "Active");
        }

        IsActive = true;
    }
}

/// <summary>Restricts a member's warehouse-scoped permissions to specific warehouses.</summary>
public sealed class WarehouseAssignment : AuditableEntity, ITenantOwned
{
    private WarehouseAssignment() { }

    internal WarehouseAssignment(Guid tenantId, Guid membershipId, Guid warehouseId)
    {
        TenantId = tenantId;
        MembershipId = membershipId;
        WarehouseId = Guard.NotEmpty(warehouseId, "Warehouse");
    }

    public Guid TenantId { get; private set; }
    public Guid MembershipId { get; private set; }
    public Guid WarehouseId { get; private set; }
}
