using Microsoft.AspNetCore.Mvc;
using StockForge.API.Authorization;
using StockForge.API.Filters;
using StockForge.Application.Billing;
using StockForge.Application.Common;
using StockForge.Application.Procurement;
using StockForge.Application.Sales;
using StockForge.Application.Security;

namespace StockForge.API.Controllers;

[ApiController]
[Route("api/suppliers")]
public sealed class SuppliersController(SupplierService suppliers) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Procurement.ViewSuppliers)]
    public Task<PagedResult<SupplierDto>> List([FromQuery] SupplierQuery query, CancellationToken ct) => suppliers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Procurement.ViewSuppliers)]
    public Task<SupplierDto> Get(Guid id, CancellationToken ct) => suppliers.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Procurement.ManageSuppliers)]
    public async Task<ActionResult<SupplierDto>> Create(SupplierRequest request, CancellationToken ct)
    {
        var supplier = await suppliers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = supplier.Id }, supplier);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Procurement.ManageSuppliers)]
    public Task<SupplierDto> Update(Guid id, SupplierRequest request, CancellationToken ct) => suppliers.UpdateAsync(id, request, ct);

    [HttpGet("{id:guid}/products")]
    [HasPermission(Permissions.Procurement.ViewSuppliers)]
    public Task<IReadOnlyList<SupplierProductDto>> Products(Guid id, CancellationToken ct) => suppliers.ListProductsAsync(id, ct);

    [HttpPut("{id:guid}/products")]
    [HasPermission(Permissions.Procurement.ManageSuppliers)]
    public Task<IReadOnlyList<SupplierProductDto>> UpsertProduct(Guid id, SupplierProductRequest request, CancellationToken ct) =>
        suppliers.UpsertProductAsync(id, request, ct);

    [HttpDelete("{id:guid}/products/{productId:guid}")]
    [HasPermission(Permissions.Procurement.ManageSuppliers)]
    public async Task<IActionResult> RemoveProduct(Guid id, Guid productId, CancellationToken ct)
    {
        await suppliers.RemoveProductAsync(id, productId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController(PurchaseOrderService orders) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Procurement.ViewPurchaseOrders)]
    public Task<PagedResult<PurchaseOrderSummaryDto>> List([FromQuery] PurchaseOrderQuery query, CancellationToken ct) => orders.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Procurement.ViewPurchaseOrders)]
    public Task<PurchaseOrderDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(id, ct);

    /// <summary>Low-stock products with on-order quantities and the preferred supplier's terms.</summary>
    [HttpGet("suggestions")]
    [HasPermission(Permissions.Procurement.CreatePurchaseOrders)]
    public Task<IReadOnlyList<ReorderSuggestionDto>> Suggestions([FromQuery] Guid? warehouseId, CancellationToken ct) =>
        orders.GetReorderSuggestionsAsync(warehouseId, ct);

    [HttpPost]
    [HasPermission(Permissions.Procurement.CreatePurchaseOrders)]
    [Idempotent(Required = true)]
    public async Task<ActionResult<PurchaseOrderDto>> Create(CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var order = await orders.CreateAsync(request, Request.Headers[IdempotencyFilter.Header].ToString(), ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Procurement.CreatePurchaseOrders)]
    public Task<PurchaseOrderDto> Update(Guid id, UpdatePurchaseOrderRequest request, CancellationToken ct) => orders.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.Procurement.CreatePurchaseOrders)]
    [Idempotent(Required = true)]
    public Task<PurchaseOrderDto> Submit(Guid id, CancellationToken ct) => orders.SubmitAsync(id, ct);

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.Procurement.ApprovePurchaseOrders)]
    [Idempotent(Required = true)]
    public Task<PurchaseOrderDto> Approve(Guid id, CancellationToken ct) => orders.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.Procurement.ApprovePurchaseOrders)]
    public Task<PurchaseOrderDto> Reject(Guid id, ReasonRequest request, CancellationToken ct) => orders.RejectAsync(id, request.Reason, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Procurement.CreatePurchaseOrders)]
    public Task<PurchaseOrderDto> Cancel(Guid id, CancellationToken ct) => orders.CancelAsync(id, ct);

    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.Procurement.CreatePurchaseOrders, Permissions.Procurement.ApprovePurchaseOrders)]
    public Task<PurchaseOrderDto> Close(Guid id, CancellationToken ct) => orders.CloseAsync(id, ct);

    /// <summary>Goods received note: books the delivered quantities into stock at the PO cost.</summary>
    [HttpPost("{id:guid}/receipts")]
    [HasPermission(Permissions.Procurement.ReceiveGoods)]
    [Idempotent(Required = true)]
    public Task<PurchaseOrderDto> Receive(Guid id, ReceiveGoodsRequest request, CancellationToken ct) => orders.ReceiveAsync(id, request, ct);
}

[ApiController]
[Route("api/supplier-invoices")]
public sealed class SupplierInvoicesController(SupplierInvoiceService invoices) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Finance.ViewPayables)]
    public Task<PagedResult<SupplierInvoiceSummaryDto>> List([FromQuery] SupplierInvoiceQuery query, CancellationToken ct) => invoices.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Finance.ViewPayables)]
    public Task<SupplierInvoiceDto> Get(Guid id, CancellationToken ct) => invoices.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Finance.ManagePayables)]
    [Idempotent(Required = true)]
    public async Task<ActionResult<SupplierInvoiceDto>> Create(CreateSupplierInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await invoices.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = invoice.Summary.Id }, invoice);
    }

    [HttpPost("{id:guid}/payments")]
    [HasPermission(Permissions.Finance.ManagePayables)]
    [Idempotent(Required = true)]
    public Task<SupplierInvoiceDto> Pay(Guid id, RecordPaymentRequest request, CancellationToken ct) => invoices.RecordPaymentAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Finance.ManagePayables)]
    public Task<SupplierInvoiceDto> Cancel(Guid id, CancellationToken ct) => invoices.CancelAsync(id, ct);
}
