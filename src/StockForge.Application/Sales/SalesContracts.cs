using FluentValidation;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Warehouses;
using StockForge.Domain.Billing;
using StockForge.Domain.Sales;

namespace StockForge.Application.Sales;

// ---------- Customers ----------

public sealed record CustomerDto(
    Guid Id,
    string Code,
    string Name,
    string? Email,
    string? Phone,
    string? TaxNumber,
    AddressDto? BillingAddress,
    AddressDto? ShippingAddress,
    int? PaymentTermsDays,
    decimal? CreditLimit,
    string? Notes,
    bool IsActive,
    decimal OpenBalance,
    int OrderCount,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record CustomerRequest(
    string Name,
    string? Email,
    string? Phone,
    string? TaxNumber,
    AddressDto? BillingAddress,
    AddressDto? ShippingAddress,
    int? PaymentTermsDays,
    decimal? CreditLimit,
    string? Notes,
    bool IsActive = true,
    uint? Version = null);

public sealed record CustomerQuery : PageQuery
{
    public bool? IsActive { get; init; }
}

// ---------- Orders ----------

public sealed record OrderLineRequest(Guid ProductId, decimal Quantity, decimal? UnitPrice = null, decimal DiscountPercent = 0);

public sealed record CreateOrderRequest(
    Guid CustomerId,
    Guid WarehouseId,
    DateOnly? RequiredDate,
    string? CustomerReference,
    string? Notes,
    decimal DiscountPercent,
    AddressDto? ShippingAddress,
    IReadOnlyList<OrderLineRequest> Lines,
    bool Submit = false);

public sealed record UpdateOrderRequest(
    Guid CustomerId,
    Guid WarehouseId,
    DateOnly? RequiredDate,
    string? CustomerReference,
    string? Notes,
    decimal DiscountPercent,
    AddressDto? ShippingAddress,
    IReadOnlyList<OrderLineRequest> Lines,
    uint? Version = null);

public sealed record OrderQuery : PageQuery
{
    public SalesOrderStatus? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? WarehouseId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record ReasonRequest(string Reason);

public sealed record ApproveOrderRequest(string? Notes);

public sealed record PickLineRequest(Guid LineId, decimal Quantity);

public sealed record RecordPickRequest(IReadOnlyList<PickLineRequest> Lines);

public sealed record ShipOrderRequest(string? Carrier, string? TrackingNumber, int PackageCount = 1);

public sealed record ReturnLineRequest(Guid LineId, decimal Quantity, bool Restock = true);

public sealed record CreateReturnRequest(IReadOnlyList<ReturnLineRequest> Lines, string Reason);

public sealed record OrderLineDto(
    Guid Id,
    int LineNumber,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal TaxRate,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total,
    decimal QuantityPicked,
    decimal QuantityPacked,
    decimal QuantityShipped,
    decimal QuantityReturned,
    decimal? QuantityAvailable);

public sealed record OrderSummaryDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseCode,
    SalesOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? RequiredDate,
    string Currency,
    decimal Total,
    decimal TotalQuantity,
    bool RequiresApproval,
    DateTimeOffset CreatedAt);

public sealed record OrderInvoiceDto(Guid Id, string Number, InvoiceStatus Status, decimal Total, decimal BalanceDue, bool HasPdf);

public sealed record ShipmentLineDto(Guid SalesOrderLineId, Guid ProductId, string Sku, decimal Quantity);

public sealed record ShipmentDto(
    Guid Id,
    string Number,
    Guid SalesOrderId,
    string OrderNumber,
    Guid WarehouseId,
    string? Carrier,
    string? TrackingNumber,
    int PackageCount,
    ShipmentStatus Status,
    DateTimeOffset ShippedAt,
    DateTimeOffset? DeliveredAt,
    IReadOnlyList<ShipmentLineDto> Lines);

public sealed record SalesReturnLineDto(Guid SalesOrderLineId, Guid ProductId, string Sku, decimal Quantity, bool Restock, decimal CreditAmount);

public sealed record SalesReturnDto(
    Guid Id,
    string Number,
    Guid SalesOrderId,
    string Reason,
    decimal CreditAmount,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<SalesReturnLineDto> Lines);

public sealed record OrderDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    SalesOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? RequiredDate,
    string? CustomerReference,
    string? Notes,
    string Currency,
    decimal DiscountPercent,
    AddressDto? ShippingAddress,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    decimal TotalQuantity,
    bool RequiresApproval,
    string? ApprovalReason,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    string? RejectionReason,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    IReadOnlyList<OrderLineDto> Lines,
    OrderInvoiceDto? Invoice,
    IReadOnlyList<ShipmentDto> Shipments,
    IReadOnlyList<SalesReturnDto> Returns,
    uint Version);

public sealed record FulfillmentQueueItemDto(
    Guid OrderId,
    string Number,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseCode,
    SalesOrderStatus Status,
    DateOnly? RequiredDate,
    DateTimeOffset? ApprovedAt,
    decimal TotalQuantity,
    decimal QuantityPicked,
    decimal QuantityPacked,
    decimal QuantityShipped);

// ---------- Validators ----------

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.TaxNumber).MaximumLength(64);
        RuleFor(x => x.BillingAddress!).SetValidator(new AddressDtoValidator()).When(x => x.BillingAddress is not null);
        RuleFor(x => x.ShippingAddress!).SetValidator(new AddressDtoValidator()).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365).When(x => x.PaymentTermsDays.HasValue);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0).When(x => x.CreditLimit.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.CustomerReference).MaximumLength(64);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.ShippingAddress!).SetValidator(new AddressDtoValidator()).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .When(x => x.Lines is { Count: > 0 }).WithMessage("Each product may appear only once.");
        RuleForEach(x => x.Lines).SetValidator(new OrderLineRequestValidator());
    }
}

public sealed class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    public UpdateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.CustomerReference).MaximumLength(64);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.ShippingAddress!).SetValidator(new AddressDtoValidator()).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .When(x => x.Lines is { Count: > 0 }).WithMessage("Each product may appear only once.");
        RuleForEach(x => x.Lines).SetValidator(new OrderLineRequestValidator());
    }
}

public sealed class OrderLineRequestValidator : AbstractValidator<OrderLineRequest>
{
    public OrderLineRequestValidator()
    {
        RuleFor(l => l.ProductId).NotEmpty();
        RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000_000).PrecisionScale(18, 3, true);
        RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true).When(l => l.UnitPrice.HasValue);
        RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class RecordPickRequestValidator : AbstractValidator<RecordPickRequest>
{
    public RecordPickRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class ShipOrderRequestValidator : AbstractValidator<ShipOrderRequest>
{
    public ShipOrderRequestValidator()
    {
        RuleFor(x => x.Carrier).MaximumLength(100);
        RuleFor(x => x.TrackingNumber).MaximumLength(100);
        RuleFor(x => x.PackageCount).InclusiveBetween(1, 10_000);
    }
}

public sealed class CreateReturnRequestValidator : AbstractValidator<CreateReturnRequest>
{
    public CreateReturnRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

// ---------- Background jobs ----------

public sealed record GenerateInvoicePdfJob(Guid InvoiceId, bool EmailCustomer) : IJob
{
    public static string JobType => "billing.invoice_pdf";
}

public sealed record OrderConfirmationEmailJob(Guid OrderId) : IJob
{
    public static string JobType => "sales.order_confirmation_email";
}

public sealed record ShipmentNotificationJob(Guid ShipmentId) : IJob
{
    public static string JobType => "sales.shipment_notification";
}
