using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Domain.Catalog;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;

namespace StockForge.Application.Inventory;

/// <summary>Stock balances, direct stock operations (in/out/damaged/returned), movement history and valuation.</summary>
public sealed class InventoryService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITransactionRunner transactions,
    InventoryLedger ledger,
    IUserDirectory users)
{
    public async Task<PagedResult<InventoryBalanceDto>> GetBalancesAsync(InventoryQuery query, CancellationToken cancellationToken)
    {
        var items = db.InventoryItems.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var allowedIds = allowed.ToArray();
            items = items.Where(i => allowedIds.Contains(i.WarehouseId));
        }

        if (query.WarehouseId is { } warehouseId)
        {
            items = items.Where(i => i.WarehouseId == warehouseId);
        }

        if (query.ProductId is { } productId)
        {
            items = items.Where(i => i.ProductId == productId);
        }

        if (query.CategoryId is { } categoryId)
        {
            items = items.Where(i => i.Product!.CategoryId == categoryId);
        }

        if (query.InStockOnly)
        {
            items = items.Where(i => i.QuantityOnHand > 0);
        }

        if (query.LowStockOnly)
        {
            items = items.Where(i => i.Product!.IsActive && i.QuantityOnHand - i.QuantityReserved <= i.Product.ReorderLevel);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            items = items.Where(i =>
                i.Product!.Name.ToLower().Contains(term) ||
                i.Product.Sku.ToLower().Contains(term) ||
                i.Product.Barcode == query.Search.Trim());
        }

        return await items
            .OrderBy(i => i.Product!.Name).ThenBy(i => i.Warehouse!.Code)
            .Select(i => new InventoryBalanceDto(
                i.Id,
                i.WarehouseId,
                i.Warehouse!.Code,
                i.Warehouse.Name,
                i.ProductId,
                i.Product!.Sku,
                i.Product.Name,
                i.Product.Category != null ? i.Product.Category.Name : null,
                i.Product.BaseUnit!.Code,
                i.QuantityOnHand,
                i.QuantityReserved,
                i.QuantityOnHand - i.QuantityReserved,
                i.Product.ReorderLevel,
                i.QuantityOnHand - i.QuantityReserved <= i.Product.ReorderLevel,
                i.AverageUnitCost,
                i.QuantityOnHand * i.AverageUnitCost,
                i.LastMovementAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public Task<StockMovementDto> StockInAsync(StockInRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.WarehouseId, request.ProductId, requireActiveProduct: true, (item, product) =>
        {
            var factor = product.ToBaseQuantity(1, request.UnitId);
            var unitCost = request.UnitCost.HasValue ? decimal.Round(request.UnitCost.Value / factor, 4) : product.CostPrice;
            return ledger.Receive(item, request.Quantity * factor, unitCost, StockMovementType.StockIn,
                MovementReference.Manual(request.ReferenceNumber), request.Notes);
        }, cancellationToken);

    public Task<StockMovementDto> StockOutAsync(StockOutRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.WarehouseId, request.ProductId, requireActiveProduct: false, (item, product) =>
            ledger.Issue(item, product.ToBaseQuantity(request.Quantity, request.UnitId), StockMovementType.StockOut,
                MovementReference.Manual(request.ReferenceNumber), request.Reason), cancellationToken);

    public Task<StockMovementDto> RecordDamageAsync(DamagedStockRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.WarehouseId, request.ProductId, requireActiveProduct: false, (item, product) =>
            ledger.Issue(item, product.ToBaseQuantity(request.Quantity, request.UnitId), StockMovementType.Damaged,
                MovementReference.Manual(), request.Reason), cancellationToken);

    public Task<StockMovementDto> RecordReturnAsync(ReturnedStockRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.WarehouseId, request.ProductId, requireActiveProduct: false, (item, product) =>
        {
            var factor = product.ToBaseQuantity(1, request.UnitId);
            var unitCost = request.UnitCost.HasValue ? decimal.Round(request.UnitCost.Value / factor, 4) : item.AverageUnitCost;
            return ledger.Receive(item, request.Quantity * factor, unitCost, StockMovementType.Returned,
                new MovementReference(MovementReferenceType.SalesReturn, null, request.ReferenceNumber), request.Reason);
        }, cancellationToken);

    public async Task<PagedResult<StockMovementDto>> GetMovementsAsync(MovementQuery query, CancellationToken cancellationToken)
    {
        var movements = db.StockMovements.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var allowedIds = allowed.ToArray();
            movements = movements.Where(m => allowedIds.Contains(m.WarehouseId));
        }

        if (query.WarehouseId is { } warehouseId)
        {
            movements = movements.Where(m => m.WarehouseId == warehouseId);
        }

        if (query.ProductId is { } productId)
        {
            movements = movements.Where(m => m.ProductId == productId);
        }

        if (query.Type is { } type)
        {
            movements = movements.Where(m => m.Type == type);
        }

        if (query.ReferenceType is { } referenceType)
        {
            movements = movements.Where(m => m.ReferenceType == referenceType);
        }

        if (query.ReferenceId is { } referenceId)
        {
            movements = movements.Where(m => m.ReferenceId == referenceId);
        }

        if (query.From is { } from)
        {
            movements = movements.Where(m => m.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            movements = movements.Where(m => m.CreatedAt < to);
        }

        var projected =
            from m in movements
            join p in db.Products on m.ProductId equals p.Id
            join w in db.Warehouses on m.WarehouseId equals w.Id
            select new { m, p.Sku, p.Name, w.Code };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            projected = projected.Where(x =>
                x.Name.ToLower().Contains(term) || x.Sku.ToLower().Contains(term) ||
                (x.m.ReferenceNumber != null && x.m.ReferenceNumber.ToLower().Contains(term)));
        }

        var page = await projected
            .OrderByDescending(x => x.m.CreatedAt).ThenByDescending(x => x.m.Id)
            .Select(x => new StockMovementDto(
                x.m.Id, x.m.CreatedAt, x.m.Type, x.m.WarehouseId, x.Code, x.m.ProductId, x.Sku, x.Name,
                x.m.QuantityDelta, x.m.ReservedDelta, x.m.OnHandAfter, x.m.ReservedAfter, x.m.UnitCost,
                x.m.ReferenceType, x.m.ReferenceId, x.m.ReferenceNumber, x.m.Reason, x.m.CreatedBy, null))
            .ToPagedResultAsync(query, cancellationToken);

        return page with { Items = await WithUserNamesAsync(page.Items, cancellationToken) };
    }

    public async Task<StockMovementDto> GetMovementAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await (
                from m in db.StockMovements.AsNoTracking().Where(m => m.Id == id)
                join p in db.Products on m.ProductId equals p.Id
                join w in db.Warehouses on m.WarehouseId equals w.Id
                select new StockMovementDto(
                    m.Id, m.CreatedAt, m.Type, m.WarehouseId, w.Code, m.ProductId, p.Sku, p.Name,
                    m.QuantityDelta, m.ReservedDelta, m.OnHandAfter, m.ReservedAfter, m.UnitCost,
                    m.ReferenceType, m.ReferenceId, m.ReferenceNumber, m.Reason, m.CreatedBy, null))
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || !currentUser.CanAccessWarehouse(row.WarehouseId))
        {
            throw new NotFoundException("Stock movement", id);
        }

        return (await WithUserNamesAsync([row], cancellationToken))[0];
    }

    public async Task<InventoryValuationDto> GetValuationAsync(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var items = db.InventoryItems.AsNoTracking().Where(i => i.QuantityOnHand > 0);
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var allowedIds = allowed.ToArray();
            items = items.Where(i => allowedIds.Contains(i.WarehouseId));
        }

        if (warehouseId is { } id)
        {
            items = items.Where(i => i.WarehouseId == id);
        }

        var byWarehouse = await items
            .GroupBy(i => new { i.WarehouseId, i.Warehouse!.Code, i.Warehouse.Name })
            .Select(g => new WarehouseValuationDto(
                g.Key.WarehouseId, g.Key.Code, g.Key.Name,
                g.Sum(i => i.QuantityOnHand),
                g.Sum(i => i.QuantityOnHand * i.AverageUnitCost),
                g.Sum(i => i.QuantityOnHand * i.Product!.SellingPrice)))
            .ToListAsync(cancellationToken);
        byWarehouse = byWarehouse.OrderByDescending(v => v.CostValue).ToList();

        var byCategory = await items
            .GroupBy(i => new { i.Product!.CategoryId, Name = i.Product.Category != null ? i.Product.Category.Name : null })
            .Select(g => new CategoryValuationDto(
                g.Key.CategoryId, g.Key.Name ?? "Uncategorized",
                g.Sum(i => i.QuantityOnHand),
                g.Sum(i => i.QuantityOnHand * i.AverageUnitCost),
                g.Sum(i => i.QuantityOnHand * i.Product!.SellingPrice)))
            .ToListAsync(cancellationToken);
        byCategory = byCategory.OrderByDescending(v => v.CostValue).ToList();

        return new InventoryValuationDto(
            byWarehouse.Sum(w => w.Units),
            decimal.Round(byWarehouse.Sum(w => w.CostValue), 2),
            decimal.Round(byWarehouse.Sum(w => w.RetailValue), 2),
            byWarehouse,
            byCategory);
    }

    private async Task<IReadOnlyList<StockMovementDto>> WithUserNamesAsync(
        IReadOnlyList<StockMovementDto> movements, CancellationToken cancellationToken)
    {
        var names = await users.GetDisplayNamesAsync(
            movements.Where(m => m.CreatedBy.HasValue).Select(m => m.CreatedBy!.Value).Distinct(), cancellationToken);
        return movements.Select(m => m with { CreatedByName = names.NameOf(m.CreatedBy) }).ToList();
    }

    private async Task<StockMovementDto> ExecuteAsync(
        Guid warehouseId,
        Guid productId,
        bool requireActiveProduct,
        Func<InventoryItem, Product, StockMovement> apply,
        CancellationToken cancellationToken)
    {
        currentUser.EnsureWarehouseAccess(warehouseId);
        var movementId = await transactions.ExecuteAsync(async token =>
        {
            var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, token)
                            ?? throw new NotFoundException("Warehouse", warehouseId);
            warehouse.EnsureActive();
            var product = await db.Products.Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == productId, token)
                          ?? throw new NotFoundException("Product", productId);
            if (requireActiveProduct && !product.IsActive)
            {
                throw new DomainException("product_inactive", $"Product '{product.Sku}' is inactive.");
            }

            var item = await ledger.LoadAsync(warehouseId, productId, token);
            var movement = apply(item, product);
            await db.SaveChangesAsync(token);
            return movement.Id;
        }, cancellationToken);

        return await GetMovementAsync(movementId, cancellationToken);
    }
}
