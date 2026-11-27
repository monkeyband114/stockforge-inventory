using StockForge.Domain.Common;

namespace StockForge.Domain.Inventory;

public enum StockMovementType
{
    StockIn = 1,
    StockOut = 2,
    TransferOut = 3,
    TransferIn = 4,
    Adjustment = 5,
    Reservation = 6,
    Release = 7,
    Damaged = 8,
    Returned = 9,
}

/// <summary>What business document caused a movement.</summary>
public enum MovementReferenceType
{
    Manual = 1,
    StockTransfer = 2,
    StockAdjustment = 3,
    StockReservation = 4,
    SalesOrder = 5,
    SalesReturn = 6,
    PurchaseOrder = 7,
    GoodsReceipt = 8,
}

public readonly record struct MovementReference(MovementReferenceType Type, Guid? Id, string? Number)
{
    public static MovementReference Manual(string? number = null) => new(MovementReferenceType.Manual, null, number);
}

/// <summary>
/// Immutable ledger row describing one inventory change. Never updated or deleted — the database revokes
/// UPDATE/DELETE from the application role and a trigger rejects them for everyone.
/// </summary>
public sealed class StockMovement : Entity, ITenantOwned
{
    private StockMovement() { }

    private StockMovement(
        InventoryItem item,
        StockMovementType type,
        StockChange change,
        MovementReference reference,
        string? reason,
        Guid? userId,
        string? correlationId,
        DateTimeOffset at)
    {
        TenantId = item.TenantId;
        InventoryItemId = item.Id;
        WarehouseId = item.WarehouseId;
        ProductId = item.ProductId;
        Type = type;
        QuantityDelta = change.OnHandDelta;
        ReservedDelta = change.ReservedDelta;
        OnHandAfter = change.OnHandAfter;
        ReservedAfter = change.ReservedAfter;
        UnitCost = change.UnitCost;
        ReferenceType = reference.Type;
        ReferenceId = reference.Id;
        ReferenceNumber = reference.Number;
        Reason = reason is null ? null : Guard.Optional(reason, "Reason", 500);
        CreatedBy = userId;
        CorrelationId = correlationId;
        CreatedAt = at;
        RaiseDomainEvent(new StockMovementRecordedEvent(TenantId, WarehouseId, ProductId, Id, type, QuantityDelta, ReservedDelta));
    }

    public Guid TenantId { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public StockMovementType Type { get; private set; }

    /// <summary>Signed change of on-hand quantity.</summary>
    public decimal QuantityDelta { get; private set; }

    /// <summary>Signed change of reserved quantity.</summary>
    public decimal ReservedDelta { get; private set; }

    public decimal OnHandAfter { get; private set; }
    public decimal ReservedAfter { get; private set; }
    public decimal UnitCost { get; private set; }
    public MovementReferenceType ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Reason { get; private set; }
    public string? CorrelationId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static StockMovement Record(
        InventoryItem item,
        StockMovementType type,
        StockChange change,
        MovementReference reference,
        string? reason,
        Guid? userId,
        string? correlationId,
        DateTimeOffset at) =>
        new(item, type, change, reference, reason, userId, correlationId, at);
}

public sealed record StockMovementRecordedEvent(
    Guid TenantId,
    Guid WarehouseId,
    Guid ProductId,
    Guid MovementId,
    StockMovementType Type,
    decimal QuantityDelta,
    decimal ReservedDelta) : IDomainEvent;
