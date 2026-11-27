using StockForge.Domain.Common;

namespace StockForge.Domain.Inventory;

public enum TransferStatus
{
    /// <summary>Created; stock is reserved at the source warehouse.</summary>
    Requested = 1,

    /// <summary>Dispatched; stock has left the source and is in transit.</summary>
    InTransit = 2,

    /// <summary>Received at the destination.</summary>
    Completed = 3,

    /// <summary>Cancelled before dispatch; the source reservation was released.</summary>
    Cancelled = 4,
}

/// <summary>Moves stock between two warehouses of the same organization: request → dispatch → receive.</summary>
public sealed class StockTransfer : TenantEntity, IAuditable
{
    private readonly List<StockTransferLine> _lines = [];

    private StockTransfer() { }

    public StockTransfer(
        string number,
        Guid sourceWarehouseId,
        Guid destinationWarehouseId,
        IReadOnlyCollection<(Guid ProductId, decimal Quantity)> lines,
        string? notes,
        Guid requestedBy,
        DateTimeOffset at)
    {
        Number = Guard.Required(number, "Number", 32);
        SourceWarehouseId = Guard.NotEmpty(sourceWarehouseId, "Source warehouse");
        DestinationWarehouseId = Guard.NotEmpty(destinationWarehouseId, "Destination warehouse");
        if (SourceWarehouseId == DestinationWarehouseId)
        {
            throw new DomainValidationException("Source and destination warehouses must differ.");
        }

        if (lines.Count == 0)
        {
            throw new DomainValidationException("A transfer needs at least one line.");
        }

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            throw new DomainValidationException("Each product may appear only once per transfer.");
        }

        foreach (var (productId, quantity) in lines)
        {
            _lines.Add(new StockTransferLine(Id, productId, quantity));
        }

        Notes = Guard.Optional(notes, "Notes", 1000);
        RequestedBy = requestedBy;
        RequestedAt = at;
        Status = TransferStatus.Requested;
        RaiseStatusChanged();
    }

    public string Number { get; private set; } = string.Empty;
    public Guid SourceWarehouseId { get; private set; }
    public Guid DestinationWarehouseId { get; private set; }
    public TransferStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DispatchedBy { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public Guid? ReceivedBy { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyCollection<StockTransferLine> Lines => _lines;

    public void MarkDispatched(Guid userId, DateTimeOffset at)
    {
        Transition(TransferStatus.Requested, TransferStatus.InTransit);
        DispatchedBy = userId;
        DispatchedAt = at;
    }

    public void MarkReceived(Guid userId, DateTimeOffset at)
    {
        Transition(TransferStatus.InTransit, TransferStatus.Completed);
        ReceivedBy = userId;
        ReceivedAt = at;
    }

    public void Cancel(Guid userId, DateTimeOffset at)
    {
        Transition(TransferStatus.Requested, TransferStatus.Cancelled);
        CancelledBy = userId;
        CancelledAt = at;
    }

    private void Transition(TransferStatus expected, TransferStatus target)
    {
        if (Status != expected)
        {
            throw new InvalidStateTransitionException("Stock transfer", Status.ToString(), target.ToString());
        }

        Status = target;
        RaiseStatusChanged();
    }

    private void RaiseStatusChanged() =>
        RaiseDomainEvent(new StockTransferStatusChangedEvent(TenantId, Id, Number, SourceWarehouseId, DestinationWarehouseId, Status));
}

public sealed record StockTransferStatusChangedEvent(
    Guid TenantId,
    Guid TransferId,
    string Number,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    TransferStatus Status) : IDomainEvent;

public sealed class StockTransferLine : TenantEntity
{
    private StockTransferLine() { }

    internal StockTransferLine(Guid transferId, Guid productId, decimal quantity)
    {
        TransferId = transferId;
        ProductId = Guard.NotEmpty(productId, "Product");
        Quantity = Guard.Positive(quantity, "Quantity");
    }

    public Guid TransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>Average unit cost at the source when dispatched; the destination receives at this cost.</summary>
    public decimal UnitCost { get; private set; }

    public void SetDispatchCost(decimal unitCost) => UnitCost = Guard.NotNegative(unitCost, "Unit cost");
}
