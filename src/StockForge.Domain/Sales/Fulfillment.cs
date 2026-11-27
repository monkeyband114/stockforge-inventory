using StockForge.Domain.Common;

namespace StockForge.Domain.Sales;

public enum ShipmentStatus
{
    Shipped = 1,
    Delivered = 2,
}

/// <summary>A physical consignment for (part of) a sales order.</summary>
public sealed class Shipment : TenantEntity, IAuditable
{
    private readonly List<ShipmentLine> _lines = [];

    private Shipment() { }

    public Shipment(string number, Guid salesOrderId, Guid warehouseId, string? carrier, string? trackingNumber, int packageCount,
        IReadOnlyCollection<(Guid OrderLineId, Guid ProductId, decimal Quantity)> lines, Guid shippedBy, DateTimeOffset at)
    {
        Number = Guard.Required(number, "Number", 32);
        SalesOrderId = salesOrderId;
        WarehouseId = warehouseId;
        Carrier = Guard.Optional(carrier, "Carrier", 100);
        TrackingNumber = Guard.Optional(trackingNumber, "Tracking number", 100);
        PackageCount = packageCount > 0 ? packageCount : throw new DomainValidationException("At least one package is required.");
        if (lines.Count == 0)
        {
            throw new DomainException("nothing_to_ship", "Nothing is packed for shipment.");
        }

        foreach (var (orderLineId, productId, quantity) in lines)
        {
            _lines.Add(new ShipmentLine(Id, orderLineId, productId, quantity));
        }

        ShippedBy = shippedBy;
        ShippedAt = at;
        Status = ShipmentStatus.Shipped;
    }

    public string Number { get; private set; } = string.Empty;
    public Guid SalesOrderId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }
    public int PackageCount { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public Guid ShippedBy { get; private set; }
    public DateTimeOffset ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public uint Version { get; private set; }
    public IReadOnlyCollection<ShipmentLine> Lines => _lines;

    public void MarkDelivered(DateTimeOffset at)
    {
        if (Status != ShipmentStatus.Shipped)
        {
            throw new InvalidStateTransitionException("Shipment", Status.ToString(), ShipmentStatus.Delivered.ToString());
        }

        Status = ShipmentStatus.Delivered;
        DeliveredAt = at;
    }
}

public sealed class ShipmentLine : TenantEntity
{
    private ShipmentLine() { }

    internal ShipmentLine(Guid shipmentId, Guid salesOrderLineId, Guid productId, decimal quantity)
    {
        ShipmentId = shipmentId;
        SalesOrderLineId = salesOrderLineId;
        ProductId = productId;
        Quantity = Guard.Positive(quantity, "Quantity");
    }

    public Guid ShipmentId { get; private set; }
    public Guid SalesOrderLineId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
}

/// <summary>Goods returned by a customer (RMA). Restockable units go back into stock; the value is credited on the invoice.</summary>
public sealed class SalesReturn : TenantEntity, IAuditable
{
    private readonly List<SalesReturnLine> _lines = [];

    private SalesReturn() { }

    public SalesReturn(string number, Guid salesOrderId, Guid warehouseId, string reason,
        IReadOnlyCollection<(Guid OrderLineId, Guid ProductId, decimal Quantity, bool Restock, decimal Credit)> lines,
        Guid receivedBy, DateTimeOffset at)
    {
        Number = Guard.Required(number, "Number", 32);
        SalesOrderId = salesOrderId;
        WarehouseId = warehouseId;
        Reason = Guard.Required(reason, "Return reason", 1000);
        if (lines.Count == 0)
        {
            throw new DomainValidationException("A return needs at least one line.");
        }

        foreach (var (orderLineId, productId, quantity, restock, credit) in lines)
        {
            _lines.Add(new SalesReturnLine(Id, orderLineId, productId, quantity, restock, credit));
        }

        CreditAmount = _lines.Sum(l => l.CreditAmount);
        ReceivedBy = receivedBy;
        ReceivedAt = at;
    }

    public string Number { get; private set; } = string.Empty;
    public Guid SalesOrderId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public decimal CreditAmount { get; private set; }
    public Guid ReceivedBy { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public IReadOnlyCollection<SalesReturnLine> Lines => _lines;
}

public sealed class SalesReturnLine : TenantEntity
{
    private SalesReturnLine() { }

    internal SalesReturnLine(Guid returnId, Guid salesOrderLineId, Guid productId, decimal quantity, bool restock, decimal creditAmount)
    {
        ReturnId = returnId;
        SalesOrderLineId = salesOrderLineId;
        ProductId = productId;
        Quantity = Guard.Positive(quantity, "Quantity");
        Restock = restock;
        CreditAmount = Guard.NotNegative(creditAmount, "Credit");
    }

    public Guid ReturnId { get; private set; }
    public Guid SalesOrderLineId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public bool Restock { get; private set; }
    public decimal CreditAmount { get; private set; }
}
