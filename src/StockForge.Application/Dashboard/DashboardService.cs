using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Inventory;
using StockForge.Domain.Billing;
using StockForge.Domain.Inventory;
using StockForge.Domain.Sales;

namespace StockForge.Application.Dashboard;

public sealed record TrendPoint(DateOnly Date, decimal Inbound, decimal Outbound);

public sealed record LowStockItemDto(
    Guid ProductId,
    string Sku,
    string Name,
    Guid WarehouseId,
    string WarehouseCode,
    decimal QuantityAvailable,
    decimal ReorderLevel,
    decimal ReorderQuantity);

public sealed record OrganizationDashboardDto(
    int TotalProducts,
    int ActiveProducts,
    int TotalWarehouses,
    decimal InventoryCostValue,
    decimal InventoryRetailValue,
    decimal UnitsOnHand,
    decimal UnitsReserved,
    int LowStockCount,
    int OutOfStockCount,
    int PendingAdjustments,
    int OpenTransfers,
    IReadOnlyList<TrendPoint> MovementTrend,
    IReadOnlyList<CategoryValuationDto> ValueByCategory,
    IReadOnlyList<WarehouseValuationDto> ValueByWarehouse,
    IReadOnlyList<LowStockItemDto> LowStockItems,
    OrderMetricsDto Orders);

/// <summary>Order and fulfillment KPIs (window: the dashboard's trend days).</summary>
public sealed record OrderMetricsDto(
    int TotalOrders,
    int PendingApproval,
    int OpenOrders,
    int ShippedInPeriod,
    decimal SalesInPeriod,
    decimal FulfillmentRatePercent,
    decimal? AverageHoursToShip,
    IReadOnlyList<MonthlySalesPoint> MonthlySales,
    IReadOnlyList<StatusCount> ByStatus);

public sealed record MonthlySalesPoint(int Year, int Month, decimal Sales, int Orders);

public sealed record StatusCount(string Status, int Count);

public sealed record WarehouseDashboardDto(
    Guid WarehouseId,
    string Code,
    string Name,
    int SkuCount,
    decimal UnitsOnHand,
    decimal UnitsReserved,
    decimal UnitsAvailable,
    decimal StockValue,
    int LowStockCount,
    int IncomingTransfers,
    decimal IncomingUnits,
    int OutgoingTransfers,
    decimal OutgoingUnits,
    int PendingAdjustments,
    IReadOnlyList<TrendPoint> MovementTrend,
    IReadOnlyList<StockMovementDto> RecentMovements,
    IReadOnlyList<LowStockItemDto> LowStockItems,
    IReadOnlyList<StatusCount> FulfillmentQueue);

/// <summary>Raw SQL row for movement trends (explicit column names, independent of naming conventions).</summary>
public sealed class TrendRow
{
    [Column("day")]
    public DateOnly Day { get; set; }

    [Column("inbound")]
    public decimal Inbound { get; set; }

    [Column("outbound")]
    public decimal Outbound { get; set; }
}

/// <summary>Role-specific dashboards computed from live data (no cached or hard-coded figures).</summary>
public sealed class DashboardService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    InventoryService inventory,
    TimeProvider clock)
{
    public async Task<OrganizationDashboardDto> GetOrganizationAsync(int trendDays, CancellationToken cancellationToken)
    {
        trendDays = Math.Clamp(trendDays, 7, 180);
        var items = ScopedItems();

        var totals = await items
            .GroupBy(_ => 1)
            .Select(g => new
            {
                OnHand = g.Sum(i => i.QuantityOnHand),
                Reserved = g.Sum(i => i.QuantityReserved),
                LowStock = g.Count(i => i.Product!.IsActive && i.QuantityOnHand - i.QuantityReserved <= i.Product.ReorderLevel && i.QuantityOnHand - i.QuantityReserved > 0),
                OutOfStock = g.Count(i => i.Product!.IsActive && i.QuantityOnHand - i.QuantityReserved <= 0),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var valuation = await inventory.GetValuationAsync(null, cancellationToken);
        var tz = await TenantTimeZoneAsync(cancellationToken);

        return new OrganizationDashboardDto(
            await db.Products.CountAsync(cancellationToken),
            await db.Products.CountAsync(p => p.IsActive, cancellationToken),
            await db.Warehouses.CountAsync(w => w.IsActive, cancellationToken),
            valuation.TotalCostValue,
            valuation.TotalRetailValue,
            totals?.OnHand ?? 0,
            totals?.Reserved ?? 0,
            totals?.LowStock ?? 0,
            totals?.OutOfStock ?? 0,
            await ScopedAdjustments().CountAsync(a => a.Status == AdjustmentStatus.PendingApproval, cancellationToken),
            await ScopedTransfers().CountAsync(t => t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit, cancellationToken),
            await TrendAsync(null, trendDays, tz, excludeTransfers: true, cancellationToken),
            valuation.Categories,
            valuation.Warehouses,
            await LowStockAsync(null, 10, cancellationToken),
            await OrderMetricsAsync(trendDays, cancellationToken));
    }

    private static readonly SalesOrderStatus[] OpenStatuses =
    [
        SalesOrderStatus.Approved, SalesOrderStatus.Processing, SalesOrderStatus.Picking, SalesOrderStatus.Packed,
        SalesOrderStatus.PartiallyFulfilled,
    ];

    private async Task<OrderMetricsDto> OrderMetricsAsync(int days, CancellationToken cancellationToken)
    {
        var orders = db.SalesOrders.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var ids = allowed.ToArray();
            orders = orders.Where(o => ids.Contains(o.WarehouseId));
        }

        var since = clock.GetUtcNow().AddDays(-days);
        var approvedInPeriod = orders.Where(o => o.ApprovedAt >= since);
        var approvedCount = await approvedInPeriod.CountAsync(cancellationToken);
        var fullyShipped = await approvedInPeriod.CountAsync(o => o.Status == SalesOrderStatus.Shipped ||
            o.Status == SalesOrderStatus.Delivered || o.Status == SalesOrderStatus.Returned, cancellationToken);
        var shipTimes = await orders.Where(o => o.ShippedAt >= since && o.ApprovedAt != null)
            .Select(o => new { o.ApprovedAt, o.ShippedAt })
            .ToListAsync(cancellationToken);

        var firstMonth = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddMonths(-5);
        firstMonth = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        var sales = await db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Cancelled && i.IssueDate >= firstMonth)
            .GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Sales = g.Sum(i => i.Total), Orders = g.Count() })
            .ToListAsync(cancellationToken);
        var monthly = Enumerable.Range(0, 6).Select(offset => firstMonth.AddMonths(offset)).Select(m =>
        {
            var point = sales.FirstOrDefault(s => s.Year == m.Year && s.Month == m.Month);
            return new MonthlySalesPoint(m.Year, m.Month, point?.Sales ?? 0, point?.Orders ?? 0);
        }).ToList();

        var byStatus = await orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var periodStart = DateOnly.FromDateTime(since.UtcDateTime);

        return new OrderMetricsDto(
            byStatus.Sum(s => s.Count),
            byStatus.Where(s => s.Key == SalesOrderStatus.PendingApproval).Sum(s => s.Count),
            byStatus.Where(s => OpenStatuses.Contains(s.Key)).Sum(s => s.Count),
            shipTimes.Count,
            await db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled && i.IssueDate >= periodStart)
                .SumAsync(i => (decimal?)i.Total, cancellationToken) ?? 0,
            approvedCount == 0 ? 0 : decimal.Round(100m * fullyShipped / approvedCount, 1),
            shipTimes.Count == 0 ? null : decimal.Round((decimal)shipTimes.Average(t => (t.ShippedAt!.Value - t.ApprovedAt!.Value).TotalHours), 1),
            monthly,
            byStatus.OrderBy(s => s.Key).Select(s => new StatusCount(s.Key.ToString(), s.Count)).ToList());
    }

    public async Task<WarehouseDashboardDto> GetWarehouseAsync(Guid warehouseId, int trendDays, CancellationToken cancellationToken)
    {
        currentUser.EnsureWarehouseAccess(warehouseId);
        trendDays = Math.Clamp(trendDays, 7, 180);
        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken)
                        ?? throw new NotFoundException("Warehouse", warehouseId);

        var items = db.InventoryItems.AsNoTracking().Where(i => i.WarehouseId == warehouseId);
        var totals = await items
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Skus = g.Count(i => i.QuantityOnHand > 0),
                OnHand = g.Sum(i => i.QuantityOnHand),
                Reserved = g.Sum(i => i.QuantityReserved),
                Value = g.Sum(i => i.QuantityOnHand * i.AverageUnitCost),
                LowStock = g.Count(i => i.Product!.IsActive && i.QuantityOnHand - i.QuantityReserved <= i.Product.ReorderLevel),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var incoming = await db.StockTransfers.AsNoTracking()
            .Where(t => t.DestinationWarehouseId == warehouseId && t.Status == TransferStatus.InTransit)
            .Select(t => t.Lines.Sum(l => l.Quantity))
            .ToListAsync(cancellationToken);
        var outgoing = await db.StockTransfers.AsNoTracking()
            .Where(t => t.SourceWarehouseId == warehouseId && t.Status == TransferStatus.Requested)
            .Select(t => t.Lines.Sum(l => l.Quantity))
            .ToListAsync(cancellationToken);

        var recent = await inventory.GetMovementsAsync(new MovementQuery { WarehouseId = warehouseId, PageSize = 10 }, cancellationToken);
        var tz = await TenantTimeZoneAsync(cancellationToken);

        return new WarehouseDashboardDto(
            warehouse.Id,
            warehouse.Code,
            warehouse.Name,
            totals?.Skus ?? 0,
            totals?.OnHand ?? 0,
            totals?.Reserved ?? 0,
            (totals?.OnHand ?? 0) - (totals?.Reserved ?? 0),
            decimal.Round(totals?.Value ?? 0, 2),
            totals?.LowStock ?? 0,
            incoming.Count,
            incoming.Sum(),
            outgoing.Count,
            outgoing.Sum(),
            await db.StockAdjustments.CountAsync(a => a.WarehouseId == warehouseId && a.Status == AdjustmentStatus.PendingApproval, cancellationToken),
            await TrendAsync(warehouseId, trendDays, tz, excludeTransfers: false, cancellationToken),
            recent.Items,
            await LowStockAsync(warehouseId, 10, cancellationToken),
            await db.SalesOrders.AsNoTracking()
                .Where(o => o.WarehouseId == warehouseId && OpenStatuses.Contains(o.Status))
                .GroupBy(o => o.Status)
                .Select(g => new StatusCount(g.Key.ToString(), g.Count()))
                .ToListAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<LowStockItemDto>> LowStockAsync(Guid? warehouseId, int take, CancellationToken cancellationToken)
    {
        var items = ScopedItems().Where(i => i.Product!.IsActive && i.QuantityOnHand - i.QuantityReserved <= i.Product.ReorderLevel);
        if (warehouseId is { } id)
        {
            items = items.Where(i => i.WarehouseId == id);
        }

        return await items
            .OrderBy(i => i.QuantityOnHand - i.QuantityReserved - i.Product!.ReorderLevel)
            .Take(take)
            .Select(i => new LowStockItemDto(i.ProductId, i.Product!.Sku, i.Product.Name, i.WarehouseId, i.Warehouse!.Code,
                i.QuantityOnHand - i.QuantityReserved, i.Product.ReorderLevel, i.Product.ReorderQuantity))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Daily inbound/outbound units bucketed in the organization's time zone.</summary>
    private async Task<IReadOnlyList<TrendPoint>> TrendAsync(
        Guid? warehouseId, int days, string timeZone, bool excludeTransfers, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequiredTenantId;
        var from = clock.GetUtcNow().AddDays(-days);
        var allowed = currentUser.AccessibleWarehouseIds?.ToArray();
        var restrict = allowed is not null;
        var allowedIds = allowed ?? [];
        var filterWarehouse = warehouseId.HasValue;
        var warehouse = warehouseId ?? Guid.Empty;

        var rows = await db.SqlQuery<TrendRow>($"""
            SELECT (m.created_at AT TIME ZONE {timeZone})::date AS day,
                   COALESCE(SUM(m.quantity_delta) FILTER (WHERE m.quantity_delta > 0), 0) AS inbound,
                   COALESCE(-SUM(m.quantity_delta) FILTER (WHERE m.quantity_delta < 0), 0) AS outbound
            FROM stock_movements m
            WHERE m.tenant_id = {tenantId}
              AND m.created_at >= {from}
              AND (NOT {filterWarehouse} OR m.warehouse_id = {warehouse})
              AND (NOT {restrict} OR m.warehouse_id = ANY({allowedIds}))
              AND (NOT {excludeTransfers} OR m.type NOT IN ('TransferIn', 'TransferOut'))
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(cancellationToken);

        var byDay = rows.ToDictionary(r => r.Day);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), FindZone(timeZone)).DateTime);
        return Enumerable.Range(0, days + 1)
            .Select(offset => today.AddDays(offset - days))
            .Select(day => byDay.TryGetValue(day, out var row)
                ? new TrendPoint(day, row.Inbound, row.Outbound)
                : new TrendPoint(day, 0, 0))
            .ToList();
    }

    private static TimeZoneInfo FindZone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    private async Task<string> TenantTimeZoneAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequiredTenantId;
        return await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(cancellationToken);
    }

    private IQueryable<InventoryItem> ScopedItems()
    {
        var items = db.InventoryItems.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var ids = allowed.ToArray();
            items = items.Where(i => ids.Contains(i.WarehouseId));
        }

        return items;
    }

    private IQueryable<StockAdjustment> ScopedAdjustments()
    {
        var adjustments = db.StockAdjustments.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var ids = allowed.ToArray();
            adjustments = adjustments.Where(a => ids.Contains(a.WarehouseId));
        }

        return adjustments;
    }

    private IQueryable<StockTransfer> ScopedTransfers()
    {
        var transfers = db.StockTransfers.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var ids = allowed.ToArray();
            transfers = transfers.Where(t => ids.Contains(t.SourceWarehouseId) || ids.Contains(t.DestinationWarehouseId));
        }

        return transfers;
    }
}
