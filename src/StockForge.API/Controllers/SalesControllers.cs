using Microsoft.AspNetCore.Mvc;
using StockForge.API.Authorization;
using StockForge.API.Filters;
using StockForge.Application.Billing;
using StockForge.Application.Common;
using StockForge.Application.Sales;
using StockForge.Application.Security;

namespace StockForge.API.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Customers.View)]
    public Task<PagedResult<CustomerDto>> List([FromQuery] CustomerQuery query, CancellationToken ct) => customers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Customers.View)]
    public Task<CustomerDto> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Customers.Manage)]
    public async Task<ActionResult<CustomerDto>> Create(CustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Customers.Manage)]
    public Task<CustomerDto> Update(Guid id, CustomerRequest request, CancellationToken ct) => customers.UpdateAsync(id, request, ct);
}

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(SalesOrderService orders) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Orders.View)]
    public Task<PagedResult<OrderSummaryDto>> List([FromQuery] OrderQuery query, CancellationToken ct) => orders.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Orders.View)]
    public Task<OrderDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(id, ct);

    /// <summary>Creates a draft (or, with <c>submit: true</c>, submits it). Requires an Idempotency-Key.</summary>
    [HttpPost]
    [HasPermission(Permissions.Orders.Create)]
    [Idempotent(Required = true)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await orders.CreateAsync(request, Request.Headers[IdempotencyFilter.Header].ToString(), ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Orders.Create)]
    public Task<OrderDto> Update(Guid id, UpdateOrderRequest request, CancellationToken ct) => orders.UpdateAsync(id, request, ct);

    /// <summary>Submits a draft; orders within the approval thresholds are approved and stock is reserved immediately.</summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.Orders.Create)]
    [Idempotent(Required = true)]
    public Task<OrderDto> Submit(Guid id, CancellationToken ct) => orders.SubmitAsync(id, ct);

    /// <summary>Approves a pending order: reserves stock, issues the invoice and queues PDF/emails in one transaction.</summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.Orders.Approve)]
    [Idempotent(Required = true)]
    public Task<OrderDto> Approve(Guid id, ApproveOrderRequest request, CancellationToken ct) => orders.ApproveAsync(id, request, ct);

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.Orders.Approve)]
    public Task<OrderDto> Reject(Guid id, ReasonRequest request, CancellationToken ct) => orders.RejectAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Orders.Cancel)]
    public Task<OrderDto> Cancel(Guid id, ReasonRequest request, CancellationToken ct) => orders.CancelAsync(id, request, ct);

    [HttpPost("{id:guid}/process")]
    [HasPermission(Permissions.Orders.Fulfill)]
    public Task<OrderDto> Process(Guid id, CancellationToken ct) => orders.StartProcessingAsync(id, ct);

    [HttpPost("{id:guid}/start-picking")]
    [HasPermission(Permissions.Orders.Fulfill)]
    public Task<OrderDto> StartPicking(Guid id, CancellationToken ct) => orders.StartPickingAsync(id, ct);

    [HttpPost("{id:guid}/pick")]
    [HasPermission(Permissions.Orders.Fulfill)]
    public Task<OrderDto> Pick(Guid id, RecordPickRequest request, CancellationToken ct) => orders.RecordPickAsync(id, request, ct);

    [HttpPost("{id:guid}/pack")]
    [HasPermission(Permissions.Orders.Fulfill)]
    public Task<OrderDto> Pack(Guid id, CancellationToken ct) => orders.PackAsync(id, ct);

    /// <summary>Ships everything packed (stock out against the reservation) and creates the shipment.</summary>
    [HttpPost("{id:guid}/ship")]
    [HasPermission(Permissions.Orders.Fulfill)]
    [Idempotent(Required = true)]
    public Task<OrderDto> Ship(Guid id, ShipOrderRequest request, CancellationToken ct) => orders.ShipAsync(id, request, ct);

    [HttpPost("{id:guid}/returns")]
    [HasPermission(Permissions.Orders.ProcessReturns)]
    [Idempotent(Required = true)]
    public Task<OrderDto> Return(Guid id, CreateReturnRequest request, CancellationToken ct) => orders.CreateReturnAsync(id, request, ct);
}

[ApiController]
[Route("api/shipments")]
public sealed class ShipmentsController(SalesOrderService orders) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Orders.View)]
    public Task<ShipmentDto> Get(Guid id, CancellationToken ct) => orders.GetShipmentAsync(id, ct);

    [HttpPost("{id:guid}/deliver")]
    [HasPermission(Permissions.Orders.Fulfill)]
    public Task<OrderDto> Deliver(Guid id, CancellationToken ct) => orders.DeliverShipmentAsync(id, ct);
}

[ApiController]
[Route("api/fulfillment")]
public sealed class FulfillmentController(SalesOrderService orders) : ControllerBase
{
    /// <summary>Picking/packing queue for the caller's warehouses (or one warehouse).</summary>
    [HttpGet("queue")]
    [HasPermission(Permissions.Orders.Fulfill, Permissions.Orders.View)]
    public Task<IReadOnlyList<FulfillmentQueueItemDto>> Queue([FromQuery] Guid? warehouseId, CancellationToken ct) =>
        orders.GetQueueAsync(warehouseId, ct);
}

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController(InvoiceService invoices) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Finance.ViewInvoices)]
    public Task<PagedResult<InvoiceSummaryDto>> List([FromQuery] InvoiceQuery query, CancellationToken ct) => invoices.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Finance.ViewInvoices)]
    public Task<InvoiceDto> Get(Guid id, CancellationToken ct) => invoices.GetAsync(id, ct);

    /// <summary>Downloads the invoice PDF; 404 with code <c>pdf_pending</c> while the worker is still generating it.</summary>
    [HttpGet("{id:guid}/pdf")]
    [HasPermission(Permissions.Finance.ViewInvoices)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var pdf = await invoices.GetPdfAsync(id, ct);
        if (pdf is null)
        {
            var problem = new ProblemDetails { Status = 404, Title = "Not ready", Detail = "The invoice PDF is still being generated. Try again shortly." };
            problem.Extensions["code"] = "pdf_pending";
            return NotFound(problem);
        }

        Response.Headers.CacheControl = "private, no-store";
        return File(pdf.Content, pdf.ContentType, pdf.FileName);
    }

    [HttpPost("{id:guid}/regenerate-pdf")]
    [HasPermission(Permissions.Finance.ManageInvoices)]
    public async Task<IActionResult> RegeneratePdf(Guid id, CancellationToken ct)
    {
        await invoices.RegeneratePdfAsync(id, ct);
        return Accepted();
    }

    [HttpPost("{id:guid}/payments")]
    [HasPermission(Permissions.Finance.RecordPayments)]
    [Idempotent(Required = true)]
    public Task<InvoiceDto> RecordPayment(Guid id, RecordPaymentRequest request, CancellationToken ct) =>
        invoices.RecordPaymentAsync(id, request, ct);
}

[ApiController]
[Route("api/finance")]
public sealed class FinanceController(InvoiceService invoices) : ControllerBase
{
    [HttpGet("summary")]
    [HasPermission(Permissions.Finance.ViewReports)]
    public Task<FinanceSummaryDto> Summary([FromQuery] int months = 12, CancellationToken ct = default) => invoices.GetSummaryAsync(months, ct);
}
