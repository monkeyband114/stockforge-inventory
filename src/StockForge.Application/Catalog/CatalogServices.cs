using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Warehouses;
using StockForge.Domain.Catalog;
using StockForge.Domain.Common;

namespace StockForge.Application.Catalog;

public sealed class UnitService(IAppDbContext db)
{
    public async Task<IReadOnlyList<UnitDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.UnitsOfMeasure.AsNoTracking().OrderBy(u => u.Code)
            .Select(u => new UnitDto(u.Id, u.Code, u.Name, u.AllowsFractions, u.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<UnitDto> CreateAsync(CreateUnitRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.UnitsOfMeasure.AnyAsync(u => u.Code == code, cancellationToken))
        {
            throw new ConflictException("duplicate_code", $"Unit '{code}' already exists.");
        }

        var unit = new UnitOfMeasure(code, request.Name, request.AllowsFractions);
        db.UnitsOfMeasure.Add(unit);
        await db.SaveChangesAsync(cancellationToken);
        return new UnitDto(unit.Id, unit.Code, unit.Name, unit.AllowsFractions, unit.IsActive);
    }

    public async Task<UnitDto> UpdateAsync(Guid id, UpdateUnitRequest request, CancellationToken cancellationToken)
    {
        var unit = await db.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw new NotFoundException("Unit", id);
        unit.Update(request.Name, request.AllowsFractions, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        return new UnitDto(unit.Id, unit.Code, unit.Name, unit.AllowsFractions, unit.IsActive);
    }
}

public sealed class CategoryService(IAppDbContext db)
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.ProductCategories.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Code, c.Description, c.ParentId, c.IsActive,
                db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.ProductCategories.AnyAsync(c => c.Code == code || c.Name == request.Name.Trim(), cancellationToken))
        {
            throw new ConflictException("duplicate_category", "A category with this code or name already exists.");
        }

        if (request.ParentId is { } parentId && !await db.ProductCategories.AnyAsync(c => c.Id == parentId, cancellationToken))
        {
            throw new NotFoundException("Parent category", parentId);
        }

        var category = new ProductCategory(request.Name, code, request.Description, request.ParentId);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Code, category.Description, category.ParentId, category.IsActive, 0);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("Category", id);
        if (request.ParentId is { } parentId)
        {
            await EnsureNoCycleAsync(id, parentId, cancellationToken);
        }

        if (await db.ProductCategories.AnyAsync(c => c.Id != id && c.Name == request.Name.Trim(), cancellationToken))
        {
            throw new ConflictException("duplicate_category", "A category with this name already exists.");
        }

        category.Update(request.Name, request.Description, request.ParentId, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Products.CountAsync(p => p.CategoryId == id, cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Code, category.Description, category.ParentId, category.IsActive, count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("Category", id);
        if (await db.Products.AnyAsync(p => p.CategoryId == id, cancellationToken) ||
            await db.ProductCategories.AnyAsync(c => c.ParentId == id, cancellationToken))
        {
            throw new DomainException("category_in_use", "The category still has products or sub-categories.");
        }

        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureNoCycleAsync(Guid categoryId, Guid parentId, CancellationToken cancellationToken)
    {
        var parents = await db.ProductCategories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentId, cancellationToken);
        if (!parents.ContainsKey(parentId))
        {
            throw new NotFoundException("Parent category", parentId);
        }

        Guid? cursor = parentId;
        var guard = 0;
        while (cursor is { } current && guard++ < 64)
        {
            if (current == categoryId)
            {
                throw new DomainValidationException("A category cannot be moved under one of its own sub-categories.");
            }

            cursor = parents.GetValueOrDefault(current);
        }
    }
}

public sealed class ProductService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITransactionRunner transactions,
    ISequenceGenerator sequences,
    IFileStorage files)
{
    private const int MaxImageBytes = 2 * 1024 * 1024;

    public async Task<PagedResult<ProductListItemDto>> ListAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var products = db.Products.AsNoTracking();
        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query.IsActive is { } isActive)
        {
            products = products.Where(p => p.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var raw = query.Search.Trim();
            var term = raw.ToLower();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) || p.Sku.ToLower().Contains(term) || p.Barcode == raw ||
                p.Units.Any(u => u.Barcode == raw));
        }

        products = query.Sort switch
        {
            "sku" => products.OrderBy(p => p.Sku),
            "price" => products.OrderBy(p => p.SellingPrice).ThenBy(p => p.Name),
            "-price" => products.OrderByDescending(p => p.SellingPrice).ThenBy(p => p.Name),
            "newest" => products.OrderByDescending(p => p.CreatedAt),
            _ => products.OrderBy(p => p.Name),
        };

        var allowed = currentUser.AccessibleWarehouseIds?.ToArray();
        var inventory = allowed is null
            ? db.InventoryItems
            : db.InventoryItems.Where(i => allowed.Contains(i.WarehouseId));

        return await products
            .Select(p => new ProductListItemDto(
                p.Id, p.Sku, p.Name, p.CategoryId, p.Category != null ? p.Category.Name : null, p.BaseUnit!.Code,
                p.Barcode, p.CostPrice, p.SellingPrice, p.ReorderLevel, p.IsActive, p.ImageFileId != null,
                inventory.Where(i => i.ProductId == p.Id).Sum(i => (decimal?)i.QuantityOnHand) ?? 0,
                inventory.Where(i => i.ProductId == p.Id).Sum(i => (decimal?)(i.QuantityOnHand - i.QuantityReserved)) ?? 0))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
                          .Include(p => p.Category)
                          .Include(p => p.BaseUnit)
                          .Include(p => p.Units).ThenInclude(u => u.UnitOfMeasure)
                          .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Product", id);

        var allowed = currentUser.AccessibleWarehouseIds?.ToArray();
        var stock = await db.InventoryItems.AsNoTracking()
            .Where(i => i.ProductId == id && (allowed == null || allowed.Contains(i.WarehouseId)))
            .OrderBy(i => i.Warehouse!.Code)
            .Select(i => new ProductStockDto(i.WarehouseId, i.Warehouse!.Code, i.Warehouse.Name, i.QuantityOnHand,
                i.QuantityReserved, i.QuantityOnHand - i.QuantityReserved, i.AverageUnitCost))
            .ToListAsync(cancellationToken);

        return new ProductDto(
            product.Id, product.Sku, product.Name, product.Description, product.CategoryId, product.Category?.Name,
            product.BaseUnitId, product.BaseUnit?.Code ?? string.Empty, product.Barcode, product.CostPrice,
            product.SellingPrice, product.TaxRate, product.ReorderLevel, product.ReorderQuantity, product.IsActive,
            product.ImageFileId.HasValue,
            product.Units.Select(u => new ProductUnitDto(u.UnitOfMeasureId, u.UnitOfMeasure?.Code ?? string.Empty,
                u.UnitOfMeasure?.Name ?? string.Empty, u.ConversionFactor, u.Barcode)).ToList(),
            stock, product.CreatedAt, product.UpdatedAt, product.Version);
    }

    /// <summary>Barcode scan lookup: matches the product barcode or any alternate-unit barcode.</summary>
    public async Task<ProductDto> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        var code = barcode.Trim();
        var id = await db.Products.AsNoTracking()
            .Where(p => p.Barcode == code || p.Units.Any(u => u.Barcode == code))
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is { } productId ? await GetAsync(productId, cancellationToken) : throw new NotFoundException("Product", code);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var id = await transactions.ExecuteAsync(async token =>
        {
            var category = await ResolveCategoryAsync(request.CategoryId, token);
            await EnsureUnitsExistAsync(request.BaseUnitId, request.Units, token);

            var sku = string.IsNullOrWhiteSpace(request.Sku)
                ? $"{category?.Code ?? "SKU"}-{await sequences.NextValueAsync(SequenceNames.Sku, token):D6}"
                : request.Sku.Trim().ToUpperInvariant();
            if (await db.Products.AnyAsync(p => p.Sku == sku, token))
            {
                throw new ConflictException("duplicate_sku", $"SKU '{sku}' is already used.");
            }

            var barcode = request.GenerateBarcode
                ? Barcodes.CreateInternalEan13(await sequences.NextValueAsync(SequenceNames.Barcode, token))
                : request.Barcode;
            await EnsureBarcodesUniqueAsync(null, barcode, request.Units, token);

            var product = new Product(sku, request.Name, request.Description, category?.Id, request.BaseUnitId,
                request.CostPrice, request.SellingPrice, request.TaxRate, request.ReorderLevel, request.ReorderQuantity);
            product.SetBarcode(barcode);
            product.ReplaceUnits((request.Units ?? []).Select(u => (u.UnitId, u.ConversionFactor, u.Barcode)).ToList());
            db.Products.Add(product);
            await db.SaveChangesAsync(token);
            return product.Id;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Product", id);
        WarehouseService.EnsureVersion(product.Version, request.Version);
        var category = await ResolveCategoryAsync(request.CategoryId, cancellationToken);
        await EnsureUnitsExistAsync(product.BaseUnitId, request.Units, cancellationToken);
        await EnsureBarcodesUniqueAsync(id, request.Barcode, request.Units, cancellationToken);

        product.UpdateDetails(request.Name, request.Description, category?.Id);
        product.UpdatePricing(request.CostPrice, request.SellingPrice, request.TaxRate);
        product.SetStockThresholds(request.ReorderLevel, request.ReorderQuantity);
        product.SetBarcode(request.Barcode);
        product.SetActive(request.IsActive);
        product.ReplaceUnits((request.Units ?? []).Select(u => (u.UnitId, u.ConversionFactor, u.Barcode)).ToList());
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes a product that never had stock; products with history must be deactivated instead.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Product", id);
        if (await db.StockMovements.AnyAsync(m => m.ProductId == id, cancellationToken) ||
            await db.StockTransfers.AnyAsync(t => t.Lines.Any(l => l.ProductId == id), cancellationToken))
        {
            throw new DomainException("product_in_use", "This product has stock history. Deactivate it instead.");
        }

        var balances = await db.InventoryItems.Where(i => i.ProductId == id).ToListAsync(cancellationToken);
        db.InventoryItems.RemoveRange(balances);
        db.Products.Remove(product);
        if (product.ImageFileId is { } imageId)
        {
            await files.DeleteAsync(imageId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetImageAsync(Guid id, string fileName, string contentType, byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length == 0 || content.Length > MaxImageBytes)
        {
            throw new DomainValidationException("Images must be between 1 byte and 2 MB.");
        }

        var detected = DetectImageType(content)
                       ?? throw new DomainValidationException("Only PNG, JPEG and WebP images are supported.");
        _ = contentType;

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Product", id);
        if (product.ImageFileId is { } previous)
        {
            await files.DeleteAsync(previous, cancellationToken);
        }

        var safeName = Path.GetFileName(fileName);
        product.SetImage(files.Stage(string.IsNullOrWhiteSpace(safeName) ? "image" : safeName, detected, content, "product-image"));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StoredFileData> GetImageAsync(Guid id, CancellationToken cancellationToken)
    {
        var imageId = await db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.ImageFileId)
            .FirstOrDefaultAsync(cancellationToken);
        return imageId is { } fileId
            ? await files.GetAsync(fileId, cancellationToken) ?? throw new NotFoundException("Product image", id)
            : throw new NotFoundException("Product image", id);
    }

    public async Task RemoveImageAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Product", id);
        if (product.ImageFileId is { } imageId)
        {
            await files.DeleteAsync(imageId, cancellationToken);
            product.SetImage(null);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Sniffs magic bytes; the client-supplied content type is never trusted.</summary>
    internal static string? DetectImageType(ReadOnlySpan<byte> data) => data switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    private async Task<ProductCategory?> ResolveCategoryAsync(Guid? categoryId, CancellationToken token) =>
        categoryId is { } id
            ? await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, token) ?? throw new NotFoundException("Category", id)
            : null;

    private async Task EnsureUnitsExistAsync(Guid baseUnitId, IReadOnlyList<ProductUnitRequest>? units, CancellationToken token)
    {
        var ids = (units ?? []).Select(u => u.UnitId).Append(baseUnitId).Distinct().ToList();
        var found = await db.UnitsOfMeasure.CountAsync(u => ids.Contains(u.Id) && u.IsActive, token);
        if (found != ids.Count)
        {
            throw new NotFoundException("Unit of measure");
        }
    }

    private async Task EnsureBarcodesUniqueAsync(Guid? productId, string? barcode, IReadOnlyList<ProductUnitRequest>? units, CancellationToken token)
    {
        var codes = (units ?? []).Select(u => u.Barcode).Append(barcode)
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).ToList();
        if (codes.Count != codes.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ConflictException("duplicate_barcode", "The same barcode is used twice.");
        }

        if (codes.Count == 0)
        {
            return;
        }

        var clash = await db.Products.AnyAsync(p => p.Id != productId && p.Barcode != null && codes.Contains(p.Barcode), token) ||
                    await db.ProductUnits.AnyAsync(u => u.ProductId != productId && u.Barcode != null && codes.Contains(u.Barcode), token);
        if (clash)
        {
            throw new ConflictException("duplicate_barcode", "A barcode is already assigned to another product.");
        }
    }
}
