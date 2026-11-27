using StockForge.Domain.Billing;
using StockForge.Domain.Common;
using StockForge.Domain.Sales;

namespace StockForge.Domain.Procurement;

public sealed class Supplier : TenantEntity, IAuditable
{
    private Supplier() { }

    public Supplier(string code, string name, string? contactName, string? email, string? phone, string? taxNumber, Address? address,
        int? paymentTermsDays, int? leadTimeDays, string? notes)
    {
        Code = Guard.Required(code, "Supplier code", 32).ToUpperInvariant();
        Update(name, contactName, email, phone, taxNumber, address, paymentTermsDays, leadTimeDays, notes);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? TaxNumber { get; private set; }
    public Address? Address { get; private set; }
    public int? PaymentTermsDays { get; private set; }
    public int? LeadTimeDays { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public uint Version { get; private set; }

    public void Update(string name, string? contactName, string? email, string? phone, string? taxNumber, Address? address,
        int? paymentTermsDays, int? leadTimeDays, string? notes)
    {
        Name = Guard.Required(name, "Supplier name", 200);
        ContactName = Guard.Optional(contactName, "Contact name", 120);
        Email = Guard.Optional(email, "Email", 256);
        Phone = Guard.Optional(phone, "Phone", 40);
        TaxNumber = Guard.Optional(taxNumber, "Tax number", 64);
        Address = address;
        PaymentTermsDays = paymentTermsDays is null or (>= 0 and <= 365)
            ? paymentTermsDays
            : throw new DomainValidationException("Payment terms must be between 0 and 365 days.");
        LeadTimeDays = leadTimeDays is null or (>= 0 and <= 365)
            ? leadTimeDays
            : throw new DomainValidationException("Lead time must be between 0 and 365 days.");
        Notes = Guard.Optional(notes, "Notes", 2000);
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}

/// <summary>A product as sold by a supplier: their SKU, agreed cost and minimum order quantity.</summary>
public sealed class SupplierProduct : TenantEntity, IAuditable
{
    private SupplierProduct() { }

    public SupplierProduct(Guid supplierId, Guid productId, string? supplierSku, decimal unitCost, decimal minimumOrderQuantity,
        int? leadTimeDays, bool isPreferred)
    {
        SupplierId = Guard.NotEmpty(supplierId, "Supplier");
        ProductId = Guard.NotEmpty(productId, "Product");
        Update(supplierSku, unitCost, minimumOrderQuantity, leadTimeDays, isPreferred);
    }

    public Guid SupplierId { get; private set; }
    public Guid ProductId { get; private set; }
    public string? SupplierSku { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal MinimumOrderQuantity { get; private set; }
    public int? LeadTimeDays { get; private set; }
    public bool IsPreferred { get; private set; }

    public void Update(string? supplierSku, decimal unitCost, decimal minimumOrderQuantity, int? leadTimeDays, bool isPreferred)
    {
        SupplierSku = Guard.Optional(supplierSku, "Supplier SKU", 64);
        UnitCost = Guard.NotNegative(unitCost, "Unit cost");
        MinimumOrderQuantity = Guard.NotNegative(minimumOrderQuantity, "Minimum order quantity");
        LeadTimeDays = leadTimeDays;
        IsPreferred = isPreferred;
    }
}

public enum PurchaseOrderStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    PartiallyReceived = 4,
    Received = 5,
    Closed = 6,
    Cancelled = 7,
    Rejected = 8,
}

public readonly record struct PurchaseOrderLineInput(Guid ProductId, string Sku, string ProductName, decimal Quantity, decimal UnitCost, decimal TaxRate);

/// <summary>
/// Purchase order: Draft → PendingApproval → Approved → PartiallyReceived → Received, or Closed short,
/// Cancelled (nothing received) or Rejected. Goods receipts book stock in at the PO line cost.
/// </summary>
public sealed class PurchaseOrder : TenantEntity, IAuditable
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder() { }

    public PurchaseOrder(string number, Guid supplierId, Guid warehouseId, DateOnly orderDate, DateOnly? expectedDate, string currency,
        string? notes, Guid createdBy, string? idempotencyKey)
    {
        Number = Guard.Required(number, "Number", 32);
        OrderDate = orderDate;
        Currency = Guard.Required(currency, "Currency", 3).ToUpperInvariant();
        CreatedByUser = createdBy;
        IdempotencyKey = Guard.Optional(idempotencyKey, "Idempotency key", 100);
        Status = PurchaseOrderStatus.Draft;
        SetDetails(supplierId, warehouseId, expectedDate, notes);
    }

    public string Number { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public DateOnly? ExpectedDate { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string? Notes { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public bool RequiresApproval { get; private set; }
    public Guid CreatedByUser { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public uint Version { get; private set; }
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines;

    public decimal ReceivedValue => _lines.Sum(l => Money.Round(l.QuantityReceived * l.UnitCost * (1 + l.TaxRate)));

    public void SetDetails(Guid supplierId, Guid warehouseId, DateOnly? expectedDate, string? notes)
    {
        EnsureStatus("Edited", PurchaseOrderStatus.Draft);
        SupplierId = Guard.NotEmpty(supplierId, "Supplier");
        WarehouseId = Guard.NotEmpty(warehouseId, "Receiving warehouse");
        ExpectedDate = expectedDate is { } expected && expected < OrderDate
            ? throw new DomainValidationException("The expected date cannot be before the order date.")
            : expectedDate;
        Notes = Guard.Optional(notes, "Notes", 2000);
    }

    public void SetLines(IReadOnlyCollection<PurchaseOrderLineInput> lines)
    {
        EnsureStatus("Edited", PurchaseOrderStatus.Draft);
        if (lines.Count == 0)
        {
            throw new DomainValidationException("A purchase order needs at least one line.");
        }

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            throw new DomainValidationException("Each product may appear only once per purchase order.");
        }

        _lines.Clear();
        var number = 1;
        foreach (var line in lines)
        {
            _lines.Add(new PurchaseOrderLine(Id, number++, line));
        }

        Subtotal = _lines.Sum(l => l.Subtotal);
        TaxTotal = _lines.Sum(l => l.TaxAmount);
        Total = _lines.Sum(l => l.Total);
    }

    /// <summary>Submits; returns true when manager approval is required (total above the threshold).</summary>
    public bool Submit(decimal approvalThreshold, DateTimeOffset at)
    {
        EnsureStatus("Submitted", PurchaseOrderStatus.Draft);
        if (_lines.Count == 0)
        {
            throw new DomainValidationException("A purchase order needs at least one line.");
        }

        RequiresApproval = approvalThreshold <= 0 || Total > approvalThreshold;
        SubmittedAt = at;
        Status = PurchaseOrderStatus.PendingApproval;
        return RequiresApproval;
    }

    public void Approve(Guid approvedBy, bool automatic, DateTimeOffset at)
    {
        EnsureStatus("Approved", PurchaseOrderStatus.PendingApproval);
        if (!automatic && approvedBy == CreatedByUser)
        {
            throw new DomainException("self_approval", "You cannot approve a purchase order you created.");
        }

        if (automatic && RequiresApproval)
        {
            throw new DomainException("approval_required", "This purchase order requires approval.");
        }

        ApprovedBy = approvedBy;
        ApprovedAt = at;
        Status = PurchaseOrderStatus.Approved;
    }

    public void Reject(string reason, DateTimeOffset at)
    {
        EnsureStatus("Rejected", PurchaseOrderStatus.PendingApproval);
        RejectionReason = Guard.Required(reason, "Rejection reason", 1000);
        RejectedAt = at;
        Status = PurchaseOrderStatus.Rejected;
    }

    public void Cancel(DateTimeOffset at)
    {
        EnsureStatus("Cancelled", PurchaseOrderStatus.Draft, PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Approved);
        CancelledAt = at;
        Status = PurchaseOrderStatus.Cancelled;
    }

    /// <summary>Records received quantities (partial deliveries allowed; over-receipt is not).</summary>
    public void Receive(Guid lineId, decimal quantity)
    {
        EnsureStatus("Received", PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived);
        var line = _lines.FirstOrDefault(l => l.Id == lineId) ?? throw new DomainValidationException("The purchase order line does not exist.");
        line.Receive(quantity);
        Status = _lines.All(l => l.QuantityReceived >= l.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
    }

    /// <summary>Closes a partially received order: the outstanding quantity will not be delivered.</summary>
    public void CloseShort(DateTimeOffset at)
    {
        EnsureStatus("Closed", PurchaseOrderStatus.PartiallyReceived);
        ClosedAt = at;
        Status = PurchaseOrderStatus.Closed;
    }

    private void EnsureStatus(string action, params PurchaseOrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidStateTransitionException("Purchase order", Status.ToString(), action);
        }
    }
}

public sealed class PurchaseOrderLine : TenantEntity
{
    private PurchaseOrderLine() { }

    internal PurchaseOrderLine(Guid purchaseOrderId, int lineNumber, PurchaseOrderLineInput input)
    {
        PurchaseOrderId = purchaseOrderId;
        LineNumber = lineNumber;
        ProductId = Guard.NotEmpty(input.ProductId, "Product");
        Sku = Guard.Required(input.Sku, "SKU", 64);
        ProductName = Guard.Required(input.ProductName, "Product name", 200);
        Quantity = Guard.Positive(input.Quantity, "Quantity");
        UnitCost = Guard.NotNegative(input.UnitCost, "Unit cost");
        TaxRate = input.TaxRate is >= 0 and <= 1 ? input.TaxRate : throw new DomainValidationException("Tax rate must be between 0 and 1.");
        Subtotal = Money.Round(Quantity * UnitCost);
        TaxAmount = Money.Round(Subtotal * TaxRate);
        Total = Subtotal + TaxAmount;
    }

    public Guid PurchaseOrderId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TaxRate { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Total { get; private set; }
    public decimal QuantityReceived { get; private set; }

    public decimal QuantityOutstanding => Quantity - QuantityReceived;

    internal void Receive(decimal quantity)
    {
        Guard.Positive(quantity, "Received quantity");
        if (quantity > QuantityOutstanding)
        {
            throw new DomainException("over_receipt", $"Line {LineNumber}: only {QuantityOutstanding:0.###} unit(s) are outstanding.");
        }

        QuantityReceived += quantity;
    }
}

/// <summary>Goods received note: what physically arrived against a purchase order (partial deliveries allowed).</summary>
public sealed class GoodsReceipt : TenantEntity, IAuditable
{
    private readonly List<GoodsReceiptLine> _lines = [];

    private GoodsReceipt() { }

    public GoodsReceipt(string number, Guid purchaseOrderId, Guid warehouseId, string? deliveryNote, string? notes,
        IReadOnlyCollection<(Guid OrderLineId, Guid ProductId, decimal Quantity, decimal UnitCost)> lines, Guid receivedBy, DateTimeOffset at)
    {
        Number = Guard.Required(number, "Number", 32);
        PurchaseOrderId = purchaseOrderId;
        WarehouseId = warehouseId;
        DeliveryNote = Guard.Optional(deliveryNote, "Delivery note", 64);
        Notes = Guard.Optional(notes, "Notes", 1000);
        if (lines.Count == 0)
        {
            throw new DomainValidationException("A goods receipt needs at least one line.");
        }

        foreach (var (orderLineId, productId, quantity, unitCost) in lines)
        {
            _lines.Add(new GoodsReceiptLine(Id, orderLineId, productId, quantity, unitCost));
        }

        ReceivedBy = receivedBy;
        ReceivedAt = at;
    }

    public string Number { get; private set; } = string.Empty;
    public Guid PurchaseOrderId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string? DeliveryNote { get; private set; }
    public string? Notes { get; private set; }
    public Guid ReceivedBy { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines;
}

public sealed class GoodsReceiptLine : TenantEntity
{
    private GoodsReceiptLine() { }

    internal GoodsReceiptLine(Guid goodsReceiptId, Guid purchaseOrderLineId, Guid productId, decimal quantity, decimal unitCost)
    {
        GoodsReceiptId = goodsReceiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
        ProductId = productId;
        Quantity = Guard.Positive(quantity, "Quantity");
        UnitCost = Guard.NotNegative(unitCost, "Unit cost");
    }

    public Guid GoodsReceiptId { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
}

public enum SupplierInvoiceStatus
{
    Open = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Overdue = 4,
    Cancelled = 5,
}

/// <summary>A bill received from a supplier (accounts payable), optionally matched to a purchase order.</summary>
public sealed class SupplierInvoice : TenantEntity, IAuditable
{
    private readonly List<SupplierPayment> _payments = [];

    private SupplierInvoice() { }

    public SupplierInvoice(string reference, Guid supplierId, Guid? purchaseOrderId, string supplierInvoiceNumber, DateOnly invoiceDate,
        DateOnly dueDate, string currency, decimal subtotal, decimal taxTotal, bool matchesReceipts, string? notes)
    {
        Reference = Guard.Required(reference, "Reference", 32);
        SupplierId = Guard.NotEmpty(supplierId, "Supplier");
        PurchaseOrderId = purchaseOrderId;
        SupplierInvoiceNumber = Guard.Required(supplierInvoiceNumber, "Supplier invoice number", 64);
        InvoiceDate = invoiceDate;
        DueDate = dueDate >= invoiceDate ? dueDate : throw new DomainValidationException("The due date cannot be before the invoice date.");
        Currency = Guard.Required(currency, "Currency", 3).ToUpperInvariant();
        Subtotal = Money.Round(Guard.NotNegative(subtotal, "Subtotal"));
        TaxTotal = Money.Round(Guard.NotNegative(taxTotal, "Tax"));
        Total = Subtotal + TaxTotal;
        MatchesReceipts = matchesReceipts;
        Notes = Guard.Optional(notes, "Notes", 1000);
        Status = SupplierInvoiceStatus.Open;
    }

    public string Reference { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Guid? PurchaseOrderId { get; private set; }
    public string SupplierInvoiceNumber { get; private set; } = string.Empty;
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string Currency { get; private set; } = "USD";
    public decimal Subtotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public decimal AmountPaid { get; private set; }

    /// <summary>Three-way match: the invoiced total does not exceed the value of goods received on the PO.</summary>
    public bool MatchesReceipts { get; private set; }

    public string? Notes { get; private set; }
    public SupplierInvoiceStatus Status { get; private set; }
    public uint Version { get; private set; }
    public IReadOnlyCollection<SupplierPayment> Payments => _payments;

    public decimal BalanceDue => Total - AmountPaid;

    public SupplierPayment RecordPayment(decimal amount, PaymentMethod method, string? reference, DateTimeOffset paidAt, Guid recordedBy, DateOnly today)
    {
        if (Status is SupplierInvoiceStatus.Cancelled or SupplierInvoiceStatus.Paid)
        {
            throw new InvalidStateTransitionException("Supplier invoice", Status.ToString(), "paid");
        }

        Guard.Positive(amount, "Payment amount");
        if (amount > BalanceDue)
        {
            throw new DomainException("overpayment", $"The payment exceeds the balance due of {BalanceDue:0.00}.");
        }

        var payment = new SupplierPayment(Id, Money.Round(amount), method, reference, paidAt, recordedBy);
        _payments.Add(payment);
        AmountPaid += payment.Amount;
        Refresh(today);
        return payment;
    }

    public void Refresh(DateOnly today)
    {
        if (Status == SupplierInvoiceStatus.Cancelled)
        {
            return;
        }

        Status = BalanceDue <= 0 ? SupplierInvoiceStatus.Paid
            : DueDate < today ? SupplierInvoiceStatus.Overdue
            : AmountPaid > 0 ? SupplierInvoiceStatus.PartiallyPaid
            : SupplierInvoiceStatus.Open;
    }

    public void Cancel()
    {
        if (AmountPaid > 0)
        {
            throw new DomainException("invoice_paid", "The supplier invoice has payments recorded and cannot be cancelled.");
        }

        Status = SupplierInvoiceStatus.Cancelled;
    }
}

public sealed class SupplierPayment : TenantEntity
{
    private SupplierPayment() { }

    internal SupplierPayment(Guid supplierInvoiceId, decimal amount, PaymentMethod method, string? reference, DateTimeOffset paidAt, Guid recordedBy)
    {
        SupplierInvoiceId = supplierInvoiceId;
        Amount = amount;
        Method = Enum.IsDefined(method) ? method : throw new DomainValidationException("Unknown payment method.");
        Reference = Guard.Optional(reference, "Reference", 100);
        PaidAt = paidAt;
        RecordedBy = recordedBy;
    }

    public Guid SupplierInvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }
    public Guid RecordedBy { get; private set; }
}
