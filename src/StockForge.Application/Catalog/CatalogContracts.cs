using FluentValidation;
using StockForge.Application.Common;

namespace StockForge.Application.Catalog;

public sealed record UnitDto(Guid Id, string Code, string Name, bool AllowsFractions, bool IsActive);

public sealed record CreateUnitRequest(string Code, string Name, bool AllowsFractions);

public sealed record UpdateUnitRequest(string Name, bool AllowsFractions, bool IsActive);

public sealed record CategoryDto(Guid Id, string Name, string Code, string? Description, Guid? ParentId, bool IsActive, int ProductCount);

public sealed record CreateCategoryRequest(string Name, string Code, string? Description, Guid? ParentId);

public sealed record UpdateCategoryRequest(string Name, string? Description, Guid? ParentId, bool IsActive);

public sealed record ProductUnitDto(Guid UnitId, string UnitCode, string UnitName, decimal ConversionFactor, string? Barcode);

public sealed record ProductUnitRequest(Guid UnitId, decimal ConversionFactor, string? Barcode);

public sealed record ProductListItemDto(
    Guid Id,
    string Sku,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    string BaseUnitCode,
    string? Barcode,
    decimal CostPrice,
    decimal SellingPrice,
    decimal ReorderLevel,
    bool IsActive,
    bool HasImage,
    decimal QuantityOnHand,
    decimal QuantityAvailable);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    Guid BaseUnitId,
    string BaseUnitCode,
    string? Barcode,
    decimal CostPrice,
    decimal SellingPrice,
    decimal? TaxRate,
    decimal ReorderLevel,
    decimal ReorderQuantity,
    bool IsActive,
    bool HasImage,
    IReadOnlyList<ProductUnitDto> Units,
    IReadOnlyList<ProductStockDto> Stock,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version);

public sealed record ProductStockDto(Guid WarehouseId, string WarehouseCode, string WarehouseName, decimal QuantityOnHand, decimal QuantityReserved, decimal QuantityAvailable, decimal AverageUnitCost);

public sealed record CreateProductRequest(
    string? Sku,
    string Name,
    string? Description,
    Guid? CategoryId,
    Guid BaseUnitId,
    string? Barcode,
    bool GenerateBarcode,
    decimal CostPrice,
    decimal SellingPrice,
    decimal? TaxRate,
    decimal ReorderLevel,
    decimal ReorderQuantity,
    IReadOnlyList<ProductUnitRequest>? Units) : IProductFields;

public sealed record UpdateProductRequest(
    string Name,
    string? Description,
    Guid? CategoryId,
    string? Barcode,
    decimal CostPrice,
    decimal SellingPrice,
    decimal? TaxRate,
    decimal ReorderLevel,
    decimal ReorderQuantity,
    bool IsActive,
    IReadOnlyList<ProductUnitRequest>? Units,
    uint? Version) : IProductFields;

public sealed record ProductQuery : PageQuery
{
    public Guid? CategoryId { get; init; }
    public bool? IsActive { get; init; }
    public string? Sort { get; init; }
}

public sealed class CreateUnitRequestValidator : AbstractValidator<CreateUnitRequest>
{
    public CreateUnitRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10).Matches("^[A-Za-z0-9]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
    }
}

public sealed class UpdateUnitRequestValidator : AbstractValidator<UpdateUnitRequest>
{
    public UpdateUnitRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Code).NotEmpty().Matches("^[A-Za-z0-9]{2,6}$").WithMessage("Code must be 2–6 letters or digits.");
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

/// <summary>Fields shared by create and update product requests, validated by one rule set.</summary>
public interface IProductFields
{
    string Name { get; }
    string? Description { get; }
    string? Barcode { get; }
    decimal CostPrice { get; }
    decimal SellingPrice { get; }
    decimal? TaxRate { get; }
    decimal ReorderLevel { get; }
    decimal ReorderQuantity { get; }
    IReadOnlyList<ProductUnitRequest>? Units { get; }
}

public sealed class ProductFieldsValidator : AbstractValidator<IProductFields>
{
    public ProductFieldsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 4, true);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 1).When(x => x.TaxRate.HasValue)
            .WithMessage("Tax rate is a fraction between 0 and 1 (0.075 = 7.5%).");
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Barcode).MaximumLength(64).Matches("^[A-Za-z0-9.-]*$");
        RuleForEach(x => x.Units).ChildRules(u =>
        {
            u.RuleFor(x => x.UnitId).NotEmpty();
            u.RuleFor(x => x.ConversionFactor).GreaterThan(0);
            u.RuleFor(x => x.Barcode).MaximumLength(64);
        });
    }
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).MaximumLength(64).Matches("^[A-Za-z0-9._-]*$").When(x => !string.IsNullOrEmpty(x.Sku));
        RuleFor(x => x.BaseUnitId).NotEmpty();
        RuleFor(x => x.Barcode).Empty().When(x => x.GenerateBarcode)
            .WithMessage("Provide a barcode or ask for one to be generated, not both.");
        Include(new ProductFieldsValidator());
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator() => Include(new ProductFieldsValidator());
}
