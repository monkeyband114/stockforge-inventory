using Microsoft.AspNetCore.Mvc;
using StockForge.API.Authorization;
using StockForge.API.Filters;
using StockForge.Application.Common;
using StockForge.Application.Dashboard;
using StockForge.Application.Inventory;
using StockForge.Application.Security;

namespace StockForge.API.Controllers;

[ApiController]
[Route("api/inventory")]
public sealed class InventoryController(InventoryService inventory) : ControllerBase
{
    /// <summary>Stock balances per warehouse and product (restricted to assigned warehouses for scoped staff).</summary>
    [HttpGet]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<InventoryBalanceDto>> Balances([FromQuery] InventoryQuery query, CancellationToken ct) =>
        inventory.GetBalancesAsync(query, ct);

    [HttpGet("low-stock")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<InventoryBalanceDto>> LowStock([FromQuery] InventoryQuery query, CancellationToken ct) =>
        inventory.GetBalancesAsync(query with { LowStockOnly = true }, ct);

    [HttpGet("valuation")]
    [HasPermission(Permissions.Inventory.Valuation)]
    public Task<InventoryValuationDto> Valuation([FromQuery] Guid? warehouseId, CancellationToken ct) =>
        inventory.GetValuationAsync(warehouseId, ct);

    [HttpPost("stock-in")]
    [HasPermission(Permissions.Inventory.Receive)]
    [Idempotent]
    public async Task<ActionResult<StockMovementDto>> StockIn(StockInRequest request, CancellationToken ct) =>
        Created((string?)null, await inventory.StockInAsync(request, ct));

    [HttpPost("stock-out")]
    [HasPermission(Permissions.Inventory.Issue)]
    [Idempotent]
    public async Task<ActionResult<StockMovementDto>> StockOut(StockOutRequest request, CancellationToken ct) =>
        Created((string?)null, await inventory.StockOutAsync(request, ct));

    [HttpPost("damaged")]
    [HasPermission(Permissions.Inventory.Issue)]
    [Idempotent]
    public async Task<ActionResult<StockMovementDto>> Damaged(DamagedStockRequest request, CancellationToken ct) =>
        Created((string?)null, await inventory.RecordDamageAsync(request, ct));

    [HttpPost("returns")]
    [HasPermission(Permissions.Inventory.Receive)]
    [Idempotent]
    public async Task<ActionResult<StockMovementDto>> Returned(ReturnedStockRequest request, CancellationToken ct) =>
        Created((string?)null, await inventory.RecordReturnAsync(request, ct));
}

[ApiController]
[Route("api/stock-movements")]
public sealed class StockMovementsController(InventoryService inventory) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<StockMovementDto>> List([FromQuery] MovementQuery query, CancellationToken ct) =>
        inventory.GetMovementsAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<StockMovementDto> Get(Guid id, CancellationToken ct) => inventory.GetMovementAsync(id, ct);
}

[ApiController]
[Route("api/reservations")]
public sealed class ReservationsController(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<StockReservationDto>> List([FromQuery] ReservationQuery query, CancellationToken ct) =>
        reservations.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<StockReservationDto> Get(Guid id, CancellationToken ct) => reservations.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Inventory.Reserve)]
    [Idempotent]
    public async Task<ActionResult<StockReservationDto>> Create(CreateReservationRequest request, CancellationToken ct)
    {
        var reservation = await reservations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = reservation.Id }, reservation);
    }

    [HttpPost("{id:guid}/release")]
    [HasPermission(Permissions.Inventory.Reserve)]
    public Task<StockReservationDto> Release(Guid id, CancellationToken ct) => reservations.ReleaseAsync(id, ct);
}

[ApiController]
[Route("api/stock-adjustments")]
public sealed class StockAdjustmentsController(StockAdjustmentService adjustments) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<StockAdjustmentDto>> List([FromQuery] AdjustmentQuery query, CancellationToken ct) =>
        adjustments.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<StockAdjustmentDto> Get(Guid id, CancellationToken ct) => adjustments.GetAsync(id, ct);

    /// <summary>Records a physical count; the variance becomes an adjustment (auto-applied for approvers).</summary>
    [HttpPost("counts")]
    [HasPermission(Permissions.Inventory.Count)]
    [Idempotent]
    public async Task<ActionResult<StockAdjustmentDto>> Count(StockCountRequest request, CancellationToken ct)
    {
        var adjustment = await adjustments.RequestCountAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = adjustment.Id }, adjustment);
    }

    [HttpPost("corrections")]
    [HasPermission(Permissions.Inventory.RequestAdjustment)]
    [Idempotent]
    public async Task<ActionResult<StockAdjustmentDto>> Correction(StockCorrectionRequest request, CancellationToken ct)
    {
        var adjustment = await adjustments.RequestCorrectionAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = adjustment.Id }, adjustment);
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.Inventory.ApproveAdjustment)]
    public Task<StockAdjustmentDto> Approve(Guid id, ReviewAdjustmentRequest request, CancellationToken ct) =>
        adjustments.ApproveAsync(id, request, ct);

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.Inventory.ApproveAdjustment)]
    public Task<StockAdjustmentDto> Reject(Guid id, ReviewAdjustmentRequest request, CancellationToken ct) =>
        adjustments.RejectAsync(id, request, ct);
}

[ApiController]
[Route("api/stock-transfers")]
public sealed class StockTransfersController(StockTransferService transfers) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<StockTransferDto>> List([FromQuery] TransferQuery query, CancellationToken ct) => transfers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<StockTransferDto> Get(Guid id, CancellationToken ct) => transfers.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Inventory.Transfer)]
    [Idempotent]
    public async Task<ActionResult<StockTransferDto>> Create(CreateTransferRequest request, CancellationToken ct)
    {
        var transfer = await transfers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = transfer.Id }, transfer);
    }

    [HttpPost("{id:guid}/dispatch")]
    [HasPermission(Permissions.Inventory.Transfer)]
    public Task<StockTransferDto> Dispatch(Guid id, CancellationToken ct) => transfers.DispatchAsync(id, ct);

    [HttpPost("{id:guid}/receive")]
    [HasPermission(Permissions.Inventory.Receive)]
    public Task<StockTransferDto> Receive(Guid id, CancellationToken ct) => transfers.ReceiveAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Inventory.Transfer)]
    public Task<StockTransferDto> Cancel(Guid id, CancellationToken ct) => transfers.CancelAsync(id, ct);
}

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(DashboardService dashboards) : ControllerBase
{
    [HttpGet("organization")]
    [HasPermission(Permissions.Reports.Organization, Permissions.Inventory.Analytics)]
    public Task<OrganizationDashboardDto> Organization([FromQuery] int days = 30, CancellationToken ct = default) =>
        dashboards.GetOrganizationAsync(days, ct);

    [HttpGet("warehouses/{warehouseId:guid}")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<WarehouseDashboardDto> Warehouse(Guid warehouseId, [FromQuery] int days = 14, CancellationToken ct = default) =>
        dashboards.GetWarehouseAsync(warehouseId, days, ct);
}
