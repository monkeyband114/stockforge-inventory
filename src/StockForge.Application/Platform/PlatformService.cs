using System.ComponentModel.DataAnnotations.Schema;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Domain.Platform;
using StockForge.Domain.Tenancy;

namespace StockForge.Application.Platform;

public sealed record TenantSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    TenantStatus Status,
    string ContactEmail,
    string BaseCurrency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason,
    int MemberCount,
    int WarehouseCount,
    int ProductCount);

public sealed record PlatformStatsDto(int Tenants, int ActiveTenants, int Users, int FailedJobs, int PendingJobs);

public sealed record TenantListQuery : PageQuery
{
    public TenantStatus? Status { get; init; }
}

public sealed record SuspendTenantRequest(string Reason);

public sealed class SuspendTenantRequestValidator : AbstractValidator<SuspendTenantRequest>
{
    public SuspendTenantRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

/// <summary>Raw row returned by the SECURITY DEFINER function <c>platform_tenant_stats()</c>.</summary>
public sealed class TenantStatsRow
{
    [Column("tenant_id")]
    public Guid TenantId { get; set; }

    [Column("warehouses")]
    public int Warehouses { get; set; }

    [Column("products")]
    public int Products { get; set; }
}

/// <summary>
/// Super-administrator operations across organizations. Tenant business tables are protected by RLS, so
/// per-tenant counts come from a SECURITY DEFINER function that only returns aggregates.
/// </summary>
public sealed class PlatformService(IAppDbContext db, IUserDirectory users, IAuditTrail audit, ISessionCache sessions, TimeProvider clock)
{
    public async Task<PagedResult<TenantSummaryDto>> ListTenantsAsync(TenantListQuery query, CancellationToken cancellationToken)
    {
        var tenants = db.Tenants.AsNoTracking();
        if (query.Status is { } status)
        {
            tenants = tenants.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            tenants = tenants.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term) || t.ContactEmail.ToLower().Contains(term));
        }

        var page = await tenants.OrderBy(t => t.Name)
            .Select(t => new
            {
                Tenant = t,
                Members = db.TenantMemberships.IgnoreQueryFilters().Count(m => m.TenantId == t.Id && m.IsActive),
            })
            .ToPagedResultAsync(query, cancellationToken);

        var stats = (await db.SqlQuery<TenantStatsRow>($"SELECT tenant_id, warehouses, products FROM platform_tenant_stats()")
                .ToListAsync(cancellationToken))
            .ToDictionary(s => s.TenantId);

        var items = page.Items.Select(x =>
        {
            var s = stats.GetValueOrDefault(x.Tenant.Id);
            return new TenantSummaryDto(x.Tenant.Id, x.Tenant.Name, x.Tenant.Slug, x.Tenant.Status, x.Tenant.ContactEmail,
                x.Tenant.BaseCurrency, x.Tenant.CreatedAt, x.Tenant.SuspendedAt, x.Tenant.SuspensionReason, x.Members,
                s?.Warehouses ?? 0, s?.Products ?? 0);
        }).ToList();
        return new PagedResult<TenantSummaryDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<PlatformStatsDto> GetStatsAsync(CancellationToken cancellationToken) => new(
        await db.Tenants.CountAsync(cancellationToken),
        await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active, cancellationToken),
        await users.CountUsersAsync(cancellationToken),
        await db.OutboxMessages.CountAsync(j => j.Status == OutboxStatus.Failed, cancellationToken),
        await db.OutboxMessages.CountAsync(j => j.Status == OutboxStatus.Pending, cancellationToken));

    public async Task SuspendAsync(Guid tenantId, SuspendTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
                     ?? throw new NotFoundException("Organization", tenantId);
        tenant.Suspend(request.Reason, clock.GetUtcNow());
        audit.Record("TenantSuspended", nameof(Tenant), tenantId.ToString(), new { request.Reason });
        await db.SaveChangesAsync(cancellationToken);
        await sessions.InvalidateTenantSessionsAsync(tenantId, cancellationToken);
    }

    public async Task ActivateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
                     ?? throw new NotFoundException("Organization", tenantId);
        tenant.Activate();
        audit.Record("TenantActivated", nameof(Tenant), tenantId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}
