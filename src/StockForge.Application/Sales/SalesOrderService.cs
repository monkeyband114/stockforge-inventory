using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Inventory;
using StockForge.Application.Warehouses;
using StockForge.Domain.Billing;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;
using StockForge.Domain.Sales;
using StockForge.Domain.Tenancy;

namespace StockForge.Application.Sales;

/// <summary>
/// Order lifecycle. Every state change that touches stock or money runs as one transaction through
/// <see cref="ITransactionRunner"/>: e.g. approval validates the order, reserves stock for every line (writing
/// ledger rows), moves the order to Approved, issues the invoice with the next invoice number and enqueues the
/// PDF/email jobs — all or nothing. Optimistic concurrency (xmin) plus state guards make concurrent or
/// repeated requests safe; unique constraints (one invoice per order, one order per idempotency key) are the
/// last line of defence against duplicates.
/// </summary>
public sealed class SalesOrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITransactionRunner transactions,
    InventoryLedger ledger,
    ISequenceGenerator sequences,
    IJobQueue jobs,
    IAuditTrail audit,
    IUserDirectory users,
    TimeProvider clock)
{
    private static readonly SalesOrderStatus[] QueueStatuses =
    [
        SalesOrderStatus.Approved, SalesOrderStatus.Processing, SalesOrderStatus.Picking, SalesOrderStatus.Packed,
        SalesOrderStatus.PartiallyFulfilled,
    ];

    private static readonly InvoiceStatus[] OpenInvoiceStatuses = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    // ===================== Queries =====================

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(OrderQuery query, CancellationToken cancellationToken)
    {
        var orders = Scoped(db.SalesOrders.AsNoTracking());
        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.CustomerId is { } customerId)
        {
            orders = orders.Where(o => o.CustomerId == customerId);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            orders = orders.Where(o => o.WarehouseId == warehouseId);
        }

        if (query.From is { } from)
        {
            orders = orders.Where(o => o.OrderDate >= from);
        }

        if (query.To is { } to)
        {
            orders = orders.Where(o => o.OrderDate <= to);
        }

        var projected =
            from o in orders
            join c in db.Customers on o.CustomerId equals c.Id
            join w in db.Warehouses on o.WarehouseId equals w.Id
            select new { o, CustomerName = c.Name, w.Code };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            projected = projected.Where(x => x.o.Number.ToLower().Contains(term) || x.CustomerName.ToLower().Contains(term) ||
                                             (x.o.CustomerReference != null && x.o.CustomerReference.ToLower().Contains(term)));
        }

        return await projected
            .OrderByDescending(x => x.o.CreatedAt).ThenByDescending(x => x.o.Id)
            .Select(x => new OrderSummaryDto(x.o.Id, x.o.Number, x.o.CustomerId, x.CustomerName, x.o.WarehouseId, x.Code, x.o.Status,
                x.o.OrderDate, x.o.RequiredDate, x.o.Currency, x.o.Total, x.o.TotalQuantity, x.o.RequiresApproval, x.o.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await Scoped(db.SalesOrders.AsNoTracking().Include(o => o.Lines)).FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                    ?? throw new NotFoundException("Sales order", id);
        var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == order.CustomerId, cancellationToken);
        var warehouse = await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == order.WarehouseId, cancellationToken);
        var productIds = order.Lines.Select(l => l.ProductId).ToList();
        var available = await db.InventoryItems.AsNoTracking()
            .Where(i => i.WarehouseId == order.WarehouseId && productIds.Contains(i.ProductId))
            .ToDictionaryAsync(i => i.ProductId, i => i.QuantityOnHand - i.QuantityReserved, cancellationToken);
        var invoice = await db.Invoices.AsNoTracking().Where(i => i.SalesOrderId == id)
            .Select(i => new OrderInvoiceDto(i.Id, i.Number, i.Status, i.Total, i.Total - i.AmountPaid - i.CreditedAmount, i.PdfFileId != null))
            .FirstOrDefaultAsync(cancellationToken);
        var shipments = await db.Shipments.AsNoTracking().Include(s => s.Lines).Where(s => s.SalesOrderId == id)
            .OrderBy(s => s.ShippedAt).ToListAsync(cancellationToken);
        var returns = await db.SalesReturns.AsNoTracking().Include(r => r.Lines).Where(r => r.SalesOrderId == id)
            .OrderBy(r => r.ReceivedAt).ToListAsync(cancellationToken);
        var skus = order.Lines.ToDictionary(l => l.ProductId, l => l.Sku);
        var names = await users.GetDisplayNamesAsync(
            new[] { order.CreatedByUser, order.ApprovedBy ?? Guid.Empty }.Where(g => g != Guid.Empty), cancellationToken);

        return new OrderDto(
            order.Id, order.Number, order.CustomerId, customer.Name, customer.Email, order.WarehouseId, warehouse.Code, warehouse.Name,
            order.Status, order.OrderDate, order.RequiredDate, order.CustomerReference, order.Notes, order.Currency,
            order.OrderDiscountPercent, AddressDto.From(order.ShippingAddress), order.Subtotal, order.DiscountTotal, order.TaxTotal,
            order.Total, order.TotalQuantity, order.RequiresApproval, order.ApprovalReason, names.NameOf(order.CreatedByUser),
            order.CreatedAt, order.SubmittedAt, names.NameOf(order.ApprovedBy), order.ApprovedAt, order.RejectedAt,
            order.RejectionReason, order.CancelledAt, order.CancellationReason, order.ShippedAt, order.DeliveredAt,
            order.Lines.OrderBy(l => l.LineNumber).Select(l => new OrderLineDto(
                l.Id, l.LineNumber, l.ProductId, l.Sku, l.ProductName, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRate,
                l.Subtotal, l.DiscountAmount, l.TaxAmount, l.Total, l.QuantityPicked, l.QuantityPacked, l.QuantityShipped,
                l.QuantityReturned, available.TryGetValue(l.ProductId, out var a) ? a : 0)).ToList(),
            invoice,
            shipments.Select(s => ToDto(s, order.Number, skus)).ToList(),
            returns.Select(r => new SalesReturnDto(r.Id, r.Number, r.SalesOrderId, r.Reason, r.CreditAmount, r.ReceivedAt,
                r.Lines.Select(l => new SalesReturnLineDto(l.SalesOrderLineId, l.ProductId, skus.GetValueOrDefault(l.ProductId, ""),
                    l.Quantity, l.Restock, l.CreditAmount)).ToList())).ToList(),
            order.Version,
            order.CreatedByUser);
    }

    /// <summary>Picking/packing queue: approved orders not yet fully shipped, oldest commitment first.</summary>
    public async Task<IReadOnlyList<FulfillmentQueueItemDto>> GetQueueAsync(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var orders = Scoped(db.SalesOrders.AsNoTracking()).Where(o => QueueStatuses.Contains(o.Status));
        if (warehouseId is { } id)
        {
            currentUser.EnsureWarehouseAccess(id);
            orders = orders.Where(o => o.WarehouseId == id);
        }

        return await (
                from o in orders
                join c in db.Customers on o.CustomerId equals c.Id
                join w in db.Warehouses on o.WarehouseId equals w.Id
                orderby o.RequiredDate ?? DateOnly.MaxValue, o.ApprovedAt
                select new FulfillmentQueueItemDto(o.Id, o.Number, c.Name, o.WarehouseId, w.Code, o.Status, o.RequiredDate, o.ApprovedAt,
                    o.TotalQuantity, o.Lines.Sum(l => l.QuantityPicked), o.Lines.Sum(l => l.QuantityPacked), o.Lines.Sum(l => l.QuantityShipped)))
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<ShipmentDto> GetShipmentAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null || !currentUser.CanAccessWarehouse(shipment.WarehouseId))
        {
            throw new NotFoundException("Shipment", shipmentId);
        }

        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.Id == shipment.SalesOrderId, cancellationToken);
        return ToDto(shipment, order.Number, order.Lines.ToDictionary(l => l.ProductId, l => l.Sku));
    }

    // ===================== Draft & approval =====================

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
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
                var customer = await RequireCustomerAsync(request.CustomerId, token);
                await RequireWarehouseAsync(request.WarehouseId, token);
                var number = $"SO-{await sequences.NextValueAsync(SequenceNames.SalesOrder, token):D6}";
                var shipTo = request.ShippingAddress?.ToDomain() ?? (customer.ShippingAddress ?? customer.BillingAddress)?.Copy();
                var order = new SalesOrder(number, customer.Id, request.WarehouseId, Today(tenant.TimeZone), request.RequiredDate,
                    request.CustomerReference, request.Notes, tenant.BaseCurrency, request.DiscountPercent, shipTo, userId, idempotencyKey);
                order.SetLines(await PriceLinesAsync(request.Lines, tenant.Settings, token));
                db.SalesOrders.Add(order);
                if (request.Submit)
                {
                    await SubmitInternalAsync(order, customer, tenant, userId, token);
                }

                await db.SaveChangesAsync(token);
                return order.Id;
            }, cancellationToken);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // Two concurrent creates with the same key: the unique index let exactly one through.
            var existing = await FindByKeyAsync(idempotencyKey, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            id = existing.Value;
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<OrderDto> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(async token =>
        {
            var order = await LoadAsync(id, token);
            WarehouseService.EnsureVersion(order.Version, request.Version);
            var tenant = await LoadTenantAsync(token);
            var customer = await RequireCustomerAsync(request.CustomerId, token);
            await RequireWarehouseAsync(request.WarehouseId, token);
            // Same default as create: the customer's shipping (or billing) address when none is given.
            var shipTo = request.ShippingAddress?.ToDomain() ?? (customer.ShippingAddress ?? customer.BillingAddress)?.Copy();
            order.SetDetails(request.CustomerId, request.WarehouseId, request.RequiredDate, request.CustomerReference, request.Notes,
                request.DiscountPercent, shipTo);
            order.SetLines(await PriceLinesAsync(request.Lines, tenant.Settings, token));
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Submits a draft. Orders under the organization's thresholds are approved (and processed) immediately.</summary>
    public Task<OrderDto> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) =>
        {
            var tenant = await LoadTenantAsync(token);
            var customer = await RequireCustomerAsync(order.CustomerId, token);
            await SubmitInternalAsync(order, customer, tenant, currentUser.RequiredUserId, token);
        }, cancellationToken);

    public Task<OrderDto> ApproveAsync(Guid id, ApproveOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) =>
        {
            var tenant = await LoadTenantAsync(token);
            var customer = await RequireCustomerAsync(order.CustomerId, token, requireActive: false);
            await ApproveInternalAsync(order, customer, tenant, currentUser.RequiredUserId, automatic: false, request.Notes, token);
        }, cancellationToken);

    public Task<OrderDto> RejectAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) =>
        {
            order.Reject(currentUser.RequiredUserId, request.Reason, Now);
            audit.Record("OrderRejected", nameof(SalesOrder), order.Id.ToString(), new { order.Number, request.Reason });
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Cancels an order that has not shipped: releases its reservations and cancels its (unpaid) invoice.</summary>
    public Task<OrderDto> CancelAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) =>
        {
            var heldStock = order.HoldsReservation;
            order.Cancel(currentUser.RequiredUserId, request.Reason, Now);
            if (heldStock)
            {
                await ReleaseReservationsAsync(order, "Order cancelled", token);
            }

            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.SalesOrderId == order.Id, token);
            invoice?.Cancel(Now);
            audit.Record("OrderCancelled", nameof(SalesOrder), order.Id.ToString(), new { order.Number, request.Reason });
        }, cancellationToken);

    // ===================== Fulfillment =====================

    public Task<OrderDto> StartProcessingAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) => Sync(order.StartProcessing), cancellationToken, requireWarehouseAccess: true);

    public Task<OrderDto> StartPickingAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) => Sync(order.StartPicking), cancellationToken, requireWarehouseAccess: true);

    public Task<OrderDto> RecordPickAsync(Guid id, RecordPickRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) =>
        {
            foreach (var line in request.Lines)
            {
                order.RecordPick(line.LineId, line.Quantity);
            }

            return Task.CompletedTask;
        }, cancellationToken, requireWarehouseAccess: true);

    public Task<OrderDto> PackAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, (order, _) => Sync(order.Pack), cancellationToken, requireWarehouseAccess: true);

    /// <summary>
    /// Ships everything packed: consumes the reservation (Stock Out against reserved stock) and creates the
    /// shipment, atomically. Remaining quantities stay reserved and the order becomes PartiallyFulfilled.
    /// </summary>
    public Task<OrderDto> ShipAsync(Guid id, ShipOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) =>
        {
            var shipped = order.Ship(Now);
            var reservations = await ActiveReservationsAsync(order.Id, token);
            var items = await ledger.LoadAsync(shipped.Select(s => (order.WarehouseId, s.Line.ProductId)).ToList(), token);
            var reference = new MovementReference(MovementReferenceType.SalesOrder, order.Id, order.Number);
            foreach (var (line, quantity) in shipped.OrderBy(s => s.Line.ProductId))
            {
                ledger.ConsumeReserved(items[(order.WarehouseId, line.ProductId)], quantity, StockMovementType.StockOut, reference,
                    $"Shipped on order {order.Number}");
                reservations[line.ProductId].Consume(quantity, Now);
            }

            var number = $"SHP-{await sequences.NextValueAsync(SequenceNames.Shipment, token):D6}";
            var shipment = new Shipment(number, order.Id, order.WarehouseId, request.Carrier, request.TrackingNumber, request.PackageCount,
                shipped.Select(s => (s.Line.Id, s.Line.ProductId, s.Quantity)).ToList(), currentUser.RequiredUserId, Now);
            db.Shipments.Add(shipment);
            jobs.Enqueue(new ShipmentNotificationJob(shipment.Id));
        }, cancellationToken, requireWarehouseAccess: true);

    public async Task<OrderDto> DeliverShipmentAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var orderId = await transactions.ExecuteAsync(async token =>
        {
            var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId, token)
                           ?? throw new NotFoundException("Shipment", shipmentId);
            currentUser.EnsureWarehouseAccess(shipment.WarehouseId);
            shipment.MarkDelivered(Now);
            var order = await LoadAsync(shipment.SalesOrderId, token);
            var othersPending = await db.Shipments.AnyAsync(
                s => s.SalesOrderId == order.Id && s.Id != shipment.Id && s.Status != ShipmentStatus.Delivered, token);
            if (order.Status == SalesOrderStatus.Shipped && !othersPending)
            {
                order.MarkDelivered(Now);
            }

            await db.SaveChangesAsync(token);
            return order.Id;
        }, cancellationToken);
        return await GetAsync(orderId, cancellationToken);
    }

    /// <summary>Receives returned goods: restockable units re-enter stock (Returned movement) and the value is credited on the invoice.</summary>
    public Task<OrderDto> CreateReturnAsync(Guid id, CreateReturnRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(id, async (order, token) =>
        {
            var tenant = await LoadTenantAsync(token);
            var lines = new List<(Guid OrderLineId, Guid ProductId, decimal Quantity, bool Restock, decimal Credit)>();
            foreach (var requested in request.Lines)
            {
                var line = order.Line(requested.LineId);
                order.RecordReturn(line.Id, requested.Quantity);
                lines.Add((line.Id, line.ProductId, requested.Quantity, requested.Restock, Money.Round(line.NetUnitPriceInclTax * requested.Quantity)));
            }

            var number = $"RMA-{await sequences.NextValueAsync(SequenceNames.SalesReturn, token):D6}";
            var salesReturn = new SalesReturn(number, order.Id, order.WarehouseId, request.Reason, lines, currentUser.RequiredUserId, Now);
            db.SalesReturns.Add(salesReturn);

            var restock = lines.Where(l => l.Restock).ToList();
            if (restock.Count > 0)
            {
                var items = await ledger.LoadAsync(restock.Select(l => (order.WarehouseId, l.ProductId)).Distinct().ToList(), token);
                var reference = new MovementReference(MovementReferenceType.SalesReturn, salesReturn.Id, number);
                foreach (var line in restock.OrderBy(l => l.ProductId))
                {
                    var item = items[(order.WarehouseId, line.ProductId)];
                    var cost = item.AverageUnitCost > 0 ? item.AverageUnitCost : item.Product?.CostPrice ?? 0;
                    ledger.Receive(item, line.Quantity, cost, StockMovementType.Returned, reference, $"Return {number}: {request.Reason}");
                }
            }

            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.SalesOrderId == order.Id, token);
            invoice?.ApplyCredit(salesReturn.CreditAmount, Today(tenant.TimeZone));
            audit.Record("ReturnReceived", nameof(SalesReturn), salesReturn.Id.ToString(), new { Order = order.Number, Return = number, salesReturn.CreditAmount });
        }, cancellationToken, requireWarehouseAccess: true);

    // ===================== Internals =====================

    private async Task SubmitInternalAsync(SalesOrder order, Customer customer, TenantInfo tenant, Guid userId, CancellationToken token)
    {
        var reason = order.Submit(tenant.Settings, Now);
        if (reason is null && customer.CreditLimit is { } limit)
        {
            var exposure = await db.Invoices.Where(i => i.CustomerId == customer.Id && OpenInvoiceStatuses.Contains(i.Status))
                .SumAsync(i => (decimal?)(i.Total - i.AmountPaid - i.CreditedAmount), token) ?? 0;
            if (exposure + order.Total > limit)
            {
                reason = $"Customer credit limit of {limit:0.00} would be exceeded (open balance {exposure:0.00}).";
                order.FlagForApproval(reason);
            }
        }

        if (reason is null)
        {
            await ApproveInternalAsync(order, customer, tenant, userId, automatic: true, null, token);
        }
    }

    private async Task ApproveInternalAsync(SalesOrder order, Customer customer, TenantInfo tenant, Guid approverId, bool automatic,
        string? notes, CancellationToken token)
    {
        var warehouse = await db.Warehouses.FirstAsync(w => w.Id == order.WarehouseId, token);
        warehouse.EnsureActive();
        order.Approve(approverId, automatic, Now);

        // Reserve every line; any shortage throws and rolls the whole approval back.
        var items = await ledger.LoadAsync(order.Lines.Select(l => (order.WarehouseId, l.ProductId)).ToList(), token);
        var reference = new MovementReference(MovementReferenceType.SalesOrder, order.Id, order.Number);
        foreach (var line in order.Lines.OrderBy(l => l.ProductId))
        {
            db.StockReservations.Add(new StockReservation(order.WarehouseId, line.ProductId, line.Quantity, reference, null, $"Order {order.Number}"));
            ledger.Reserve(items[(order.WarehouseId, line.ProductId)], line.Quantity, reference, $"Reserved for order {order.Number}");
        }

        var invoiceNumber = $"INV-{await sequences.NextValueAsync(SequenceNames.Invoice, token):D6}";
        var invoice = Invoice.FromOrder(invoiceNumber, order, customer, Today(tenant.TimeZone), customer.PaymentTermsDays ?? tenant.Settings.InvoiceDueDays);
        db.Invoices.Add(invoice);

        jobs.Enqueue(new GenerateInvoicePdfJob(invoice.Id, EmailCustomer: customer.Email is not null),
            new JobOptions { DeduplicationKey = $"invoice-pdf:{invoice.Id}" });
        if (customer.Email is not null)
        {
            jobs.Enqueue(new OrderConfirmationEmailJob(order.Id), new JobOptions { DeduplicationKey = $"order-confirmation:{order.Id}" });
        }

        audit.Record(automatic ? "OrderAutoApproved" : "OrderApproved", nameof(SalesOrder), order.Id.ToString(),
            new { order.Number, order.Total, Invoice = invoiceNumber, notes });
    }

    private async Task ReleaseReservationsAsync(SalesOrder order, string reason, CancellationToken token)
    {
        var reservations = await ActiveReservationsAsync(order.Id, token);
        if (reservations.Count == 0)
        {
            return;
        }

        var items = await ledger.LoadAsync(reservations.Values.Select(r => (r.WarehouseId, r.ProductId)).ToList(), token);
        var reference = new MovementReference(MovementReferenceType.SalesOrder, order.Id, order.Number);
        foreach (var reservation in reservations.Values.OrderBy(r => r.ProductId))
        {
            var quantity = reservation.ReleaseRemaining(Now);
            if (quantity > 0)
            {
                ledger.Release(items[(reservation.WarehouseId, reservation.ProductId)], quantity, reference, reason);
            }
        }
    }

    private async Task<Dictionary<Guid, StockReservation>> ActiveReservationsAsync(Guid orderId, CancellationToken token) =>
        (await db.StockReservations
            .Where(r => r.ReferenceType == MovementReferenceType.SalesOrder && r.ReferenceId == orderId && r.Status == ReservationStatus.Active)
            .ToListAsync(token))
        .ToDictionary(r => r.ProductId);

    private async Task<OrderDto> ExecuteAsync(Guid id, Func<SalesOrder, CancellationToken, Task> action, CancellationToken cancellationToken,
        bool requireWarehouseAccess = false)
    {
        await transactions.ExecuteAsync(async token =>
        {
            var order = await LoadAsync(id, token);
            if (requireWarehouseAccess)
            {
                currentUser.EnsureWarehouseAccess(order.WarehouseId);
            }

            await action(order, token);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<SalesOrder> LoadAsync(Guid id, CancellationToken token) =>
        await Scoped(db.SalesOrders.Include(o => o.Lines)).FirstOrDefaultAsync(o => o.Id == id, token)
        ?? throw new NotFoundException("Sales order", id);

    private async Task<Guid?> FindByKeyAsync(string key, CancellationToken token) =>
        await db.SalesOrders.AsNoTracking().Where(o => o.IdempotencyKey == key).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(token);

    private async Task<List<SalesOrderLineInput>> PriceLinesAsync(IReadOnlyList<OrderLineRequest> lines, TenantSettings settings, CancellationToken token)
    {
        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
        return lines.Select(l =>
        {
            var product = products.GetValueOrDefault(l.ProductId) ?? throw new NotFoundException("Product", l.ProductId);
            if (!product.IsActive)
            {
                throw new DomainException("product_inactive", $"Product '{product.Sku}' is inactive.");
            }

            return new SalesOrderLineInput(product.Id, product.Sku, product.Name, l.Quantity, l.UnitPrice ?? product.SellingPrice,
                l.DiscountPercent, product.TaxRate ?? settings.DefaultTaxRate);
        }).ToList();
    }

    private async Task<Customer> RequireCustomerAsync(Guid customerId, CancellationToken token, bool requireActive = true)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, token)
                       ?? throw new NotFoundException("Customer", customerId);
        return !requireActive || customer.IsActive
            ? customer
            : throw new DomainException("customer_inactive", $"Customer '{customer.Name}' is inactive.");
    }

    private async Task RequireWarehouseAsync(Guid warehouseId, CancellationToken token)
    {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, token)
                        ?? throw new NotFoundException("Warehouse", warehouseId);
        warehouse.EnsureActive();
    }

    private sealed record TenantInfo(string BaseCurrency, string TimeZone, TenantSettings Settings);

    private async Task<TenantInfo> LoadTenantAsync(CancellationToken token)
    {
        var tenantId = tenantContext.RequiredTenantId;
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, token);
        return new TenantInfo(tenant.BaseCurrency, tenant.TimeZone, tenant.Settings);
    }

    /// <summary>Warehouse-scoped members only see orders fulfilled from their warehouses.</summary>
    private IQueryable<SalesOrder> Scoped(IQueryable<SalesOrder> orders)
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

    private static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static ShipmentDto ToDto(Shipment s, string orderNumber, IReadOnlyDictionary<Guid, string> skus) => new(
        s.Id, s.Number, s.SalesOrderId, orderNumber, s.WarehouseId, s.Carrier, s.TrackingNumber, s.PackageCount, s.Status, s.ShippedAt,
        s.DeliveredAt, s.Lines.Select(l => new ShipmentLineDto(l.SalesOrderLineId, l.ProductId, skus.GetValueOrDefault(l.ProductId, ""), l.Quantity)).ToList());
}
