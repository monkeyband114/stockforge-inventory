using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Jobs;
using StockForge.Domain.Inventory;

namespace StockForge.Application.Inventory;

/// <summary>
/// The single entry point for changing stock balances. Every operation mutates an <see cref="InventoryItem"/>
/// (whose invariants prevent overselling) and appends the matching immutable <see cref="StockMovement"/>.
/// The ledger never saves: callers run it inside <see cref="ITransactionRunner"/> and call SaveChanges once,
/// so the balance update, the ledger rows, audit rows and outbox jobs commit atomically.
/// </summary>
public sealed class InventoryLedger(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IJobQueue jobs,
    TimeProvider clock)
{
    private bool? _lowStockAlertsEnabled;

    /// <summary>
    /// Loads (creating if missing) the balance rows for the given pairs, plus their products, as tracked
    /// entities. Rows are read in id order; EF also writes them in key order, which keeps lock ordering
    /// deterministic across concurrent transactions and avoids deadlocks.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Guid WarehouseId, Guid ProductId), InventoryItem>> LoadAsync(
        IReadOnlyCollection<(Guid WarehouseId, Guid ProductId)> keys, CancellationToken cancellationToken)
    {
        var distinct = keys.Distinct().ToList();
        await db.EnsureInventoryItemsAsync(distinct, cancellationToken);

        var warehouseIds = distinct.Select(k => k.WarehouseId).Distinct().ToList();
        var productIds = distinct.Select(k => k.ProductId).Distinct().ToList();

        await db.Products.Where(p => productIds.Contains(p.Id)).LoadAsync(cancellationToken);
        var items = await db.InventoryItems
            .Where(i => warehouseIds.Contains(i.WarehouseId) && productIds.Contains(i.ProductId))
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken);

        _lowStockAlertsEnabled ??= await db.Tenants
            .Where(t => t.Id == tenantContext.RequiredTenantId)
            .Select(t => t.Settings.LowStockAlertsEnabled)
            .SingleAsync(cancellationToken);

        var wanted = distinct.ToHashSet();
        return items.Where(i => wanted.Contains((i.WarehouseId, i.ProductId)))
            .ToDictionary(i => (i.WarehouseId, i.ProductId));
    }

    public async Task<InventoryItem> LoadAsync(Guid warehouseId, Guid productId, CancellationToken cancellationToken) =>
        (await LoadAsync([(warehouseId, productId)], cancellationToken))[(warehouseId, productId)];

    /// <summary>Stock In, Returned or Transfer In: increases on-hand stock at the given unit cost.</summary>
    public StockMovement Receive(InventoryItem item, decimal quantity, decimal unitCost, StockMovementType type,
        MovementReference reference, string? reason)
    {
        EnsureType(type, StockMovementType.StockIn, StockMovementType.Returned, StockMovementType.TransferIn);
        var before = item.QuantityAvailable;
        return Record(item, type, item.Receive(quantity, unitCost, Now), reference, reason, before);
    }

    /// <summary>Stock Out or Damaged: removes unreserved stock.</summary>
    public StockMovement Issue(InventoryItem item, decimal quantity, StockMovementType type,
        MovementReference reference, string? reason)
    {
        EnsureType(type, StockMovementType.StockOut, StockMovementType.Damaged);
        var before = item.QuantityAvailable;
        return Record(item, type, item.Issue(quantity, Now), reference, reason, before);
    }

    public StockMovement Reserve(InventoryItem item, decimal quantity, MovementReference reference, string? reason)
    {
        var before = item.QuantityAvailable;
        return Record(item, StockMovementType.Reservation, item.Reserve(quantity, Now), reference, reason, before);
    }

    public StockMovement Release(InventoryItem item, decimal quantity, MovementReference reference, string? reason)
    {
        var before = item.QuantityAvailable;
        return Record(item, StockMovementType.Release, item.Release(quantity, Now), reference, reason, before);
    }

    /// <summary>Ships previously reserved stock (Stock Out against an order, Transfer Out).</summary>
    public StockMovement ConsumeReserved(InventoryItem item, decimal quantity, StockMovementType type,
        MovementReference reference, string? reason)
    {
        EnsureType(type, StockMovementType.StockOut, StockMovementType.TransferOut);
        var before = item.QuantityAvailable;
        return Record(item, type, item.ConsumeReserved(quantity, Now), reference, reason, before);
    }

    public StockMovement Adjust(InventoryItem item, decimal delta, MovementReference reference, string? reason)
    {
        var before = item.QuantityAvailable;
        return Record(item, StockMovementType.Adjustment, item.Adjust(delta, Now), reference, reason, before);
    }

    private DateTimeOffset Now => clock.GetUtcNow();

    private StockMovement Record(InventoryItem item, StockMovementType type, StockChange change,
        MovementReference reference, string? reason, decimal availableBefore)
    {
        var movement = StockMovement.Record(item, type, change, reference, reason, currentUser.UserId,
            currentUser.CorrelationId, Now);
        db.StockMovements.Add(movement);
        DetectLowStock(item, availableBefore);
        return movement;
    }

    /// <summary>Enqueues an alert only when available stock crosses the threshold, not on every movement.</summary>
    private void DetectLowStock(InventoryItem item, decimal availableBefore)
    {
        if (_lowStockAlertsEnabled != true || item.Product is not { ReorderLevel: > 0 } product)
        {
            return;
        }

        if (availableBefore > product.ReorderLevel && item.QuantityAvailable <= product.ReorderLevel)
        {
            jobs.Enqueue(new LowStockAlertJob(item.WarehouseId, item.ProductId, item.QuantityAvailable, product.ReorderLevel));
        }
    }

    private static void EnsureType(StockMovementType actual, params StockMovementType[] allowed)
    {
        if (!allowed.Contains(actual))
        {
            throw new ArgumentOutOfRangeException(nameof(actual), actual, "Movement type is not valid for this operation.");
        }
    }
}
