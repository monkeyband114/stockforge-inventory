using StockForge.Domain.Common;

namespace StockForge.Domain.Inventory;

public enum ReservationStatus
{
    Active = 1,
    Released = 2,
    Consumed = 3,
}

/// <summary>
/// Stock earmarked for a document (sales order, transfer or manual hold). The quantity is also reflected
/// in <see cref="InventoryItem.QuantityReserved"/>; both are changed together by the inventory ledger.
/// </summary>
public sealed class StockReservation : TenantEntity
{
    private StockReservation() { }

    public StockReservation(
        Guid warehouseId,
        Guid productId,
        decimal quantity,
        MovementReference reference,
        DateTimeOffset? expiresAt,
        string? notes)
    {
        WarehouseId = Guard.NotEmpty(warehouseId, "Warehouse");
        ProductId = Guard.NotEmpty(productId, "Product");
        Quantity = Guard.Positive(quantity, "Quantity");
        ReferenceType = reference.Type;
        ReferenceId = reference.Id;
        ReferenceNumber = reference.Number;
        ExpiresAt = expiresAt;
        Notes = Guard.Optional(notes, "Notes", 500);
        Status = ReservationStatus.Active;
    }

    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal QuantityConsumed { get; private set; }
    public decimal QuantityReleased { get; private set; }
    public MovementReferenceType ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? Notes { get; private set; }
    public uint Version { get; private set; }

    public decimal RemainingQuantity => Quantity - QuantityConsumed - QuantityReleased;

    /// <summary>Marks part (or all) of the reservation as shipped/used.</summary>
    public void Consume(decimal quantity, DateTimeOffset at)
    {
        EnsureActive("Consumed");
        Guard.Positive(quantity, "Quantity");
        if (quantity > RemainingQuantity)
        {
            throw new DomainException("invalid_consume", "Cannot consume more than the remaining reserved quantity.");
        }

        QuantityConsumed += quantity;
        CloseIfDone(at);
    }

    /// <summary>Releases the remaining quantity; returns how much was released.</summary>
    public decimal ReleaseRemaining(DateTimeOffset at)
    {
        EnsureActive("Released");
        var remaining = RemainingQuantity;
        QuantityReleased += remaining;
        Status = QuantityConsumed > 0 ? ReservationStatus.Consumed : ReservationStatus.Released;
        ClosedAt = at;
        return remaining;
    }

    private void CloseIfDone(DateTimeOffset at)
    {
        if (RemainingQuantity == 0)
        {
            Status = ReservationStatus.Consumed;
            ClosedAt = at;
        }
    }

    private void EnsureActive(string target)
    {
        if (Status != ReservationStatus.Active)
        {
            throw new InvalidStateTransitionException("Reservation", Status.ToString(), target);
        }
    }
}
