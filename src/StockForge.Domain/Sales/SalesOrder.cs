using StockForge.Domain.Common;
using StockForge.Domain.Tenancy;

namespace StockForge.Domain.Sales;

public enum SalesOrderStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Processing = 4,
    Picking = 5,
    Packed = 6,
    Shipped = 7,
    Delivered = 8,
    Cancelled = 9,
    Rejected = 10,
    PartiallyFulfilled = 11,
    Returned = 12,
}

/// <summary>Priced input for one order line (prices and tax resolved by the application layer).</summary>
public readonly record struct SalesOrderLineInput(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal TaxRate);

/// <summary>
/// Customer order and its fulfillment state machine:
/// Draft → PendingApproval → Approved → Processing → Picking → Packed → Shipped/PartiallyFulfilled → Delivered,
/// with Rejected, Cancelled (before any shipment) and Returned. Stock is reserved on approval and consumed on
/// shipment; the application layer does both in the same transaction as the state change.
/// </summary>
public sealed class SalesOrder : TenantEntity, IAuditable
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder() { }

    public SalesOrder(
        string number,
        Guid customerId,
        Guid warehouseId,
        DateOnly orderDate,
        DateOnly? requiredDate,
        string? customerReference,
        string? notes,
        string currency,
        decimal orderDiscountPercent,
        Address? shippingAddress,
        Guid createdBy,
        string? idempotencyKey)
    {
        Number = Guard.Required(number, "Number", 32);
        OrderDate = orderDate;
        Currency = Guard.Required(currency, "Currency", 3).ToUpperInvariant();
        CreatedByUser = createdBy;
        IdempotencyKey = Guard.Optional(idempotencyKey, "Idempotency key", 100);
        Status = SalesOrderStatus.Draft;
        SetDetails(customerId, warehouseId, requiredDate, customerReference, notes, orderDiscountPercent, shippingAddress);
        RaiseStatusChanged();
    }

    public string Number { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public DateOnly? RequiredDate { get; private set; }
    public string? CustomerReference { get; private set; }
    public string? Notes { get; private set; }
    public string Currency { get; private set; } = "USD";
    public decimal OrderDiscountPercent { get; private set; }
    public Address? ShippingAddress { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public decimal TotalQuantity { get; private set; }
    public bool RequiresApproval { get; private set; }
    public string? ApprovalReason { get; private set; }
    public Guid CreatedByUser { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? RejectedBy { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Client idempotency key of the creating request (unique per tenant): a retry can never create a twin order.</summary>
    public string? IdempotencyKey { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyCollection<SalesOrderLine> Lines => _lines;

    public bool HasShipments => _lines.Any(l => l.QuantityShipped > 0);

    public bool IsFullyShipped => _lines.Count > 0 && _lines.All(l => l.QuantityShipped == l.Quantity);

    // ---------- Draft editing ----------

    public void SetDetails(Guid customerId, Guid warehouseId, DateOnly? requiredDate, string? customerReference, string? notes,
        decimal orderDiscountPercent, Address? shippingAddress)
    {
        EnsureStatus("Edited", SalesOrderStatus.Draft);
        CustomerId = Guard.NotEmpty(customerId, "Customer");
        WarehouseId = Guard.NotEmpty(warehouseId, "Fulfillment warehouse");
        if (requiredDate is { } required && required < OrderDate)
        {
            throw new DomainValidationException("The required date cannot be before the order date.");
        }

        RequiredDate = requiredDate;
        CustomerReference = Guard.Optional(customerReference, "Customer reference", 64);
        Notes = Guard.Optional(notes, "Notes", 2000);
        OrderDiscountPercent = orderDiscountPercent is >= 0 and <= 100
            ? orderDiscountPercent
            : throw new DomainValidationException("Order discount must be between 0 and 100%.");
        ShippingAddress = shippingAddress;
        Recalculate();
    }

    public void SetLines(IReadOnlyCollection<SalesOrderLineInput> lines)
    {
        EnsureStatus("Edited", SalesOrderStatus.Draft);
        if (lines.Count == 0)
        {
            throw new DomainValidationException("An order needs at least one line.");
        }

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            throw new DomainValidationException("Each product may appear only once per order.");
        }

        _lines.Clear();
        var number = 1;
        foreach (var input in lines)
        {
            _lines.Add(new SalesOrderLine(Id, number++, input));
        }

        Recalculate();
    }

    // ---------- Approval ----------

    /// <summary>Submits a draft; returns why approval is required, or null when it can be approved automatically.</summary>
    public string? Submit(TenantSettings settings, DateTimeOffset at)
    {
        EnsureStatus("Submitted", SalesOrderStatus.Draft);
        if (_lines.Count == 0)
        {
            throw new DomainValidationException("An order needs at least one line.");
        }

        ApprovalReason = ApprovalReasonFor(settings);
        RequiresApproval = ApprovalReason is not null;
        SubmittedAt = at;
        Transition(SalesOrderStatus.PendingApproval);
        return ApprovalReason;
    }

    public string? ApprovalReasonFor(TenantSettings settings)
    {
        if (settings.RequireApprovalForAllOrders)
        {
            return "The organization requires approval for every order.";
        }

        if (settings.OrderApprovalAmountThreshold > 0 && Total > settings.OrderApprovalAmountThreshold)
        {
            return $"Order total {Total:0.00} exceeds the approval threshold of {settings.OrderApprovalAmountThreshold:0.00}.";
        }

        if (settings.OrderApprovalQuantityThreshold > 0 && TotalQuantity > settings.OrderApprovalQuantityThreshold)
        {
            return $"Bulk order: {TotalQuantity:0.###} units exceeds the threshold of {settings.OrderApprovalQuantityThreshold:0.###}.";
        }

        return null;
    }

    /// <summary>Forces manual approval for a reason detected outside the order (e.g. customer credit limit).</summary>
    public void FlagForApproval(string reason)
    {
        EnsureStatus("Flagged", SalesOrderStatus.PendingApproval);
        RequiresApproval = true;
        ApprovalReason = Guard.Required(reason, "Approval reason", 500);
    }

    /// <param name="approvedBy">The approving manager, or the submitter for automatic approval.</param>
    /// <param name="automatic">True when no approval was required.</param>
    public void Approve(Guid approvedBy, bool automatic, DateTimeOffset at)
    {
        EnsureStatus("Approved", SalesOrderStatus.PendingApproval);
        if (!automatic && approvedBy == CreatedByUser)
        {
            throw new DomainException("self_approval", "You cannot approve an order you created.");
        }

        if (automatic && RequiresApproval)
        {
            throw new DomainException("approval_required", ApprovalReason ?? "This order requires approval.");
        }

        ApprovedBy = approvedBy;
        ApprovedAt = at;
        Transition(SalesOrderStatus.Approved);
    }

    public void Reject(Guid rejectedBy, string reason, DateTimeOffset at)
    {
        EnsureStatus("Rejected", SalesOrderStatus.PendingApproval);
        RejectedBy = rejectedBy;
        RejectedAt = at;
        RejectionReason = Guard.Required(reason, "Rejection reason", 1000);
        Transition(SalesOrderStatus.Rejected);
    }

    public void Cancel(Guid cancelledBy, string reason, DateTimeOffset at)
    {
        EnsureStatus("Cancelled", SalesOrderStatus.Draft, SalesOrderStatus.PendingApproval, SalesOrderStatus.Approved,
            SalesOrderStatus.Processing, SalesOrderStatus.Picking, SalesOrderStatus.Packed);
        if (HasShipments)
        {
            throw new DomainException("order_partially_shipped", "Part of this order has shipped; it can no longer be cancelled.");
        }

        CancelledBy = cancelledBy;
        CancelledAt = at;
        CancellationReason = Guard.Required(reason, "Cancellation reason", 1000);
        Transition(SalesOrderStatus.Cancelled);
    }

    /// <summary>True if stock was reserved for this order (approved and not yet closed).</summary>
    public bool HoldsReservation => Status is SalesOrderStatus.Approved or SalesOrderStatus.Processing or SalesOrderStatus.Picking
        or SalesOrderStatus.Packed or SalesOrderStatus.PartiallyFulfilled;

    // ---------- Fulfillment ----------

    public void StartProcessing()
    {
        EnsureStatus("Processing", SalesOrderStatus.Approved);
        Transition(SalesOrderStatus.Processing);
    }

    public void StartPicking()
    {
        EnsureStatus("Picking", SalesOrderStatus.Processing, SalesOrderStatus.PartiallyFulfilled);
        Transition(SalesOrderStatus.Picking);
    }

    public void RecordPick(Guid lineId, decimal quantity)
    {
        EnsureStatus("Picked", SalesOrderStatus.Picking);
        Line(lineId).Pick(quantity);
    }

    /// <summary>Packs everything picked and not yet packed.</summary>
    public void Pack()
    {
        EnsureStatus("Packed", SalesOrderStatus.Picking);
        if (!_lines.Any(l => l.QuantityPicked > l.QuantityPacked))
        {
            throw new DomainException("nothing_to_pack", "Pick at least one item before packing.");
        }

        foreach (var line in _lines)
        {
            line.PackPicked();
        }

        Transition(SalesOrderStatus.Packed);
    }

    /// <summary>Ships everything packed and not yet shipped; returns the shipped quantity per line.</summary>
    public IReadOnlyList<(SalesOrderLine Line, decimal Quantity)> Ship(DateTimeOffset at)
    {
        EnsureStatus("Shipped", SalesOrderStatus.Packed);
        var shipped = _lines
            .Select(l => (Line: l, Quantity: l.QuantityPacked - l.QuantityShipped))
            .Where(x => x.Quantity > 0)
            .ToList();
        foreach (var (line, quantity) in shipped)
        {
            line.Ship(quantity);
        }

        ShippedAt = at;
        Transition(IsFullyShipped ? SalesOrderStatus.Shipped : SalesOrderStatus.PartiallyFulfilled);
        return shipped;
    }

    public void MarkDelivered(DateTimeOffset at)
    {
        EnsureStatus("Delivered", SalesOrderStatus.Shipped);
        DeliveredAt = at;
        Transition(SalesOrderStatus.Delivered);
    }

    public void RecordReturn(Guid lineId, decimal quantity)
    {
        EnsureStatus("Returned", SalesOrderStatus.Shipped, SalesOrderStatus.Delivered, SalesOrderStatus.PartiallyFulfilled,
            SalesOrderStatus.Returned);
        Line(lineId).Return(quantity);
        if (IsFullyShipped && _lines.All(l => l.QuantityReturned == l.QuantityShipped) && Status != SalesOrderStatus.Returned)
        {
            Transition(SalesOrderStatus.Returned);
        }
    }

    public SalesOrderLine Line(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId) ?? throw new DomainValidationException("The order line does not exist.");

    private void Recalculate()
    {
        foreach (var line in _lines)
        {
            line.Price(OrderDiscountPercent);
        }

        Subtotal = _lines.Sum(l => l.Subtotal);
        DiscountTotal = _lines.Sum(l => l.DiscountAmount);
        TaxTotal = _lines.Sum(l => l.TaxAmount);
        Total = _lines.Sum(l => l.Total);
        TotalQuantity = _lines.Sum(l => l.Quantity);
    }

    private void EnsureStatus(string action, params SalesOrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidStateTransitionException("Sales order", Status.ToString(), action);
        }
    }

    private void Transition(SalesOrderStatus status)
    {
        Status = status;
        RaiseStatusChanged();
    }

    private void RaiseStatusChanged() =>
        RaiseDomainEvent(new SalesOrderStatusChangedEvent(TenantId, Id, Number, WarehouseId, Status, Total));
}

public sealed class SalesOrderLine : TenantEntity
{
    private SalesOrderLine() { }

    internal SalesOrderLine(Guid orderId, int lineNumber, SalesOrderLineInput input)
    {
        OrderId = orderId;
        LineNumber = lineNumber;
        ProductId = Guard.NotEmpty(input.ProductId, "Product");
        Sku = Guard.Required(input.Sku, "SKU", 64);
        ProductName = Guard.Required(input.ProductName, "Product name", 200);
        Quantity = Guard.Positive(input.Quantity, "Quantity");
        UnitPrice = Guard.NotNegative(input.UnitPrice, "Unit price");
        DiscountPercent = input.DiscountPercent is >= 0 and <= 100
            ? input.DiscountPercent
            : throw new DomainValidationException("Line discount must be between 0 and 100%.");
        TaxRate = input.TaxRate is >= 0 and <= 1
            ? input.TaxRate
            : throw new DomainValidationException("Tax rate must be between 0 and 1.");
    }

    public Guid OrderId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRate { get; private set; }

    /// <summary>Quantity × unit price, before discounts.</summary>
    public decimal Subtotal { get; private set; }

    /// <summary>Line discount plus this line's share of the order discount.</summary>
    public decimal DiscountAmount { get; private set; }

    public decimal TaxAmount { get; private set; }
    public decimal Total { get; private set; }
    public decimal QuantityPicked { get; private set; }
    public decimal QuantityPacked { get; private set; }
    public decimal QuantityShipped { get; private set; }
    public decimal QuantityReturned { get; private set; }

    /// <summary>Net amount (incl. tax) per unit actually charged, used for return credits.</summary>
    public decimal NetUnitPriceInclTax => Quantity == 0 ? 0 : Total / Quantity;

    internal void Price(decimal orderDiscountPercent)
    {
        Subtotal = Money.Round(Quantity * UnitPrice);
        var lineDiscount = Money.Round(Subtotal * DiscountPercent / 100m);
        var afterLine = Subtotal - lineDiscount;
        var orderDiscount = Money.Round(afterLine * orderDiscountPercent / 100m);
        var net = afterLine - orderDiscount;
        DiscountAmount = lineDiscount + orderDiscount;
        TaxAmount = Money.Round(net * TaxRate);
        Total = net + TaxAmount;
    }

    internal void Pick(decimal quantity)
    {
        Guard.Positive(quantity, "Picked quantity");
        if (QuantityPicked + quantity > Quantity)
        {
            throw new DomainException("over_pick", $"Line {LineNumber}: cannot pick more than the ordered {Quantity:0.###}.");
        }

        QuantityPicked += quantity;
    }

    internal void PackPicked() => QuantityPacked = QuantityPicked;

    internal void Ship(decimal quantity) => QuantityShipped += quantity;

    internal void Return(decimal quantity)
    {
        Guard.Positive(quantity, "Returned quantity");
        if (QuantityReturned + quantity > QuantityShipped)
        {
            throw new DomainException("over_return", $"Line {LineNumber}: only {QuantityShipped - QuantityReturned:0.###} shipped unit(s) can be returned.");
        }

        QuantityReturned += quantity;
    }
}

public static class Money
{
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}

public sealed record SalesOrderStatusChangedEvent(
    Guid TenantId,
    Guid OrderId,
    string Number,
    Guid WarehouseId,
    SalesOrderStatus Status,
    decimal Total) : IDomainEvent;
