using StockForge.Domain.Catalog;
using StockForge.Domain.Common;

namespace StockForge.Domain.Inventory;

/// <summary>
/// The stock balance of one product in one warehouse. All mutations go through the methods below, which
/// enforce the invariants <c>OnHand ≥ 0</c>, <c>Reserved ≥ 0</c> and <c>Reserved ≤ OnHand</c> (also enforced by
/// database CHECK constraints). The row's <c>xmin</c> is the optimistic concurrency token.
/// </summary>
public sealed class InventoryItem : TenantEntity
{
    private InventoryItem() { }

    public InventoryItem(Guid warehouseId, Guid productId)
    {
        WarehouseId = Guard.NotEmpty(warehouseId, "Warehouse");
        ProductId = Guard.NotEmpty(productId, "Product");
    }

    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal QuantityOnHand { get; private set; }
    public decimal QuantityReserved { get; private set; }

    /// <summary>Perpetual weighted-average unit cost, used for inventory valuation.</summary>
    public decimal AverageUnitCost { get; private set; }

    public DateTimeOffset? LastMovementAt { get; private set; }
    public uint Version { get; private set; }

    public Warehouse? Warehouse { get; private set; }
    public Product? Product { get; private set; }

    public decimal QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>Adds stock and recalculates the weighted-average cost.</summary>
    public StockChange Receive(decimal quantity, decimal unitCost, DateTimeOffset at)
    {
        Guard.Positive(quantity, "Quantity");
        Guard.NotNegative(unitCost, "Unit cost");
        var newOnHand = QuantityOnHand + quantity;
        AverageUnitCost = newOnHand == 0
            ? unitCost
            : decimal.Round(((QuantityOnHand * AverageUnitCost) + (quantity * unitCost)) / newOnHand, 4);
        return Apply(quantity, 0, unitCost, at);
    }

    /// <summary>Removes unreserved stock (sale without reservation, write-off, damage).</summary>
    public StockChange Issue(decimal quantity, DateTimeOffset at)
    {
        Guard.Positive(quantity, "Quantity");
        EnsureAvailable(quantity);
        return Apply(-quantity, 0, AverageUnitCost, at);
    }

    /// <summary>Earmarks available stock for an order or transfer.</summary>
    public StockChange Reserve(decimal quantity, DateTimeOffset at)
    {
        Guard.Positive(quantity, "Quantity");
        EnsureAvailable(quantity);
        return Apply(0, quantity, AverageUnitCost, at);
    }

    /// <summary>Returns reserved stock to the available pool.</summary>
    public StockChange Release(decimal quantity, DateTimeOffset at)
    {
        Guard.Positive(quantity, "Quantity");
        if (quantity > QuantityReserved)
        {
            throw new DomainException("invalid_release", "Cannot release more stock than is reserved.");
        }

        return Apply(0, -quantity, AverageUnitCost, at);
    }

    /// <summary>Ships previously reserved stock: reduces both on-hand and reserved.</summary>
    public StockChange ConsumeReserved(decimal quantity, DateTimeOffset at)
    {
        Guard.Positive(quantity, "Quantity");
        if (quantity > QuantityReserved)
        {
            throw new DomainException("invalid_consume", "Cannot consume more stock than is reserved.");
        }

        return Apply(-quantity, -quantity, AverageUnitCost, at);
    }

    /// <summary>Signed correction of on-hand stock (cycle count, found/lost stock).</summary>
    public StockChange Adjust(decimal delta, DateTimeOffset at)
    {
        if (delta == 0)
        {
            throw new DomainValidationException("Adjustment quantity cannot be zero.");
        }

        if (delta < 0 && QuantityOnHand + delta < QuantityReserved)
        {
            throw new InsufficientStockException(ProductId, WarehouseId, -delta, QuantityAvailable);
        }

        return Apply(delta, 0, AverageUnitCost, at);
    }

    private void EnsureAvailable(decimal quantity)
    {
        if (quantity > QuantityAvailable)
        {
            throw new InsufficientStockException(ProductId, WarehouseId, quantity, QuantityAvailable);
        }
    }

    private StockChange Apply(decimal onHandDelta, decimal reservedDelta, decimal unitCost, DateTimeOffset at)
    {
        QuantityOnHand += onHandDelta;
        QuantityReserved += reservedDelta;
        LastMovementAt = at;
        RaiseDomainEvent(new InventoryLevelChangedEvent(TenantId, WarehouseId, ProductId, QuantityOnHand, QuantityReserved));
        return new StockChange(onHandDelta, reservedDelta, QuantityOnHand, QuantityReserved, unitCost);
    }
}

/// <summary>The effect of one balance mutation, used to write the matching ledger row.</summary>
public readonly record struct StockChange(
    decimal OnHandDelta,
    decimal ReservedDelta,
    decimal OnHandAfter,
    decimal ReservedAfter,
    decimal UnitCost);

/// <summary>Raised whenever an inventory balance changes. Published after commit (SignalR, low-stock checks).</summary>
public sealed record InventoryLevelChangedEvent(
    Guid TenantId,
    Guid WarehouseId,
    Guid ProductId,
    decimal QuantityOnHand,
    decimal QuantityReserved) : IDomainEvent
{
    public decimal QuantityAvailable => QuantityOnHand - QuantityReserved;
}
