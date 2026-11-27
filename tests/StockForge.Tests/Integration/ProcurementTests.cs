using System.Net;
using StockForge.Application.Billing;
using StockForge.Application.Common;
using StockForge.Application.Inventory;
using StockForge.Application.Organization;
using StockForge.Application.Procurement;
using StockForge.Application.Sales;
using StockForge.Domain.Billing;
using StockForge.Domain.Inventory;
using StockForge.Domain.Procurement;
using StockForge.Tests.Integration.Support;

namespace StockForge.Tests.Integration;

/// <summary>Suppliers, purchase orders, goods receipts and accounts payable.</summary>
public sealed class ProcurementTests(ApiFixture fixture)
{
    private static Dictionary<string, string> Key() => new() { ["Idempotency-Key"] = $"k-{Guid.NewGuid():N}" };

    [Fact]
    public async Task Partial_deliveries_book_stock_at_the_purchase_cost_and_complete_the_order()
    {
        var t = await TestTenant.CreateAsync(fixture, "Procure");
        var product = await t.CreateProductAsync(cost: 2m);
        var supplier = await SupplierAsync(t);
        (await t.Admin.PutAsync<List<SupplierProductDto>>($"/api/suppliers/{supplier.Id}/products",
            new SupplierProductRequest(product.Id, "ACME-1", 3m, 10, 5, true))).Ok();

        var po = (await t.Admin.PostAsync<PurchaseOrderDto>("/api/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, t.MainWarehouseId, null, null, [new PurchaseOrderLineRequest(product.Id, 100)], Submit: true), Key())).Ok();
        Assert.Equal(PurchaseOrderStatus.Approved, po.Status); // 300 is under the default 5,000 threshold.
        Assert.Equal(3m, po.Lines.Single().UnitCost);         // Supplier terms are the default cost.
        var lineId = po.Lines.Single().Id;

        var partial = (await t.Admin.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(lineId, 40)], "DN-1", null), Key())).Ok();
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, partial.Status);
        var balance = await t.BalanceAsync(product.Id);
        Assert.Equal((40m, 3m), (balance.QuantityOnHand, balance.AverageUnitCost));

        var complete = (await t.Admin.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(lineId, 60)], "DN-2", null), Key())).Ok();
        Assert.Equal(PurchaseOrderStatus.Received, complete.Status);
        Assert.Equal(2, complete.Receipts.Count);
        Assert.Equal(100, (await t.BalanceAsync(product.Id)).QuantityOnHand);

        var over = await t.Admin.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(lineId, 1)], null, null), Key());
        Assert.Equal("invalid_state", over.Code);

        var movements = (await t.Admin.GetAsync<PagedResult<StockMovementDto>>($"/api/stock-movements?productId={product.Id}&referenceType=GoodsReceipt")).Ok();
        Assert.Equal(2, movements.TotalCount);
        Assert.All(movements.Items, m => Assert.Equal(StockMovementType.StockIn, m.Type));
    }

    [Fact]
    public async Task Over_receipt_is_rejected_and_leaves_stock_untouched()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync();
        var supplier = await SupplierAsync(t);
        var po = (await t.Admin.PostAsync<PurchaseOrderDto>("/api/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, t.MainWarehouseId, null, null, [new PurchaseOrderLineRequest(product.Id, 10, 1m)], Submit: true), Key())).Ok();

        var over = await t.Admin.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(po.Lines.Single().Id, 11)], null, null), Key());

        Assert.Equal("over_receipt", over.Code);
        var page = (await t.Admin.GetAsync<PagedResult<InventoryBalanceDto>>($"/api/inventory?productId={product.Id}")).Ok();
        Assert.True(page.Items.Count == 0 || page.Items[0].QuantityOnHand == 0);
    }

    [Fact]
    public async Task Large_purchase_orders_need_approval_and_warehouse_staff_cannot_approve()
    {
        var t = await TestTenant.CreateAsync(fixture);
        (await t.Admin.PutAsync<OrganizationDto>("/api/organization/settings",
            new TenantSettingsDto(0, 0, false, 0, 30, true, true, PurchaseApprovalThreshold: 100))).Ok();
        var product = await t.CreateProductAsync();
        var supplier = await SupplierAsync(t);
        var (finance, _) = await t.CreateMemberAsync("FinanceManager");
        var (staff, _) = await t.CreateMemberAsync("WarehouseStaff", t.MainWarehouseId);

        var po = (await t.Admin.PostAsync<PurchaseOrderDto>("/api/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, t.MainWarehouseId, null, null, [new PurchaseOrderLineRequest(product.Id, 100, 5m)], Submit: true), Key())).Ok();
        Assert.Equal(PurchaseOrderStatus.PendingApproval, po.Status);
        Assert.Equal(t.AdminAuth.User.Id, po.CreatedByUserId);
        (await staff.GetAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}")).Ok(); // Receiving staff can see POs due at their warehouse.

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/approve", null, Key())).Status);
        Assert.Equal("self_approval", (await t.Admin.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/approve", null, Key())).Code);
        Assert.Equal("invalid_state", (await staff.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(po.Lines.Single().Id, 1)], null, null), Key())).Code); // Not approved yet.

        var approved = (await finance.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/approve", null, Key())).Ok();
        Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);
        (await staff.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(po.Lines.Single().Id, 100)], "DN", null), Key())).Ok();
    }

    [Fact]
    public async Task Supplier_invoices_are_three_way_matched_deduplicated_and_paid()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync();
        var supplier = await SupplierAsync(t);
        var po = (await t.Admin.PostAsync<PurchaseOrderDto>("/api/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, t.MainWarehouseId, null, null, [new PurchaseOrderLineRequest(product.Id, 50, 3m)], Submit: true), Key())).Ok();
        (await t.Admin.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(po.Lines.Single().Id, 40)], null, null), Key())).Ok(); // 40 × 3 = 120 received.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var matched = (await t.Admin.PostAsync<SupplierInvoiceDto>("/api/supplier-invoices",
            new CreateSupplierInvoiceRequest(supplier.Id, po.Id, "BILL-1", today, null, 120, 0, null), Key())).Ok();
        var unmatched = (await t.Admin.PostAsync<SupplierInvoiceDto>("/api/supplier-invoices",
            new CreateSupplierInvoiceRequest(supplier.Id, po.Id, "BILL-2", today, null, 50, 0, null), Key())).Ok();
        var duplicate = await t.Admin.PostAsync<SupplierInvoiceDto>("/api/supplier-invoices",
            new CreateSupplierInvoiceRequest(supplier.Id, po.Id, "BILL-1", today, null, 1, 0, null), Key());

        Assert.True(matched.Summary.MatchesReceipts);
        Assert.False(unmatched.Summary.MatchesReceipts); // Invoiced more than was received.
        Assert.Equal("duplicate_supplier_invoice", duplicate.Code);

        var paid = (await t.Admin.PostAsync<SupplierInvoiceDto>($"/api/supplier-invoices/{matched.Summary.Id}/payments",
            new RecordPaymentRequest(120, PaymentMethod.BankTransfer, "WIRE-1", null), Key())).Ok();
        Assert.Equal((SupplierInvoiceStatus.Paid, 0m), (paid.Summary.Status, paid.Summary.BalanceDue));
        var supplierView = (await t.Admin.GetAsync<SupplierDto>($"/api/suppliers/{supplier.Id}")).Ok();
        Assert.Equal(50m, supplierView.OutstandingPayables);
    }

    [Fact]
    public async Task Reorder_suggestions_include_on_order_quantities_and_preferred_supplier()
    {
        var t = await TestTenant.CreateAsync(fixture);
        var product = await t.CreateProductAsync(reorderLevel: 10);
        await t.StockInAsync(product.Id, 3);
        var supplier = await SupplierAsync(t);
        (await t.Admin.PutAsync<List<SupplierProductDto>>($"/api/suppliers/{supplier.Id}/products",
            new SupplierProductRequest(product.Id, null, 4m, 0, null, true))).Ok();
        (await t.Admin.PostAsync<PurchaseOrderDto>("/api/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, t.MainWarehouseId, null, null, [new PurchaseOrderLineRequest(product.Id, 5)], Submit: true), Key())).Ok();

        var suggestions = (await t.Admin.GetAsync<List<ReorderSuggestionDto>>("/api/purchase-orders/suggestions")).Ok();

        var suggestion = Assert.Single(suggestions, s => s.ProductId == product.Id);
        Assert.Equal((3m, 5m, supplier.Id, 4m), (suggestion.QuantityAvailable, suggestion.QuantityOnOrder, suggestion.PreferredSupplierId!.Value, suggestion.SupplierUnitCost!.Value));
        Assert.Equal(3m, suggestion.SuggestedQuantity); // target 10 + max(0,1) = 11 − 3 available − 5 on order.
    }

    [Fact]
    public async Task Suppliers_and_purchase_orders_are_isolated_between_tenants()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        var product = await a.CreateProductAsync();
        var supplier = await SupplierAsync(a);
        var po = (await a.Admin.PostAsync<PurchaseOrderDto>("/api/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, a.MainWarehouseId, null, null, [new PurchaseOrderLineRequest(product.Id, 5, 1m)], Submit: true), Key())).Ok();

        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync<SupplierDto>($"/api/suppliers/{supplier.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.PostAsync<PurchaseOrderDto>($"/api/purchase-orders/{po.Id}/receipts",
            new ReceiveGoodsRequest([new ReceiveLineRequest(po.Lines.Single().Id, 1)], null, null), Key())).Status);
        Assert.Equal(0, (await b.Admin.GetAsync<PagedResult<SupplierDto>>("/api/suppliers")).Ok().TotalCount);
    }

    private static async Task<SupplierDto> SupplierAsync(TestTenant t) =>
        (await t.Admin.PostAsync<SupplierDto>("/api/suppliers", new SupplierRequest($"Supplier {Guid.NewGuid():N}"[..20], "Sam", "sales@supplier.test",
            null, null, null, 30, 7, null))).Ok();
}
