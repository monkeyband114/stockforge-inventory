using FluentValidation;
using StockForge.Application.Common;
using StockForge.Application.Warehouses;
using StockForge.Domain.Procurement;

namespace StockForge.Application.Procurement;

// ---------- Suppliers ----------

public sealed record SupplierDto(
    Guid Id,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? TaxNumber,
    AddressDto? Address,
    int? PaymentTermsDays,
    int? LeadTimeDays,
    string? Notes,
    bool IsActive,
    int ProductCount,
    int OpenPurchaseOrders,
    decimal OutstandingPayables,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record SupplierRequest(
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? TaxNumber,
    AddressDto? Address,
    int? PaymentTermsDays,
    int? LeadTimeDays,
    string? Notes,
    bool IsActive = true,
    uint? Version = null);

public sealed record SupplierQuery : PageQuery
{
    public bool? IsActive { get; init; }
}

public sealed record SupplierProductDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    string? SupplierSku,
    decimal UnitCost,
    decimal MinimumOrderQuantity,
    int? LeadTimeDays,
    bool IsPreferred);

public sealed record SupplierProductRequest(
    Guid ProductId,
    string? SupplierSku,
    decimal UnitCost,
    decimal MinimumOrderQuantity,
    int? LeadTimeDays,
    bool IsPreferred);

// ---------- Purchase orders ----------

public sealed record PurchaseOrderLineRequest(Guid ProductId, decimal Quantity, decimal? UnitCost = null, decimal? TaxRate = null);

public sealed record CreatePurchaseOrderRequest(
    Guid SupplierId,
    Guid WarehouseId,
    DateOnly? ExpectedDate,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest> Lines,
    bool Submit = false);

public sealed record UpdatePurchaseOrderRequest(
    Guid SupplierId,
    Guid WarehouseId,
    DateOnly? ExpectedDate,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineRequest> Lines,
    uint? Version = null);

public sealed record PurchaseOrderQuery : PageQuery
{
    public PurchaseOrderStatus? Status { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? WarehouseId { get; init; }
    public bool OutstandingOnly { get; init; }
}

public sealed record ReceiveLineRequest(Guid LineId, decimal Quantity);

public sealed record ReceiveGoodsRequest(IReadOnlyList<ReceiveLineRequest> Lines, string? DeliveryNote, string? Notes);

public sealed record PurchaseOrderSummaryDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    Guid WarehouseId,
    string WarehouseCode,
    PurchaseOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    string Currency,
    decimal Total,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    DateTimeOffset CreatedAt);

public sealed record PurchaseOrderLineDto(
    Guid Id,
    int LineNumber,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal UnitCost,
    decimal TaxRate,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    decimal QuantityReceived,
    decimal QuantityOutstanding);

public sealed record GoodsReceiptLineDto(Guid PurchaseOrderLineId, Guid ProductId, string Sku, decimal Quantity, decimal UnitCost);

public sealed record GoodsReceiptDto(
    Guid Id,
    string Number,
    Guid PurchaseOrderId,
    Guid WarehouseId,
    string? DeliveryNote,
    string? Notes,
    string? ReceivedByName,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<GoodsReceiptLineDto> Lines);

public sealed record PurchaseOrderDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    Guid WarehouseId,
    string WarehouseCode,
    PurchaseOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    string Currency,
    string? Notes,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    decimal ReceivedValue,
    bool RequiresApproval,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    string? RejectionReason,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    IReadOnlyList<GoodsReceiptDto> Receipts,
    IReadOnlyList<SupplierInvoiceSummaryDto> Invoices,
    uint Version);

public sealed record ReorderSuggestionDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid WarehouseId,
    string WarehouseCode,
    decimal QuantityAvailable,
    decimal ReorderLevel,
    decimal SuggestedQuantity,
    decimal QuantityOnOrder,
    Guid? PreferredSupplierId,
    string? PreferredSupplierName,
    decimal? SupplierUnitCost);

// ---------- Supplier invoices ----------

public sealed record CreateSupplierInvoiceRequest(
    Guid SupplierId,
    Guid? PurchaseOrderId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    decimal Subtotal,
    decimal TaxTotal,
    string? Notes);

public sealed record SupplierInvoiceQuery : PageQuery
{
    public SupplierInvoiceStatus? Status { get; init; }
    public Guid? SupplierId { get; init; }
    public bool OutstandingOnly { get; init; }
}

public sealed record SupplierInvoiceSummaryDto(
    Guid Id,
    string Reference,
    string SupplierInvoiceNumber,
    Guid SupplierId,
    string SupplierName,
    Guid? PurchaseOrderId,
    string? PurchaseOrderNumber,
    SupplierInvoiceStatus Status,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string Currency,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    bool MatchesReceipts);

public sealed record SupplierPaymentDto(Guid Id, decimal Amount, Domain.Billing.PaymentMethod Method, string? Reference, DateTimeOffset PaidAt, string? RecordedByName);

public sealed record SupplierInvoiceDto(
    SupplierInvoiceSummaryDto Summary,
    decimal Subtotal,
    decimal TaxTotal,
    string? Notes,
    IReadOnlyList<SupplierPaymentDto> Payments,
    uint Version);

// ---------- Validators ----------

public sealed class SupplierRequestValidator : AbstractValidator<SupplierRequest>
{
    public SupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactName).MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.TaxNumber).MaximumLength(64);
        RuleFor(x => x.Address!).SetValidator(new AddressDtoValidator()).When(x => x.Address is not null);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365).When(x => x.PaymentTermsDays.HasValue);
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 365).When(x => x.LeadTimeDays.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class SupplierProductRequestValidator : AbstractValidator<SupplierProductRequest>
{
    public SupplierProductRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.SupplierSku).MaximumLength(64);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).PrecisionScale(18, 4, true);
        RuleFor(x => x.MinimumOrderQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 365).When(x => x.LeadTimeDays.HasValue);
    }
}

public sealed class PurchaseOrderLineRequestValidator : AbstractValidator<PurchaseOrderLineRequest>
{
    public PurchaseOrderLineRequestValidator()
    {
        RuleFor(l => l.ProductId).NotEmpty();
        RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000_000).PrecisionScale(18, 3, true);
        RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).PrecisionScale(18, 4, true).When(l => l.UnitCost.HasValue);
        RuleFor(l => l.TaxRate).InclusiveBetween(0, 1).When(l => l.TaxRate.HasValue);
    }
}

public sealed class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .When(x => x.Lines is { Count: > 0 }).WithMessage("Each product may appear only once.");
        RuleForEach(x => x.Lines).SetValidator(new PurchaseOrderLineRequestValidator());
    }
}

public sealed class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    public UpdatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .When(x => x.Lines is { Count: > 0 }).WithMessage("Each product may appear only once.");
        RuleForEach(x => x.Lines).SetValidator(new PurchaseOrderLineRequestValidator());
    }
}

public sealed class ReceiveGoodsRequestValidator : AbstractValidator<ReceiveGoodsRequest>
{
    public ReceiveGoodsRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.LineId).Distinct().Count() == l.Count)
            .When(x => x.Lines is { Count: > 0 }).WithMessage("Each line may appear only once.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
        RuleFor(x => x.DeliveryNote).MaximumLength(64);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class CreateSupplierInvoiceRequestValidator : AbstractValidator<CreateSupplierInvoiceRequest>
{
    public CreateSupplierInvoiceRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.SupplierInvoiceNumber).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Subtotal).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.TaxTotal).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.DueDate).Must((r, due) => due >= r.InvoiceDate).When(x => x.DueDate.HasValue)
            .WithMessage("The due date cannot be before the invoice date.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
