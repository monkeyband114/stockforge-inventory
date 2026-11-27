using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Security;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;

namespace StockForge.Application.Inventory;

/// <summary>
/// Stock counts and corrections with an approval step. Requests from members who can approve (or when the
/// organization disables approval) are applied immediately; otherwise they wait for an inventory manager.
/// A pending request can never be approved by the member who raised it (segregation of duties).
/// </summary>
public sealed class StockAdjustmentService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITransactionRunner transactions,
    InventoryLedger ledger,
    ISequenceGenerator sequences,
    IUserDirectory users,
    TimeProvider clock)
{
    public async Task<PagedResult<StockAdjustmentDto>> ListAsync(AdjustmentQuery query, CancellationToken cancellationToken)
    {
        var adjustments = db.StockAdjustments.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var allowedIds = allowed.ToArray();
            adjustments = adjustments.Where(a => allowedIds.Contains(a.WarehouseId));
        }

        if (query.WarehouseId is { } warehouseId)
        {
            adjustments = adjustments.Where(a => a.WarehouseId == warehouseId);
        }

        if (query.Status is { } status)
        {
            adjustments = adjustments.Where(a => a.Status == status);
        }

        var page = await Project(adjustments).ToPagedResultAsync(query, cancellationToken);
        return page with { Items = await WithNamesAsync(page.Items, cancellationToken) };
    }

    public async Task<StockAdjustmentDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = await Project(db.StockAdjustments.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(cancellationToken);
        if (dto is null || !currentUser.CanAccessWarehouse(dto.WarehouseId))
        {
            throw new NotFoundException("Stock adjustment", id);
        }

        return (await WithNamesAsync([dto], cancellationToken))[0];
    }

    public Task<StockAdjustmentDto> RequestCountAsync(StockCountRequest request, CancellationToken cancellationToken) =>
        RequestAsync(request.WarehouseId, request.ProductId, (number, item, userId, now) =>
        {
            if (request.CountedQuantity == item.QuantityOnHand)
            {
                throw new DomainException("no_variance", "The counted quantity matches the system quantity; no adjustment is needed.");
            }

            return StockAdjustment.ForCount(number, item.WarehouseId, item.ProductId, request.CountedQuantity,
                item.QuantityOnHand, request.Notes, userId, now);
        }, cancellationToken);

    public Task<StockAdjustmentDto> RequestCorrectionAsync(StockCorrectionRequest request, CancellationToken cancellationToken) =>
        RequestAsync(request.WarehouseId, request.ProductId, (number, item, userId, now) =>
            StockAdjustment.ForCorrection(number, item.WarehouseId, item.ProductId, request.Reason,
                request.QuantityChange, item.QuantityOnHand, request.Notes, userId, now), cancellationToken);

    public async Task<StockAdjustmentDto> ApproveAsync(Guid id, ReviewAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        await transactions.ExecuteAsync(async token =>
        {
            var adjustment = await LoadForReviewAsync(id, token);
            if (adjustment.RequestedBy == userId)
            {
                throw new ForbiddenException("You cannot approve an adjustment you requested.");
            }

            var item = await ledger.LoadAsync(adjustment.WarehouseId, adjustment.ProductId, token);
            Apply(adjustment, item, userId, request.Notes);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<StockAdjustmentDto> RejectAsync(Guid id, ReviewAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        await transactions.ExecuteAsync(async token =>
        {
            var adjustment = await LoadForReviewAsync(id, token);
            adjustment.Reject(userId, request.Notes ?? string.Empty, clock.GetUtcNow());
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<StockAdjustmentDto> RequestAsync(
        Guid warehouseId,
        Guid productId,
        Func<string, InventoryItem, Guid, DateTimeOffset, StockAdjustment> create,
        CancellationToken cancellationToken)
    {
        currentUser.EnsureWarehouseAccess(warehouseId);
        var userId = currentUser.RequiredUserId;
        var id = await transactions.ExecuteAsync(async token =>
        {
            var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, token)
                            ?? throw new NotFoundException("Warehouse", warehouseId);
            warehouse.EnsureActive();
            if (!await db.Products.AnyAsync(p => p.Id == productId, token))
            {
                throw new NotFoundException("Product", productId);
            }

            var item = await ledger.LoadAsync(warehouseId, productId, token);
            var number = $"ADJ-{await sequences.NextValueAsync(SequenceNames.StockAdjustment, token):D6}";
            var adjustment = create(number, item, userId, clock.GetUtcNow());
            db.StockAdjustments.Add(adjustment);

            var requiresApproval = await db.Tenants
                .Where(t => t.Id == tenantContext.RequiredTenantId)
                .Select(t => t.Settings.StockAdjustmentRequiresApproval)
                .SingleAsync(token);
            if (!requiresApproval || currentUser.HasPermission(Permissions.Inventory.ApproveAdjustment))
            {
                Apply(adjustment, item, userId, "Auto-approved");
            }

            await db.SaveChangesAsync(token);
            return adjustment.Id;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private void Apply(StockAdjustment adjustment, InventoryItem item, Guid reviewerId, string? notes)
    {
        adjustment.MarkApplied(reviewerId, notes, clock.GetUtcNow());
        ledger.Adjust(item, adjustment.QuantityChange,
            new MovementReference(MovementReferenceType.StockAdjustment, adjustment.Id, adjustment.Number),
            $"{adjustment.Reason}: {adjustment.Notes}".TrimEnd(' ', ':'));
    }

    private async Task<StockAdjustment> LoadForReviewAsync(Guid id, CancellationToken token)
    {
        var adjustment = await db.StockAdjustments.FirstOrDefaultAsync(a => a.Id == id, token)
                         ?? throw new NotFoundException("Stock adjustment", id);
        currentUser.EnsureWarehouseAccess(adjustment.WarehouseId);
        return adjustment;
    }

    private IQueryable<StockAdjustmentDto> Project(IQueryable<StockAdjustment> adjustments) =>
        from a in adjustments
        join p in db.Products on a.ProductId equals p.Id
        join w in db.Warehouses on a.WarehouseId equals w.Id
        orderby a.RequestedAt descending, a.Id descending
        select new StockAdjustmentDto(
            a.Id, a.Number, a.WarehouseId, w.Code, a.ProductId, p.Sku, p.Name, a.Reason, a.QuantityChange,
            a.SystemQuantity, a.CountedQuantity, a.Notes, a.Status, a.RequestedBy, null, a.RequestedAt,
            a.ReviewedBy, null, a.ReviewedAt, a.ReviewNotes);

    private async Task<IReadOnlyList<StockAdjustmentDto>> WithNamesAsync(
        IReadOnlyList<StockAdjustmentDto> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(a => a.RequestedBy)
            .Concat(items.Where(a => a.ReviewedBy.HasValue).Select(a => a.ReviewedBy!.Value))
            .Distinct();
        var names = await users.GetDisplayNamesAsync(ids, cancellationToken);
        return items.Select(a => a with
        {
            RequestedByName = names.NameOf(a.RequestedBy),
            ReviewedByName = names.NameOf(a.ReviewedBy),
        }).ToList();
    }
}
