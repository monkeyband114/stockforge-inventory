using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Billing;
using StockForge.Application.Common;
using StockForge.Application.Inventory;
using StockForge.Application.Warehouses;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;
using StockForge.Domain.Procurement;

namespace StockForge.Application.Procurement;

public sealed class SupplierService(IAppDbContext db, ISequenceGenerator sequences, ITransactionRunner transactions)
{
    private static readonly PurchaseOrderStatus[] OpenStatuses =
        [PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived];

    private static readonly SupplierInvoiceStatus[] UnpaidStatuses =
        [SupplierInvoiceStatus.Open, SupplierInvoiceStatus.PartiallyPaid, SupplierInvoiceStatus.Overdue];

    public async Task<PagedResult<SupplierDto>> ListAsync(SupplierQuery query, CancellationToken cancellationToken)
    {
        var suppliers = db.Suppliers.AsNoTracking();
        if (query.IsActive is { } isActive)
        {
            suppliers = suppliers.Where(s => s.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            suppliers = suppliers.Where(s => s.Name.ToLower().Contains(term) || s.Code.ToLower().Contains(term) ||
                                             (s.Email != null && s.Email.ToLower().Contains(term)));
        }

        var page = await suppliers.OrderBy(s => s.Name)
            .Select(s => new
            {
                Supplier = s,
                Products = db.SupplierProducts.Count(p => p.SupplierId == s.Id),
                OpenOrders = db.PurchaseOrders.Count(o => o.SupplierId == s.Id && OpenStatuses.Contains(o.Status)),
                Payables = db.SupplierInvoices.Where(i => i.SupplierId == s.Id && UnpaidStatuses.Contains(i.Status))
                    .Sum(i => (decimal?)(i.Total - i.AmountPaid)) ?? 0,
            })
            .ToPagedResultAsync(query, cancellationToken);
        return new PagedResult<SupplierDto>(page.Items.Select(x => ToDto(x.Supplier, x.Products, x.OpenOrders, x.Payables)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                       ?? throw new NotFoundException("Supplier", id);
        return ToDto(supplier,
            await db.SupplierProducts.CountAsync(p => p.SupplierId == id, cancellationToken),
            await db.PurchaseOrders.CountAsync(o => o.SupplierId == id && OpenStatuses.Contains(o.Status), cancellationToken),
            await db.SupplierInvoices.Where(i => i.SupplierId == id && UnpaidStatuses.Contains(i.Status))
                .SumAsync(i => (decimal?)(i.Total - i.AmountPaid), cancellationToken) ?? 0);
    }

    public async Task<SupplierDto> CreateAsync(SupplierRequest request, CancellationToken cancellationToken)
    {
        var id = await transactions.ExecuteAsync(async token =>
        {
            var code = $"SUP-{await sequences.NextValueAsync(SequenceNames.Supplier, token):D5}";
            var supplier = new Supplier(code, request.Name, request.ContactName, request.Email, request.Phone, request.TaxNumber,
                request.Address?.ToDomain(), request.PaymentTermsDays, request.LeadTimeDays, request.Notes);
            supplier.SetActive(request.IsActive);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync(token);
            return supplier.Id;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, SupplierRequest request, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw new NotFoundException("Supplier", id);
        WarehouseService.EnsureVersion(supplier.Version, request.Version);
        supplier.Update(request.Name, request.ContactName, request.Email, request.Phone, request.TaxNumber, request.Address?.ToDomain(),
            request.PaymentTermsDays, request.LeadTimeDays, request.Notes);
        supplier.SetActive(request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierProductDto>> ListProductsAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, cancellationToken))
        {
            throw new NotFoundException("Supplier", supplierId);
        }

        return await (
                from sp in db.SupplierProducts.AsNoTracking().Where(sp => sp.SupplierId == supplierId)
                join p in db.Products on sp.ProductId equals p.Id
                orderby p.Name
                select new SupplierProductDto(sp.ProductId, p.Sku, p.Name, sp.SupplierSku, sp.UnitCost, sp.MinimumOrderQuantity, sp.LeadTimeDays, sp.IsPreferred))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Adds or updates the supplier's terms for a product; marking it preferred clears other preferred suppliers.</summary>
    public async Task<IReadOnlyList<SupplierProductDto>> UpsertProductAsync(Guid supplierId, SupplierProductRequest request, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(async token =>
        {
            if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, token))
            {
                throw new NotFoundException("Supplier", supplierId);
            }

            if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, token))
            {
                throw new NotFoundException("Product", request.ProductId);
            }

            if (request.IsPreferred)
            {
                // Clear the previous preferred supplier first: a unique index allows one preferred supplier per product.
                await db.SupplierProducts
                    .Where(p => p.ProductId == request.ProductId && p.SupplierId != supplierId && p.IsPreferred)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPreferred, false), token);
            }

            var existing = await db.SupplierProducts.FirstOrDefaultAsync(p => p.SupplierId == supplierId && p.ProductId == request.ProductId, token);
            if (existing is null)
            {
                db.SupplierProducts.Add(new SupplierProduct(supplierId, request.ProductId, request.SupplierSku, request.UnitCost,
                    request.MinimumOrderQuantity, request.LeadTimeDays, request.IsPreferred));
            }
            else
            {
                existing.Update(request.SupplierSku, request.UnitCost, request.MinimumOrderQuantity, request.LeadTimeDays, request.IsPreferred);
            }

            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await ListProductsAsync(supplierId, cancellationToken);
    }

    public async Task RemoveProductAsync(Guid supplierId, Guid productId, CancellationToken cancellationToken)
    {
        var existing = await db.SupplierProducts.FirstOrDefaultAsync(p => p.SupplierId == supplierId && p.ProductId == productId, cancellationToken)
                       ?? throw new NotFoundException("Supplier product", productId);
        db.SupplierProducts.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static SupplierDto ToDto(Supplier s, int products, int openOrders, decimal payables) => new(
        s.Id, s.Code, s.Name, s.ContactName, s.Email, s.Phone, s.TaxNumber, AddressDto.From(s.Address), s.PaymentTermsDays, s.LeadTimeDays,
        s.Notes, s.IsActive, products, openOrders, payables, s.CreatedAt, s.Version);
}

/// <summary>
/// Purchase orders and goods receipts. Receiving is one transaction: PO line progress, the GRN, and a Stock
/// In movement per line at the PO cost (which updates the weighted-average cost).
/// </summary>
public sealed class PurchaseOrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITransactionRunner transactions,
    InventoryLedger ledger,
    ISequenceGenerator sequences,
    IAuditTrail audit,
    IUserDirectory users,
    TimeProvider clock)
{
    private static readonly PurchaseOrderStatus[] OnOrderStatuses = [PurchaseOrderStatus.Approved, PurchaseOrderStatus.PartiallyReceived];

    public async Task<PagedResult<PurchaseOrderSummaryDto>> ListAsync(PurchaseOrderQuery query, CancellationToken cancellationToken)
    {
        var orders = Scoped(db.PurchaseOrders.AsNoTracking());
        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.SupplierId is { } supplierId)
        {
            orders = orders.Where(o => o.SupplierId == supplierId);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            orders = orders.Where(o => o.WarehouseId == warehouseId);
        }

        if (query.OutstandingOnly)
        {
            orders = orders.Where(o => OnOrderStatuses.Contains(o.Status));
        }

        var projected =
            from o in orders
            join s in db.Suppliers on o.SupplierId equals s.Id
            join w in db.Warehouses on o.WarehouseId equals w.Id
            select new { o, SupplierName = s.Name, w.Code };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            projected = projected.Where(x => x.o.Number.ToLower().Contains(term) || x.SupplierName.ToLower().Contains(term));
        }

        return await projected
            .OrderByDescending(x => x.o.CreatedAt).ThenByDescending(x => x.o.Id)
            .Select(x => new PurchaseOrderSummaryDto(x.o.Id, x.o.Number, x.o.SupplierId, x.SupplierName, x.o.WarehouseId, x.Code, x.o.Status,
                x.o.OrderDate, x.o.ExpectedDate, x.o.Currency, x.o.Total, x.o.Lines.Sum(l => l.Quantity), x.o.Lines.Sum(l => l.QuantityReceived),
                x.o.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await Scoped(db.PurchaseOrders.AsNoTracking().Include(o => o.Lines)).FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                    ?? throw new NotFoundException("Purchase order", id);
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == order.SupplierId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == order.WarehouseId, cancellationToken);
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines).Where(r => r.PurchaseOrderId == id)
            .OrderBy(r => r.ReceivedAt).ToListAsync(cancellationToken);
        var invoices = await SupplierInvoiceService.Project(db, db.SupplierInvoices.AsNoTracking().Where(i => i.PurchaseOrderId == id))
            .ToListAsync(cancellationToken);
        var skus = order.Lines.ToDictionary(l => l.ProductId, l => l.Sku);
        var names = await users.GetDisplayNamesAsync(
            receipts.Select(r => r.ReceivedBy).Append(order.CreatedByUser).Concat(order.ApprovedBy is { } a ? [a] : []), cancellationToken);

        return new PurchaseOrderDto(
            order.Id, order.Number, order.SupplierId, supplier.Name, order.WarehouseId, warehouse.Code, order.Status, order.OrderDate,
            order.ExpectedDate, order.Currency, order.Notes, order.Subtotal, order.TaxTotal, order.Total, order.ReceivedValue,
            order.RequiresApproval, names.NameOf(order.CreatedByUser), order.CreatedAt, order.SubmittedAt, names.NameOf(order.ApprovedBy),
            order.ApprovedAt, order.RejectedAt, order.RejectionReason, order.CancelledAt, order.ClosedAt,
            order.Lines.OrderBy(l => l.LineNumber).Select(l => new PurchaseOrderLineDto(l.Id, l.LineNumber, l.ProductId, l.Sku, l.ProductName,
                l.Quantity, l.UnitCost, l.TaxRate, l.Subtotal, l.TaxAmount, l.Total, l.QuantityReceived, l.QuantityOutstanding)).ToList(),
            receipts.Select(r => new GoodsReceiptDto(r.Id, r.Number, r.PurchaseOrderId, r.WarehouseId, r.DeliveryNote, r.Notes,
                names.NameOf(r.ReceivedBy), r.ReceivedAt,
                r.Lines.Select(l => new GoodsReceiptLineDto(l.PurchaseOrderLineId, l.ProductId, skus.GetValueOrDefault(l.ProductId, ""), l.Quantity, l.UnitCost)).ToList())).ToList(),
            invoices,
            order.Version);
    }

    public async Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (idempotencyKey is not null && await FindByKeyAsync(idempotencyKey, cancellationToken) is { } existingId)
        {
            return await GetAsync(existingId, cancellationToken);
        }

        Guid id;
        try
        {
            id = await transactions.ExecuteAsync(async token =>
            {
                var tenant = await LoadTenantAsync(token);
                await RequireSupplierAsync(request.SupplierId, token);
                await RequireWarehouseAsync(request.WarehouseId, token);
                var number = $"PO-{await sequences.NextValueAsync(SequenceNames.PurchaseOrder, token):D6}";
                var order = new PurchaseOrder(number, request.SupplierId, request.WarehouseId, Today(tenant.TimeZone), request.ExpectedDate,
                    tenant.BaseCurrency, request.Notes, currentUser.RequiredUserId, idempotencyKey);
                order.SetLines(await PriceLinesAsync(request.SupplierId, request.Lines, token));
                db.PurchaseOrders.Add(order);
                if (request.Submit)
                {
                    SubmitInternal(order, tenant.Settings.PurchaseApprovalThreshold);
                }

                await db.SaveChangesAsync(token);
                return order.Id;
            }, cancellationToken);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            var existing = await FindByKeyAsync(idempotencyKey, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            id = existing.Value;
        }

        return await GetAsync(id, cancellationToken);
    }

    public Task<PurchaseOrderDto> UpdateAsync(Guid id, UpdatePurchaseOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) =>
        {
            WarehouseService.EnsureVersion(order.Version, request.Version);
            await RequireSupplierAsync(request.SupplierId, token);
            await RequireWarehouseAsync(request.WarehouseId, token);
            order.SetDetails(request.SupplierId, request.WarehouseId, request.ExpectedDate, request.Notes);
            order.SetLines(await PriceLinesAsync(request.SupplierId, request.Lines, token));
        }, cancellationToken);

    public Task<PurchaseOrderDto> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) => SubmitInternal(order, (await LoadTenantAsync(token)).Settings.PurchaseApprovalThreshold),
            cancellationToken);

    public Task<PurchaseOrderDto> ApproveAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) =>
        {
            order.Approve(currentUser.RequiredUserId, automatic: false, Now);
            audit.Record("PurchaseOrderApproved", nameof(PurchaseOrder), order.Id.ToString(), new { order.Number, order.Total });
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<PurchaseOrderDto> RejectAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) =>
        {
            order.Reject(reason, Now);
            audit.Record("PurchaseOrderRejected", nameof(PurchaseOrder), order.Id.ToString(), new { order.Number, reason });
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<PurchaseOrderDto> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) =>
        {
            order.Cancel(Now);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<PurchaseOrderDto> CloseAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) =>
        {
            order.CloseShort(Now);
            audit.Record("PurchaseOrderClosedShort", nameof(PurchaseOrder), order.Id.ToString(), new { order.Number });
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Books a (partial) delivery: GRN + Stock In per line at the PO unit cost, atomically.</summary>
    public Task<PurchaseOrderDto> ReceiveAsync(Guid id, ReceiveGoodsRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) =>
        {
            currentUser.EnsureWarehouseAccess(order.WarehouseId);
            var warehouse = await db.Warehouses.FirstAsync(w => w.Id == order.WarehouseId, token);
            warehouse.EnsureActive();

            var lines = new List<(Guid OrderLineId, Guid ProductId, decimal Quantity, decimal UnitCost)>();
            foreach (var received in request.Lines)
            {
                order.Receive(received.LineId, received.Quantity);
                var line = order.Lines.First(l => l.Id == received.LineId);
                lines.Add((line.Id, line.ProductId, received.Quantity, line.UnitCost));
            }

            var number = $"GRN-{await sequences.NextValueAsync(SequenceNames.GoodsReceipt, token):D6}";
            var receipt = new GoodsReceipt(number, order.Id, order.WarehouseId, request.DeliveryNote, request.Notes, lines, currentUser.RequiredUserId, Now);
            db.GoodsReceipts.Add(receipt);

            var items = await ledger.LoadAsync(lines.Select(l => (order.WarehouseId, l.ProductId)).ToList(), token);
            var reference = new MovementReference(MovementReferenceType.GoodsReceipt, receipt.Id, number);
            foreach (var line in lines.OrderBy(l => l.ProductId))
            {
                ledger.Receive(items[(order.WarehouseId, line.ProductId)], line.Quantity, line.UnitCost, StockMovementType.StockIn, reference,
                    $"Received on {order.Number}");
            }

            audit.Record("GoodsReceived", nameof(GoodsReceipt), receipt.Id.ToString(), new { Order = order.Number, Receipt = number });
        }, cancellationToken);

    /// <summary>Low-stock items with the quantity already on order and the preferred supplier's terms.</summary>
    public async Task<IReadOnlyList<ReorderSuggestionDto>> GetReorderSuggestionsAsync(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var items = db.InventoryItems.AsNoTracking()
            .Where(i => i.Product!.IsActive && i.QuantityOnHand - i.QuantityReserved <= i.Product.ReorderLevel);
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var ids = allowed.ToArray();
            items = items.Where(i => ids.Contains(i.WarehouseId));
        }

        if (warehouseId is { } id)
        {
            items = items.Where(i => i.WarehouseId == id);
        }

        var rows = await items
            .Select(i => new
            {
                i.ProductId,
                i.Product!.Sku,
                i.Product.Name,
                i.WarehouseId,
                i.Warehouse!.Code,
                Available = i.QuantityOnHand - i.QuantityReserved,
                i.Product.ReorderLevel,
                i.Product.ReorderQuantity,
                OnOrder = db.PurchaseOrders
                    .Where(o => o.WarehouseId == i.WarehouseId && OnOrderStatuses.Contains(o.Status))
                    .SelectMany(o => o.Lines)
                    .Where(l => l.ProductId == i.ProductId)
                    .Sum(l => (decimal?)(l.Quantity - l.QuantityReceived)) ?? 0,
                Preferred = db.SupplierProducts.Where(sp => sp.ProductId == i.ProductId && sp.IsPreferred)
                    .Select(sp => new { sp.SupplierId, sp.UnitCost, sp.MinimumOrderQuantity })
                    .FirstOrDefault(),
            })
            .OrderBy(r => r.Available - r.ReorderLevel)
            .Take(200)
            .ToListAsync(cancellationToken);

        var supplierIds = rows.Where(r => r.Preferred != null).Select(r => r.Preferred!.SupplierId).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        return rows.Select(r =>
        {
            var target = r.ReorderLevel + Math.Max(r.ReorderQuantity, 1);
            var suggested = Math.Max(0, target - r.Available - r.OnOrder);
            if (r.Preferred is { MinimumOrderQuantity: > 0 } preferred && suggested > 0)
            {
                suggested = Math.Max(suggested, preferred.MinimumOrderQuantity);
            }

            return new ReorderSuggestionDto(r.ProductId, r.Sku, r.Name, r.WarehouseId, r.Code, r.Available, r.ReorderLevel, suggested, r.OnOrder,
                r.Preferred?.SupplierId, r.Preferred is null ? null : suppliers.GetValueOrDefault(r.Preferred.SupplierId), r.Preferred?.UnitCost);
        }).ToList();
    }

    private void SubmitInternal(PurchaseOrder order, decimal threshold)
    {
        if (!order.Submit(threshold, Now))
        {
            order.Approve(currentUser.RequiredUserId, automatic: true, Now);
        }
    }

    private async Task<PurchaseOrderDto> ExecuteAsync(Guid id, Func<PurchaseOrder, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(async token =>
        {
            var order = await Scoped(db.PurchaseOrders.Include(o => o.Lines)).FirstOrDefaultAsync(o => o.Id == id, token)
                        ?? throw new NotFoundException("Purchase order", id);
            await action(order, token);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<List<PurchaseOrderLineInput>> PriceLinesAsync(Guid supplierId, IReadOnlyList<PurchaseOrderLineRequest> lines, CancellationToken token)
    {
        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
        var terms = await db.SupplierProducts.AsNoTracking().Where(sp => sp.SupplierId == supplierId && ids.Contains(sp.ProductId))
            .ToDictionaryAsync(sp => sp.ProductId, token);
        return lines.Select(l =>
        {
            var product = products.GetValueOrDefault(l.ProductId) ?? throw new NotFoundException("Product", l.ProductId);
            var cost = l.UnitCost ?? (terms.TryGetValue(l.ProductId, out var t) ? t.UnitCost : product.CostPrice);
            return new PurchaseOrderLineInput(product.Id, product.Sku, product.Name, l.Quantity, cost, l.TaxRate ?? 0);
        }).ToList();
    }

    private async Task RequireSupplierAsync(Guid supplierId, CancellationToken token)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, token) ?? throw new NotFoundException("Supplier", supplierId);
        if (!supplier.IsActive)
        {
            throw new DomainException("supplier_inactive", $"Supplier '{supplier.Name}' is inactive.");
        }
    }

    private async Task RequireWarehouseAsync(Guid warehouseId, CancellationToken token)
    {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, token) ?? throw new NotFoundException("Warehouse", warehouseId);
        warehouse.EnsureActive();
    }

    private async Task<Guid?> FindByKeyAsync(string key, CancellationToken token) =>
        await db.PurchaseOrders.AsNoTracking().Where(o => o.IdempotencyKey == key).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(token);

    private sealed record TenantInfo(string BaseCurrency, string TimeZone, Domain.Tenancy.TenantSettings Settings);

    private async Task<TenantInfo> LoadTenantAsync(CancellationToken token)
    {
        var tenantId = tenantContext.RequiredTenantId;
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, token);
        return new TenantInfo(tenant.BaseCurrency, tenant.TimeZone, tenant.Settings);
    }

    /// <summary>Warehouse-scoped members (receiving staff) only see POs delivered to their warehouses.</summary>
    private IQueryable<PurchaseOrder> Scoped(IQueryable<PurchaseOrder> orders)
    {
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var ids = allowed.ToArray();
            return orders.Where(o => ids.Contains(o.WarehouseId));
        }

        return orders;
    }

    private DateTimeOffset Now => clock.GetUtcNow();

    private DateOnly Today(string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : TimeZoneInfo.Utc).DateTime);
}

/// <summary>Accounts payable: supplier invoices (optionally three-way matched to a PO) and payments.</summary>
public sealed class SupplierInvoiceService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITransactionRunner transactions,
    ISequenceGenerator sequences,
    IAuditTrail audit,
    IUserDirectory users,
    TimeProvider clock)
{
    public async Task<PagedResult<SupplierInvoiceSummaryDto>> ListAsync(SupplierInvoiceQuery query, CancellationToken cancellationToken)
    {
        var invoices = db.SupplierInvoices.AsNoTracking();
        if (query.Status is { } status)
        {
            invoices = invoices.Where(i => i.Status == status);
        }

        if (query.SupplierId is { } supplierId)
        {
            invoices = invoices.Where(i => i.SupplierId == supplierId);
        }

        if (query.OutstandingOnly)
        {
            invoices = invoices.Where(i => i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid ||
                                           i.Status == SupplierInvoiceStatus.Overdue);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            invoices = invoices.Where(i => i.Reference.ToLower().Contains(term) || i.SupplierInvoiceNumber.ToLower().Contains(term));
        }

        return await Project(db, invoices.OrderBy(i => i.DueDate).ThenBy(i => i.Reference)).ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<SupplierInvoiceDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.AsNoTracking().Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Supplier invoice", id);
        var summary = await Project(db, db.SupplierInvoices.AsNoTracking().Where(i => i.Id == id)).FirstAsync(cancellationToken);
        var names = await users.GetDisplayNamesAsync(invoice.Payments.Select(p => p.RecordedBy), cancellationToken);
        return new SupplierInvoiceDto(summary, invoice.Subtotal, invoice.TaxTotal, invoice.Notes,
            invoice.Payments.OrderBy(p => p.PaidAt).Select(p => new SupplierPaymentDto(p.Id, p.Amount, p.Method, p.Reference, p.PaidAt, names.NameOf(p.RecordedBy))).ToList(),
            invoice.Version);
    }

    public async Task<SupplierInvoiceDto> CreateAsync(CreateSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        var id = await transactions.ExecuteAsync(async token =>
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, token)
                           ?? throw new NotFoundException("Supplier", request.SupplierId);
            if (await db.SupplierInvoices.AnyAsync(i => i.SupplierId == supplier.Id && i.SupplierInvoiceNumber == request.SupplierInvoiceNumber.Trim(), token))
            {
                throw new ConflictException("duplicate_supplier_invoice", "This supplier invoice number has already been recorded.");
            }

            var matches = true;
            if (request.PurchaseOrderId is { } poId)
            {
                var order = await db.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == poId, token)
                            ?? throw new NotFoundException("Purchase order", poId);
                if (order.SupplierId != supplier.Id)
                {
                    throw new DomainValidationException("The purchase order belongs to a different supplier.");
                }

                var alreadyInvoiced = await db.SupplierInvoices
                    .Where(i => i.PurchaseOrderId == poId && i.Status != SupplierInvoiceStatus.Cancelled)
                    .SumAsync(i => (decimal?)i.Total, token) ?? 0;
                matches = alreadyInvoiced + request.Subtotal + request.TaxTotal <= order.ReceivedValue + 0.01m;
            }

            var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantContext.RequiredTenantId, token);
            var reference = $"SINV-{await sequences.NextValueAsync(SequenceNames.SupplierInvoice, token):D6}";
            var invoice = new SupplierInvoice(reference, supplier.Id, request.PurchaseOrderId, request.SupplierInvoiceNumber.Trim(), request.InvoiceDate,
                request.DueDate ?? request.InvoiceDate.AddDays(supplier.PaymentTermsDays ?? 30), tenant.BaseCurrency, request.Subtotal,
                request.TaxTotal, matches, request.Notes);
            invoice.Refresh(Today(tenant.TimeZone));
            db.SupplierInvoices.Add(invoice);
            audit.Record("SupplierInvoiceRecorded", nameof(SupplierInvoice), invoice.Id.ToString(),
                new { Reference = reference, invoice.SupplierInvoiceNumber, invoice.Total, ThreeWayMatch = matches });
            await db.SaveChangesAsync(token);
            return invoice.Id;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SupplierInvoiceDto> RecordPaymentAsync(Guid id, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(async token =>
        {
            var invoice = await db.SupplierInvoices.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id, token)
                          ?? throw new NotFoundException("Supplier invoice", id);
            var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantContext.RequiredTenantId, token);
            var payment = invoice.RecordPayment(request.Amount, request.Method, request.Reference, request.PaidAt ?? clock.GetUtcNow(),
                currentUser.RequiredUserId, Today(tenant.TimeZone));
            db.SupplierPayments.Add(payment);
            audit.Record("SupplierPaymentRecorded", nameof(SupplierInvoice), invoice.Id.ToString(), new { invoice.Reference, payment.Amount });
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SupplierInvoiceDto> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new NotFoundException("Supplier invoice", id);
        invoice.Cancel();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<int> MarkOverdueAsync(CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantContext.RequiredTenantId, cancellationToken);
        var today = Today(tenant.TimeZone);
        var due = await db.SupplierInvoices
            .Where(i => (i.Status == SupplierInvoiceStatus.Open || i.Status == SupplierInvoiceStatus.PartiallyPaid) && i.DueDate < today)
            .ToListAsync(cancellationToken);
        foreach (var invoice in due)
        {
            invoice.Refresh(today);
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    internal static IQueryable<SupplierInvoiceSummaryDto> Project(IAppDbContext db, IQueryable<SupplierInvoice> invoices) =>
        from i in invoices
        join s in db.Suppliers on i.SupplierId equals s.Id
        join o in db.PurchaseOrders on i.PurchaseOrderId equals o.Id into orders
        from o in orders.DefaultIfEmpty()
        select new SupplierInvoiceSummaryDto(i.Id, i.Reference, i.SupplierInvoiceNumber, i.SupplierId, s.Name, i.PurchaseOrderId,
            o != null ? o.Number : null, i.Status, i.InvoiceDate, i.DueDate, i.Currency, i.Total, i.AmountPaid, i.Total - i.AmountPaid,
            i.MatchesReceipts);

    private DateOnly Today(string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
            TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : TimeZoneInfo.Utc).DateTime);
}
