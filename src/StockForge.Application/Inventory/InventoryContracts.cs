using FluentValidation;
using StockForge.Application.Common;
using StockForge.Domain.Inventory;

namespace StockForge.Application.Inventory;

// ---------- Queries ----------

public sealed record InventoryQuery : PageQuery
{
    public Guid? WarehouseId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? CategoryId { get; init; }
    public bool LowStockOnly { get; init; }
    public bool InStockOnly { get; init; }
}

public sealed record MovementQuery : PageQuery
{
    public Guid? WarehouseId { get; init; }
    public Guid? ProductId { get; init; }
    public StockMovementType? Type { get; init; }
    public MovementReferenceType? ReferenceType { get; init; }
    public Guid? ReferenceId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

// ---------- Responses ----------

public sealed record InventoryBalanceDto(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid ProductId,
    string Sku,
    string ProductName,
    string? CategoryName,
    string UnitCode,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityAvailable,
    decimal ReorderLevel,
    bool IsLowStock,
    decimal AverageUnitCost,
    decimal StockValue,
    DateTimeOffset? LastMovementAt);

public sealed record StockMovementDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    StockMovementType Type,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal QuantityDelta,
    decimal ReservedDelta,
    decimal OnHandAfter,
    decimal ReservedAfter,
    decimal UnitCost,
    MovementReferenceType ReferenceType,
    Guid? ReferenceId,
    string? ReferenceNumber,
    string? Reason,
    Guid? CreatedBy,
    string? CreatedByName);

public sealed record WarehouseValuationDto(Guid WarehouseId, string WarehouseCode, string WarehouseName, decimal Units, decimal CostValue, decimal RetailValue);

public sealed record CategoryValuationDto(Guid? CategoryId, string CategoryName, decimal Units, decimal CostValue, decimal RetailValue);

public sealed record InventoryValuationDto(
    decimal TotalUnits,
    decimal TotalCostValue,
    decimal TotalRetailValue,
    IReadOnlyList<WarehouseValuationDto> Warehouses,
    IReadOnlyList<CategoryValuationDto> Categories);

public sealed record StockReservationDto(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal QuantityConsumed,
    decimal QuantityReleased,
    decimal RemainingQuantity,
    ReservationStatus Status,
    MovementReferenceType ReferenceType,
    Guid? ReferenceId,
    string? ReferenceNumber,
    DateTimeOffset? ExpiresAt,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record StockAdjustmentDto(
    Guid Id,
    string Number,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    string Sku,
    string ProductName,
    AdjustmentReason Reason,
    decimal QuantityChange,
    decimal SystemQuantity,
    decimal? CountedQuantity,
    string? Notes,
    AdjustmentStatus Status,
    Guid RequestedBy,
    string? RequestedByName,
    DateTimeOffset RequestedAt,
    Guid? ReviewedBy,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt,
    string? ReviewNotes);

public sealed record WarehouseRefDto(Guid Id, string Code, string Name);

public sealed record StockTransferLineDto(Guid ProductId, string Sku, string ProductName, decimal Quantity, decimal UnitCost);

public sealed record StockTransferDto(
    Guid Id,
    string Number,
    WarehouseRefDto Source,
    WarehouseRefDto Destination,
    TransferStatus Status,
    string? Notes,
    Guid RequestedBy,
    string? RequestedByName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? CancelledAt,
    decimal TotalQuantity,
    IReadOnlyList<StockTransferLineDto> Lines);

// ---------- Commands ----------

public sealed record StockInRequest(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    Guid? UnitId,
    decimal? UnitCost,
    string? ReferenceNumber,
    string? Notes);

public sealed record StockOutRequest(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    Guid? UnitId,
    string? ReferenceNumber,
    string? Reason);

public sealed record DamagedStockRequest(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    Guid? UnitId,
    string Reason);

public sealed record ReturnedStockRequest(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    Guid? UnitId,
    decimal? UnitCost,
    string? ReferenceNumber,
    string Reason);

public sealed record CreateReservationRequest(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    string? ReferenceNumber,
    DateTimeOffset? ExpiresAt,
    string? Notes);

public sealed record StockCountRequest(Guid WarehouseId, Guid ProductId, decimal CountedQuantity, string? Notes);

public sealed record StockCorrectionRequest(
    Guid WarehouseId,
    Guid ProductId,
    AdjustmentReason Reason,
    decimal QuantityChange,
    string? Notes);

public sealed record ReviewAdjustmentRequest(string? Notes);

public sealed record AdjustmentQuery : PageQuery
{
    public Guid? WarehouseId { get; init; }
    public AdjustmentStatus? Status { get; init; }
}

public sealed record TransferLineRequest(Guid ProductId, decimal Quantity);

public sealed record CreateTransferRequest(
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    IReadOnlyList<TransferLineRequest> Lines,
    string? Notes);

public sealed record TransferQuery : PageQuery
{
    public Guid? WarehouseId { get; init; }
    public TransferStatus? Status { get; init; }
}

public sealed record ReservationQuery : PageQuery
{
    public Guid? WarehouseId { get; init; }
    public Guid? ProductId { get; init; }
    public ReservationStatus? Status { get; init; }
}

// ---------- Validators ----------

internal static class QuantityRules
{
    public const decimal Max = 1_000_000_000m;

    public static IRuleBuilderOptions<T, decimal> ValidQuantity<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).LessThanOrEqualTo(Max).PrecisionScale(18, 3, ignoreTrailingZeros: true);
}

public sealed class StockInRequestValidator : AbstractValidator<StockInRequest>
{
    public StockInRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).ValidQuantity();
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost.HasValue);
        RuleFor(x => x.ReferenceNumber).MaximumLength(64);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class StockOutRequestValidator : AbstractValidator<StockOutRequest>
{
    public StockOutRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).ValidQuantity();
        RuleFor(x => x.ReferenceNumber).MaximumLength(64);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class DamagedStockRequestValidator : AbstractValidator<DamagedStockRequest>
{
    public DamagedStockRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).ValidQuantity();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ReturnedStockRequestValidator : AbstractValidator<ReturnedStockRequest>
{
    public ReturnedStockRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).ValidQuantity();
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost.HasValue);
        RuleFor(x => x.ReferenceNumber).MaximumLength(64);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).ValidQuantity();
        RuleFor(x => x.ReferenceNumber).MaximumLength(64);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.ExpiresAt).Must(e => e > clock.GetUtcNow()).When(x => x.ExpiresAt.HasValue)
            .WithMessage("Expiry must be in the future.");
    }
}

public sealed class StockCountRequestValidator : AbstractValidator<StockCountRequest>
{
    public StockCountRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0).LessThanOrEqualTo(QuantityRules.Max);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class StockCorrectionRequestValidator : AbstractValidator<StockCorrectionRequest>
{
    public StockCorrectionRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Reason).IsInEnum().NotEqual(AdjustmentReason.CycleCount)
            .WithMessage("Use the count endpoint for cycle counts.");
        RuleFor(x => x.QuantityChange).NotEqual(0).InclusiveBetween(-QuantityRules.Max, QuantityRules.Max);
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CreateTransferRequestValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferRequestValidator()
    {
        RuleFor(x => x.SourceWarehouseId).NotEmpty();
        RuleFor(x => x.DestinationWarehouseId).NotEmpty().NotEqual(x => x.SourceWarehouseId)
            .WithMessage("Source and destination must differ.");
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= 200).WithMessage("A transfer can have at most 200 lines.");
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .When(x => x.Lines is { Count: > 0 })
            .WithMessage("Each product may appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).ValidQuantity();
        });
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
