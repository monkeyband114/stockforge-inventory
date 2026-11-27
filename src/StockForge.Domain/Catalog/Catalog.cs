using System.Text.RegularExpressions;
using StockForge.Domain.Common;

namespace StockForge.Domain.Catalog;

/// <summary>A unit of measurement (EA, BOX, KG, …). Each product has a base unit and optional alternate units.</summary>
public sealed partial class UnitOfMeasure : TenantEntity, IAuditable
{
    private UnitOfMeasure() { }

    public UnitOfMeasure(string code, string name, bool allowsFractions)
    {
        Code = NormalizeCode(code);
        Name = Guard.Required(name, "Unit name", 60);
        AllowsFractions = allowsFractions;
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>Whether quantities in this unit may be fractional (KG: yes, EA: no).</summary>
    public bool AllowsFractions { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, bool allowsFractions, bool isActive)
    {
        Name = Guard.Required(name, "Unit name", 60);
        AllowsFractions = allowsFractions;
        IsActive = isActive;
    }

    private static string NormalizeCode(string code)
    {
        var normalized = Guard.Required(code, "Unit code", 10).ToUpperInvariant();
        return CodePattern().IsMatch(normalized)
            ? normalized
            : throw new DomainValidationException("Unit code may contain only letters and digits.");
    }

    [GeneratedRegex("^[A-Z0-9]+$")]
    private static partial Regex CodePattern();
}

/// <summary>Product category. <see cref="Code"/> doubles as the SKU prefix for generated SKUs.</summary>
public sealed partial class ProductCategory : TenantEntity, IAuditable
{
    private ProductCategory() { }

    public ProductCategory(string name, string code, string? description, Guid? parentId)
    {
        Name = Guard.Required(name, "Category name", 120);
        Code = NormalizeCode(code);
        Description = Guard.Optional(description, "Description", 1000);
        ParentId = parentId;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? description, Guid? parentId, bool isActive)
    {
        if (parentId == Id)
        {
            throw new DomainValidationException("A category cannot be its own parent.");
        }

        Name = Guard.Required(name, "Category name", 120);
        Description = Guard.Optional(description, "Description", 1000);
        ParentId = parentId;
        IsActive = isActive;
    }

    private static string NormalizeCode(string code)
    {
        var normalized = Guard.Required(code, "Category code", 6).ToUpperInvariant();
        return CodePattern().IsMatch(normalized)
            ? normalized
            : throw new DomainValidationException("Category code must be 2–6 letters or digits.");
    }

    [GeneratedRegex("^[A-Z0-9]{2,6}$")]
    private static partial Regex CodePattern();
}

/// <summary>A sellable/stockable item. Inventory is tracked per warehouse in <c>InventoryItem</c>.</summary>
public sealed class Product : TenantEntity, IAuditable
{
    private readonly List<ProductUnit> _units = [];

    private Product() { }

    public Product(
        string sku,
        string name,
        string? description,
        Guid? categoryId,
        Guid baseUnitId,
        decimal costPrice,
        decimal sellingPrice,
        decimal? taxRate,
        decimal reorderLevel,
        decimal reorderQuantity)
    {
        Sku = NormalizeSku(sku);
        UpdateDetails(name, description, categoryId);
        BaseUnitId = Guard.NotEmpty(baseUnitId, "Base unit");
        UpdatePricing(costPrice, sellingPrice, taxRate);
        SetStockThresholds(reorderLevel, reorderQuantity);
        IsActive = true;
    }

    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid BaseUnitId { get; private set; }
    public string? Barcode { get; private set; }
    public decimal CostPrice { get; private set; }
    public decimal SellingPrice { get; private set; }

    /// <summary>Fractional tax rate; <c>null</c> means "use the organization default".</summary>
    public decimal? TaxRate { get; private set; }

    /// <summary>Minimum stock threshold per warehouse; at or below this level the product is "low stock".</summary>
    public decimal ReorderLevel { get; private set; }

    public decimal ReorderQuantity { get; private set; }
    public Guid? ImageFileId { get; private set; }
    public bool IsActive { get; private set; }
    public uint Version { get; private set; }

    public ProductCategory? Category { get; private set; }
    public UnitOfMeasure? BaseUnit { get; private set; }
    public IReadOnlyCollection<ProductUnit> Units => _units;

    public void UpdateDetails(string name, string? description, Guid? categoryId)
    {
        Name = Guard.Required(name, "Product name", 200);
        Description = Guard.Optional(description, "Description", 4000);
        CategoryId = categoryId;
    }

    public void UpdatePricing(decimal costPrice, decimal sellingPrice, decimal? taxRate)
    {
        CostPrice = Guard.NotNegative(costPrice, "Cost price");
        SellingPrice = Guard.NotNegative(sellingPrice, "Selling price");
        if (taxRate is < 0 or > 1)
        {
            throw new DomainValidationException("Tax rate must be between 0 and 1.");
        }

        TaxRate = taxRate;
    }

    public void SetStockThresholds(decimal reorderLevel, decimal reorderQuantity)
    {
        ReorderLevel = Guard.NotNegative(reorderLevel, "Reorder level");
        ReorderQuantity = Guard.NotNegative(reorderQuantity, "Reorder quantity");
    }

    public void SetBarcode(string? barcode) => Barcode = Barcodes.Normalize(barcode);

    public void SetImage(Guid? fileId) => ImageFileId = fileId;

    public void SetActive(bool isActive) => IsActive = isActive;

    public ProductUnit AddUnit(Guid unitOfMeasureId, decimal conversionFactor, string? barcode)
    {
        if (unitOfMeasureId == BaseUnitId)
        {
            throw new DomainValidationException("The base unit cannot be added as an alternate unit.");
        }

        if (_units.Any(u => u.UnitOfMeasureId == unitOfMeasureId))
        {
            throw new DomainValidationException("This unit is already defined for the product.");
        }

        var unit = new ProductUnit(Id, unitOfMeasureId, conversionFactor, barcode);
        _units.Add(unit);
        return unit;
    }

    public void RemoveUnit(Guid unitOfMeasureId) => _units.RemoveAll(u => u.UnitOfMeasureId == unitOfMeasureId);

    /// <summary>Synchronizes alternate units: updates existing ones in place, adds new ones, removes the rest.</summary>
    public void ReplaceUnits(IReadOnlyCollection<(Guid UnitOfMeasureId, decimal ConversionFactor, string? Barcode)> units)
    {
        if (units.Select(u => u.UnitOfMeasureId).Distinct().Count() != units.Count)
        {
            throw new DomainValidationException("Each unit may be defined only once per product.");
        }

        _units.RemoveAll(existing => units.All(u => u.UnitOfMeasureId != existing.UnitOfMeasureId));
        foreach (var (unitId, factor, barcode) in units)
        {
            var existing = _units.FirstOrDefault(u => u.UnitOfMeasureId == unitId);
            if (existing is null)
            {
                AddUnit(unitId, factor, barcode);
            }
            else
            {
                existing.Update(factor, barcode);
            }
        }
    }

    /// <summary>Converts a quantity expressed in <paramref name="unitOfMeasureId"/> to the base unit.</summary>
    public decimal ToBaseQuantity(decimal quantity, Guid? unitOfMeasureId)
    {
        if (unitOfMeasureId is null || unitOfMeasureId == BaseUnitId)
        {
            return quantity;
        }

        var unit = _units.FirstOrDefault(u => u.UnitOfMeasureId == unitOfMeasureId)
                   ?? throw new DomainValidationException("The unit is not configured for this product.");
        return quantity * unit.ConversionFactor;
    }

    private static string NormalizeSku(string sku)
    {
        var normalized = Guard.Required(sku, "SKU", 64).ToUpperInvariant();
        return normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? normalized
            : throw new DomainValidationException("SKU may contain letters, digits, '-', '_' and '.'.");
    }
}

/// <summary>An alternate unit for a product, e.g. BOX = 12 × EA.</summary>
public sealed class ProductUnit : TenantEntity
{
    private ProductUnit() { }

    internal ProductUnit(Guid productId, Guid unitOfMeasureId, decimal conversionFactor, string? barcode)
    {
        ProductId = productId;
        UnitOfMeasureId = Guard.NotEmpty(unitOfMeasureId, "Unit");
        ConversionFactor = Guard.Positive(conversionFactor, "Conversion factor");
        Barcode = Barcodes.Normalize(barcode);
    }

    public Guid ProductId { get; private set; }
    public Guid UnitOfMeasureId { get; private set; }

    /// <summary>How many base units one of this unit contains.</summary>
    public decimal ConversionFactor { get; private set; }

    public string? Barcode { get; private set; }
    public UnitOfMeasure? UnitOfMeasure { get; private set; }

    internal void Update(decimal conversionFactor, string? barcode)
    {
        ConversionFactor = Guard.Positive(conversionFactor, "Conversion factor");
        Barcode = Barcodes.Normalize(barcode);
    }
}

/// <summary>Barcode helpers: normalization and GS1 EAN-13 generation/validation.</summary>
public static class Barcodes
{
    public static string? Normalize(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var trimmed = barcode.Trim();
        if (trimmed.Length is < 4 or > 64 || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.'))
        {
            throw new DomainValidationException("Barcode must be 4–64 letters, digits, '-' or '.'.");
        }

        if (trimmed.Length == 13 && trimmed.All(char.IsAsciiDigit) && !IsValidEan13(trimmed))
        {
            throw new DomainValidationException("The EAN-13 barcode has an invalid check digit.");
        }

        return trimmed;
    }

    /// <summary>Builds an EAN-13 in the GS1 "in-store" prefix range 200–299 from a numeric seed.</summary>
    public static string CreateInternalEan13(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        var body = "2" + (sequence % 100_000_000_000L).ToString("D11", System.Globalization.CultureInfo.InvariantCulture);
        return body + Ean13CheckDigit(body);
    }

    public static bool IsValidEan13(string code) =>
        code.Length == 13 && code.All(char.IsAsciiDigit) && code[12] - '0' == Ean13CheckDigit(code[..12]);

    public static int Ean13CheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || !twelveDigits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Exactly 12 digits are required.", nameof(twelveDigits));
        }

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = twelveDigits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return (10 - (sum % 10)) % 10;
    }
}
