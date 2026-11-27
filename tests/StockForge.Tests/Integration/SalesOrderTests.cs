using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StockForge.Application.Abstractions;
using StockForge.Application.Billing;
using StockForge.Application.Common;
using StockForge.Application.Inventory;
using StockForge.Application.Organization;
using StockForge.Application.Sales;
using StockForge.Application.Warehouses;
using StockForge.Domain.Billing;
using StockForge.Domain.Inventory;
using StockForge.Domain.Sales;
using StockForge.Tests.Integration.Support;

namespace StockForge.Tests.Integration;

/// <summary>Order lifecycle, transactional approval, fulfillment, invoicing and their concurrency guarantees.</summary>
public sealed class SalesOrderTests(ApiFixture fixture)
{
    private static Dictionary<string, string> Key(string? key = null) => new() { ["Idempotency-Key"] = key ?? $"k-{Guid.NewGuid():N}" };

    [Fact]
    public async Task Small_order_is_auto_approved_reserved_invoiced_shipped_and_paid()
    {
        var t = await TestTenant.CreateAsync(fixture, "Lifecycle");
        await SettingsAsync(t, taxRate: 0.1m);
        var product = await t.CreateProductAsync(price: 5m);
        await t.StockInAsync(product.Id, 100);
        var customer = await CustomerAsync(t);

        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 10));

        Assert.Equal(SalesOrderStatus.Approved, order.Status);
        Assert.Equal((50m, 5m, 55m), (order.Subtotal, order.TaxTotal, order.Total));
        Assert.NotNull(order.Invoice);
        Assert.Equal((InvoiceStatus.Issued, 55m), (order.Invoice!.Status, order.Invoice.BalanceDue));
        Assert.Equal(10, (await t.BalanceAsync(product.Id)).QuantityReserved);

        var shipped = await FulfillAsync(t.Admin, order);
        Assert.Equal(SalesOrderStatus.Shipped, shipped.Status);
        var balance = await t.BalanceAsync(product.Id);
        Assert.Equal((90m, 0m), (balance.QuantityOnHand, balance.QuantityReserved));

        var delivered = (await t.Admin.PostAsync<OrderDto>($"/api/shipments/{shipped.Shipments.Single().Id}/deliver")).Ok();
        Assert.Equal(SalesOrderStatus.Delivered, delivered.Status);

        var paid = (await t.Admin.PostAsync<InvoiceDto>($"/api/invoices/{order.Invoice.Id}/payments",
            new RecordPaymentRequest(55m, PaymentMethod.BankTransfer, "ACH-1", null), Key())).Ok();
        Assert.Equal((InvoiceStatus.Paid, 0m), (paid.Status, paid.BalanceDue));

        var ledger = (await t.Admin.GetAsync<PagedResult<StockMovementDto>>($"/api/stock-movements?referenceId={order.Id}")).Ok();
        Assert.Equal([StockMovementType.Reservation, StockMovementType.StockOut], ledger.Items.OrderBy(m => m.CreatedAt).Select(m => m.Type));
    }

    [Fact]
    public async Task Bulk_order_waits_for_manager_approval_and_creator_cannot_self_approve()
    {
        var t = await TestTenant.CreateAsync(fixture);
        await SettingsAsync(t, quantityThreshold: 20);
        var product = await t.CreateProductAsync();
        await t.StockInAsync(product.Id, 100);
        var customer = await CustomerAsync(t);
        var (finance, _) = await t.CreateMemberAsync("FinanceManager");

        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 30));
        Assert.Equal(SalesOrderStatus.PendingApproval, order.Status);
        Assert.Contains("Bulk order", order.ApprovalReason, StringComparison.Ordinal);
        Assert.Equal(0, (await t.BalanceAsync(product.Id)).QuantityReserved);

        var selfApproval = await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest(null), Key());
        Assert.Equal("self_approval", selfApproval.Code);

        var approved = (await finance.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest("ok"), Key())).Ok();
        Assert.Equal(SalesOrderStatus.Approved, approved.Status);
        Assert.Equal(30, (await t.BalanceAsync(product.Id)).QuantityReserved);
    }

    [Fact]
    public async Task Concurrent_approvals_for_limited_stock_never_overcommit()
    {
        var t = await TestTenant.CreateAsync(fixture, "ApprovalRace");
        await SettingsAsync(t, requireApprovalForAll: true);
        var product = await t.CreateProductAsync();
        await t.StockInAsync(product.Id, 30);
        var customer = await CustomerAsync(t);
        var (finance, _) = await t.CreateMemberAsync("FinanceManager");
        var orders = new List<OrderDto>();
        for (var i = 0; i < 5; i++)
        {
            orders.Add(await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 10)));
        }

        var results = await Task.WhenAll(orders.Select(o => Task.Run(() =>
            finance.PostAsync<OrderDto>($"/api/orders/{o.Id}/approve", new ApproveOrderRequest(null), Key()))));

        Assert.Equal(3, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Equal("insufficient_stock", r.Code));
        Assert.Equal(30, (await t.BalanceAsync(product.Id)).QuantityReserved);
        var invoices = (await t.Admin.GetAsync<PagedResult<InvoiceSummaryDto>>("/api/invoices")).Ok();
        Assert.Equal(3, invoices.TotalCount);
        var pending = (await t.Admin.GetAsync<PagedResult<OrderSummaryDto>>("/api/orders?status=PendingApproval")).Ok();
        Assert.Equal(2, pending.TotalCount);
    }

    [Fact]
    public async Task Failed_approval_rolls_back_reservations_invoice_and_numbering()
    {
        var t = await TestTenant.CreateAsync(fixture);
        await SettingsAsync(t, requireApprovalForAll: true);
        var plenty = await t.CreateProductAsync("Plenty");
        var scarce = await t.CreateProductAsync("Scarce");
        await t.StockInAsync(plenty.Id, 100);
        await t.StockInAsync(scarce.Id, 1);
        var customer = await CustomerAsync(t);
        var (finance, _) = await t.CreateMemberAsync("FinanceManager");
        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (plenty.Id, 40), (scarce.Id, 5));

        var failed = await finance.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest(null), Key());

        Assert.Equal("insufficient_stock", failed.Code);
        Assert.Equal(SalesOrderStatus.PendingApproval, (await t.Admin.GetAsync<OrderDto>($"/api/orders/{order.Id}")).Ok().Status);
        Assert.Equal(0, (await t.BalanceAsync(plenty.Id)).QuantityReserved);
        Assert.Equal(0, (await t.Admin.GetAsync<PagedResult<InvoiceSummaryDto>>("/api/invoices")).Ok().TotalCount);
        Assert.Equal(0, (await t.Admin.GetAsync<PagedResult<StockReservationDto>>("/api/reservations")).Ok().TotalCount);

        await t.StockInAsync(scarce.Id, 10);
        var approved = (await finance.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest(null), Key())).Ok();
        Assert.Equal("INV-000001", approved.Invoice!.Number);
    }

    [Fact]
    public async Task Retried_approval_replays_and_never_reserves_or_bills_twice()
    {
        var t = await TestTenant.CreateAsync(fixture);
        await SettingsAsync(t, requireApprovalForAll: true);
        var product = await t.CreateProductAsync();
        await t.StockInAsync(product.Id, 50);
        var customer = await CustomerAsync(t);
        var (finance, _) = await t.CreateMemberAsync("FinanceManager");
        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 8));
        var key = Key();

        var first = (await finance.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest(null), key)).Ok();
        var retry = await finance.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest(null), key);
        var secondAttempt = await finance.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest(null), Key());

        Assert.Equal(HttpStatusCode.OK, retry.Status);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(first.Invoice!.Id, retry.Value!.Invoice!.Id);
        Assert.Equal("invalid_state", secondAttempt.Code); // A new key cannot approve twice either.
        Assert.Equal(8, (await t.BalanceAsync(product.Id)).QuantityReserved);
        Assert.Equal(1, (await t.Admin.GetAsync<PagedResult<InvoiceSummaryDto>>("/api/invoices")).Ok().TotalCount);
    }

    [Fact]
    public async Task Duplicate_order_submissions_with_one_key_create_one_order()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync();
        await t.StockInAsync(product.Id, 100);
        var customer = await CustomerAsync(t);
        var key = $"k-{Guid.NewGuid():N}";
        var request = new CreateOrderRequest(customer.Id, t.MainWarehouseId, null, null, null, 0, null, [new OrderLineRequest(product.Id, 3)], Submit: true);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
            t.Admin.PostAsync<OrderDto>("/api/orders", request, Key(key)))));

        Assert.All(results, r => Assert.True(r.Status is HttpStatusCode.Created or HttpStatusCode.Conflict, r.Status.ToString()));
        Assert.Equal(1, (await t.Admin.GetAsync<PagedResult<OrderSummaryDto>>("/api/orders")).Ok().TotalCount);
        Assert.Equal(3, (await t.BalanceAsync(product.Id)).QuantityReserved);
    }

    [Fact]
    public async Task Order_creation_requires_an_idempotency_key()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync();
        var customer = await CustomerAsync(t);

        var response = await t.Admin.PostAsync<OrderDto>("/api/orders", new CreateOrderRequest(customer.Id, t.MainWarehouseId, null, null, null, 0,
            null, [new OrderLineRequest(product.Id, 1)]));

        Assert.Equal("idempotency_key_required", response.Code);
    }

    [Fact]
    public async Task Partial_shipments_and_returns_keep_stock_and_invoice_consistent()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync(price: 10m);
        await t.StockInAsync(product.Id, 100);
        var customer = await CustomerAsync(t);
        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 10));
        var lineId = order.Lines.Single().Id;

        var partial = await FulfillAsync(t.Admin, order, pickQuantity: 6);
        Assert.Equal(SalesOrderStatus.PartiallyFulfilled, partial.Status);
        var mid = await t.BalanceAsync(product.Id);
        Assert.Equal((94m, 4m), (mid.QuantityOnHand, mid.QuantityReserved));

        (await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/start-picking")).Ok();
        (await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/pick", new RecordPickRequest([new PickLineRequest(lineId, 4)]))).Ok();
        (await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/pack")).Ok();
        var complete = (await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/ship", new ShipOrderRequest("DHL", "TRK2", 1), Key())).Ok();
        Assert.Equal(SalesOrderStatus.Shipped, complete.Status);
        Assert.Equal(2, complete.Shipments.Count);

        var returned = (await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/returns",
            new CreateReturnRequest([new ReturnLineRequest(lineId, 3, Restock: true)], "Damaged packaging"), Key())).Ok();
        Assert.Equal(93, (await t.BalanceAsync(product.Id)).QuantityOnHand);
        Assert.Equal(30m, returned.Returns.Single().CreditAmount);
        var invoice = (await t.Admin.GetAsync<InvoiceDto>($"/api/invoices/{order.Invoice!.Id}")).Ok();
        Assert.Equal((30m, 70m), (invoice.CreditedAmount, invoice.BalanceDue));

        var overReturn = await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/returns",
            new CreateReturnRequest([new ReturnLineRequest(lineId, 8)], "again"), Key());
        Assert.Equal("over_return", overReturn.Code);
    }

    [Fact]
    public async Task Cancelling_an_unshipped_order_releases_stock_and_cancels_the_invoice()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync();
        await t.StockInAsync(product.Id, 20);
        var customer = await CustomerAsync(t);
        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 5));

        var cancelled = (await t.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/cancel", new ReasonRequest("Customer changed their mind"))).Ok();

        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(InvoiceStatus.Cancelled, cancelled.Invoice!.Status);
        Assert.Equal(0, (await t.BalanceAsync(product.Id)).QuantityReserved);
    }

    [Fact]
    public async Task Orders_and_invoices_are_isolated_between_tenants()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        var product = await a.CreateProductAsync();
        await a.StockInAsync(product.Id, 10);
        var customer = await CustomerAsync(a);
        var order = await OrderAsync(a.Admin, a, customer.Id, submit: true, (product.Id, 1));

        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync<OrderDto>($"/api/orders/{order.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync<InvoiceDto>($"/api/invoices/{order.Invoice!.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.PostAsync<OrderDto>($"/api/orders/{order.Id}/cancel", new ReasonRequest("x"))).Status);
        Assert.Equal(0, (await b.Admin.GetAsync<PagedResult<OrderSummaryDto>>("/api/orders")).Ok().TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.PostAsync<OrderDto>("/api/orders",
            new CreateOrderRequest(customer.Id, b.MainWarehouseId, null, null, null, 0, null, [new OrderLineRequest(product.Id, 1)]), Key())).Status);
    }

    [Fact]
    public async Task Invoice_pdf_is_generated_in_the_background_and_emailed_to_the_customer()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync();
        await t.StockInAsync(product.Id, 10);
        var email = $"buyer-{Guid.NewGuid():N}@customer.test";
        var customer = await CustomerAsync(t, email);
        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 2));
        var invoiceId = order.Invoice!.Id;

        Assert.Equal("pdf_pending", (await t.Admin.GetAsync<object>($"/api/invoices/{invoiceId}/pdf")).Code);
        await fixture.ProcessOutboxUntilAsync(() => Task.FromResult(
            fixture.Emails.Sent.Any(m => m.To == email && m.Attachments is { Count: > 0 })));

        using var pdf = await t.Admin.Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{invoiceId}/pdf")
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t.Admin.AccessToken) },
        });
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Contains(fixture.Emails.Sent, m => m.To == email && m.Subject.Contains(order.Number, StringComparison.Ordinal)); // confirmation
    }

    [Fact]
    public async Task Unpaid_invoices_past_due_become_overdue_and_appear_in_the_finance_summary()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync(price: 20m);
        await t.StockInAsync(product.Id, 10);
        var customer = await CustomerAsync(t, terms: 0);
        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 2));

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ScopeContext>().SetSystem(t.TenantId, "test");
            using var _ = Infrastructure.AdjustableClock.Use(DateTimeOffset.UtcNow.AddDays(2));
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<InvoiceService>().MarkOverdueAsync(default));
        }

        Assert.Equal(InvoiceStatus.Overdue, (await t.Admin.GetAsync<InvoiceDto>($"/api/invoices/{order.Invoice!.Id}")).Ok().Status);
        var summary = (await t.Admin.GetAsync<FinanceSummaryDto>("/api/finance/summary")).Ok();
        Assert.Equal((40m, 1), (summary.OverdueTotal, summary.OverdueInvoices));
        Assert.Equal(40m, summary.InvoicedThisMonth);
    }

    [Fact]
    public async Task Exceeding_the_customer_credit_limit_requires_approval()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync(price: 50m);
        await t.StockInAsync(product.Id, 10);
        var customer = await CustomerAsync(t, creditLimit: 80m);

        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 2));

        Assert.Equal(SalesOrderStatus.PendingApproval, order.Status);
        Assert.Contains("credit limit", order.ApprovalReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warehouse_staff_can_only_fulfil_orders_of_their_warehouse()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var other = await t.CreateWarehouseAsync("OTHER");
        var product = await t.CreateProductAsync();
        await t.StockInAsync(product.Id, 10);
        var customer = await CustomerAsync(t);
        var order = await OrderAsync(t.Admin, t, customer.Id, submit: true, (product.Id, 2));
        var (staff, _) = await t.CreateMemberAsync("WarehouseStaff", other);

        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostAsync<OrderDto>($"/api/orders/{order.Id}/process")).Status);
        var queue = (await staff.GetAsync<List<FulfillmentQueueItemDto>>("/api/fulfillment/queue")).Ok();
        Assert.Empty(queue);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync<OrderDto>($"/api/orders/{order.Id}/approve", new ApproveOrderRequest(null), Key())).Status);
    }

    // ---------- helpers ----------

    private static async Task SettingsAsync(TestTenant t, decimal taxRate = 0, decimal amountThreshold = 0, decimal quantityThreshold = 0,
        bool requireApprovalForAll = false) =>
        (await t.Admin.PutAsync<OrganizationDto>("/api/organization/settings",
            new TenantSettingsDto(amountThreshold, quantityThreshold, requireApprovalForAll, taxRate, 30, true, true))).Ok();

    private static async Task<CustomerDto> CustomerAsync(TestTenant t, string? email = null, int? terms = null, decimal? creditLimit = null) =>
        (await t.Admin.PostAsync<CustomerDto>("/api/customers", new CustomerRequest($"Customer {Guid.NewGuid():N}"[..20], email, null, null,
            new AddressDto("1 Main St", null, "Springfield", null, "12345", "United States"), null, terms, creditLimit, null))).Ok();

    private static async Task<OrderDto> OrderAsync(ApiClient client, TestTenant t, Guid customerId, bool submit, params (Guid Product, decimal Quantity)[] lines) =>
        (await client.PostAsync<OrderDto>("/api/orders", new CreateOrderRequest(customerId, t.MainWarehouseId, null, null, null, 0, null,
            lines.Select(l => new OrderLineRequest(l.Product, l.Quantity)).ToList(), submit), Key())).Ok();

    /// <summary>Process → pick (all or a quantity per line) → pack → ship.</summary>
    private static async Task<OrderDto> FulfillAsync(ApiClient client, OrderDto order, decimal? pickQuantity = null)
    {
        (await client.PostAsync<OrderDto>($"/api/orders/{order.Id}/process")).Ok();
        (await client.PostAsync<OrderDto>($"/api/orders/{order.Id}/start-picking")).Ok();
        (await client.PostAsync<OrderDto>($"/api/orders/{order.Id}/pick",
            new RecordPickRequest(order.Lines.Select(l => new PickLineRequest(l.Id, pickQuantity ?? l.Quantity)).ToList()))).Ok();
        (await client.PostAsync<OrderDto>($"/api/orders/{order.Id}/pack")).Ok();
        return (await client.PostAsync<OrderDto>($"/api/orders/{order.Id}/ship", new ShipOrderRequest("UPS", "1Z999", 1), Key())).Ok();
    }
}
