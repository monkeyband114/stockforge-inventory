using StockForge.Domain.Common;

namespace StockForge.Domain.Inventory;

public enum AdjustmentReason
{
    CycleCount = 1,
    Correction = 2,
    Loss = 3,
    Found = 4,
    Expired = 5,
    Other = 6,
}

public enum AdjustmentStatus
{
    PendingApproval = 1,
    Applied = 2,
    Rejected = 3,
}

/// <summary>
/// A requested correction of on-hand stock. Warehouse staff record counts or corrections; an inventory
/// manager approves (applies) or rejects them, unless the organization disables the approval step.
/// The variance is fixed when the request is made, so movements between count and approval are preserved.
/// </summary>
public sealed class StockAdjustment : TenantEntity, IAuditable
{
    private StockAdjustment() { }

    private StockAdjustment(
        string number,
        Guid warehouseId,
        Guid productId,
        AdjustmentReason reason,
        decimal quantityChange,
        decimal systemQuantity,
        decimal? countedQuantity,
        string? notes,
        Guid requestedBy,
        DateTimeOffset at)
    {
        Number = Guard.Required(number, "Number", 32);
        WarehouseId = Guard.NotEmpty(warehouseId, "Warehouse");
        ProductId = Guard.NotEmpty(productId, "Product");
        Reason = Enum.IsDefined(reason) ? reason : throw new DomainValidationException("Unknown adjustment reason.");
        if (quantityChange == 0)
        {
            throw new DomainValidationException("The adjustment does not change stock.");
        }

        QuantityChange = quantityChange;
        SystemQuantity = systemQuantity;
        CountedQuantity = countedQuantity;
        Notes = Guard.Optional(notes, "Notes", 1000);
        RequestedBy = requestedBy;
        RequestedAt = at;
        Status = AdjustmentStatus.PendingApproval;
        RaiseStatusChanged();
    }

    public string Number { get; private set; } = string.Empty;
    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public AdjustmentReason Reason { get; private set; }

    /// <summary>Signed change applied to on-hand stock.</summary>
    public decimal QuantityChange { get; private set; }

    /// <summary>On-hand quantity in the system when the adjustment was requested.</summary>
    public decimal SystemQuantity { get; private set; }

    /// <summary>Physically counted quantity, for cycle counts.</summary>
    public decimal? CountedQuantity { get; private set; }

    public string? Notes { get; private set; }
    public AdjustmentStatus Status { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNotes { get; private set; }
    public uint Version { get; private set; }

    public static StockAdjustment ForCount(
        string number, Guid warehouseId, Guid productId, decimal countedQuantity, decimal systemQuantity,
        string? notes, Guid requestedBy, DateTimeOffset at)
    {
        Guard.NotNegative(countedQuantity, "Counted quantity");
        return new StockAdjustment(number, warehouseId, productId, AdjustmentReason.CycleCount,
            countedQuantity - systemQuantity, systemQuantity, countedQuantity, notes, requestedBy, at);
    }

    public static StockAdjustment ForCorrection(
        string number, Guid warehouseId, Guid productId, AdjustmentReason reason, decimal quantityChange,
        decimal systemQuantity, string? notes, Guid requestedBy, DateTimeOffset at)
    {
        if (reason == AdjustmentReason.CycleCount)
        {
            throw new DomainValidationException("Use a count to record cycle-count adjustments.");
        }

        return new StockAdjustment(number, warehouseId, productId, reason, quantityChange, systemQuantity, null,
            notes, requestedBy, at);
    }

    public void MarkApplied(Guid reviewerId, string? reviewNotes, DateTimeOffset at)
    {
        EnsurePending(AdjustmentStatus.Applied);
        Status = AdjustmentStatus.Applied;
        ReviewedBy = reviewerId;
        ReviewedAt = at;
        ReviewNotes = Guard.Optional(reviewNotes, "Review notes", 1000);
        RaiseStatusChanged();
    }

    public void Reject(Guid reviewerId, string reviewNotes, DateTimeOffset at)
    {
        EnsurePending(AdjustmentStatus.Rejected);
        Status = AdjustmentStatus.Rejected;
        ReviewedBy = reviewerId;
        ReviewedAt = at;
        ReviewNotes = Guard.Required(reviewNotes, "Rejection reason", 1000);
        RaiseStatusChanged();
    }

    private void EnsurePending(AdjustmentStatus target)
    {
        if (Status != AdjustmentStatus.PendingApproval)
        {
            throw new InvalidStateTransitionException("Stock adjustment", Status.ToString(), target.ToString());
        }
    }

    private void RaiseStatusChanged() =>
        RaiseDomainEvent(new StockAdjustmentStatusChangedEvent(TenantId, Id, Number, WarehouseId, ProductId, Status, QuantityChange));
}

public sealed record StockAdjustmentStatusChangedEvent(
    Guid TenantId,
    Guid AdjustmentId,
    string Number,
    Guid WarehouseId,
    Guid ProductId,
    AdjustmentStatus Status,
    decimal QuantityChange) : IDomainEvent;
