using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Inventory;
using StockForge.Domain.Inventory;
using StockForge.Infrastructure.Persistence;
using StockForge.Tests.Integration.Support;

namespace StockForge.Tests.Integration;

/// <summary>Concurrency, atomicity and ledger integrity of stock operations against real PostgreSQL.</summary>
public sealed class InventoryConsistencyTests(ApiFixture fixture)
{
    [Fact]
    public async Task Simultaneous_reservations_for_limited_stock_never_oversell()
    {
        var tenant = await TestTenant.CreateAsync(fixture, "Concurrency");
        var product = await tenant.CreateProductAsync();
        await tenant.StockInAsync(product.Id, 50);

        // 20 concurrent "orders" of 10 units each compete for 50 units.
        var attempts = Enumerable.Range(0, 20).Select(i => Task.Run(() => tenant.Admin.PostAsync<StockReservationDto>("/api/reservations",
            new CreateReservationRequest(tenant.MainWarehouseId, product.Id, 10, $"ORDER-{i}", null, null)))).ToList();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(5, results.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.Created), r => Assert.Equal("insufficient_stock", r.Code));

        var balance = await tenant.BalanceAsync(product.Id);
        Assert.Equal(50, balance.QuantityOnHand);
        Assert.Equal(50, balance.QuantityReserved);
        Assert.Equal(0, balance.QuantityAvailable);
        await AssertLedgerMatchesBalancesAsync(tenant);
    }

    [Fact]
    public async Task Simultaneous_stock_outs_never_drive_stock_negative()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        await tenant.StockInAsync(product.Id, 30);

        var results = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => Task.Run(() =>
            tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-out",
                new StockOutRequest(tenant.MainWarehouseId, product.Id, 4, null, null, "sale")))));

        Assert.Equal(7, results.Count(r => r.Status == HttpStatusCode.Created)); // 7 × 4 = 28 ≤ 30 < 32
        Assert.Equal(2, (await tenant.BalanceAsync(product.Id)).QuantityOnHand);
        await AssertLedgerMatchesBalancesAsync(tenant);
    }

    [Fact]
    public async Task Concurrent_first_receipts_create_one_balance_row()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => tenant.StockInAsync(product.Id, 1))));

        Assert.Equal(12, results.Length);
        Assert.Equal(12, (await tenant.BalanceAsync(product.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task Failed_transfer_rolls_back_every_line_and_leaves_no_trace()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var destination = await tenant.CreateWarehouseAsync("DEST");
        var plenty = await tenant.CreateProductAsync("Plenty");
        var scarce = await tenant.CreateProductAsync("Scarce");
        await tenant.StockInAsync(plenty.Id, 100);
        await tenant.StockInAsync(scarce.Id, 1);
        var movementsBefore = (await tenant.Admin.GetAsync<PagedResult<StockMovementDto>>("/api/stock-movements")).Ok().TotalCount;

        var failed = await tenant.Admin.PostAsync<StockTransferDto>("/api/stock-transfers", new CreateTransferRequest(
            tenant.MainWarehouseId, destination, [new(plenty.Id, 40), new(scarce.Id, 5)], null));

        Assert.Equal("insufficient_stock", failed.Code);
        Assert.Equal(0, (await tenant.BalanceAsync(plenty.Id)).QuantityReserved); // First line's reservation rolled back.
        Assert.Equal(movementsBefore, (await tenant.Admin.GetAsync<PagedResult<StockMovementDto>>("/api/stock-movements")).Ok().TotalCount);
        Assert.Equal(0, (await tenant.Admin.GetAsync<PagedResult<StockTransferDto>>("/api/stock-transfers")).Ok().TotalCount);
        Assert.Equal(0, (await tenant.Admin.GetAsync<PagedResult<StockReservationDto>>("/api/reservations")).Ok().TotalCount);

        // The numbering sequence was rolled back too: the next transfer gets the first number.
        var ok = (await tenant.Admin.PostAsync<StockTransferDto>("/api/stock-transfers", new CreateTransferRequest(
            tenant.MainWarehouseId, destination, [new(plenty.Id, 40)], null))).Ok();
        Assert.Equal("TRF-000001", ok.Number);
    }

    [Fact]
    public async Task Transfer_lifecycle_moves_stock_and_cost_between_warehouses()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var destination = await tenant.CreateWarehouseAsync("STORE");
        var product = await tenant.CreateProductAsync();
        await tenant.StockInAsync(product.Id, 10, unitCost: 3m);
        await tenant.StockInAsync(product.Id, 10, unitCost: 5m); // Average cost 4.

        var transfer = (await tenant.Admin.PostAsync<StockTransferDto>("/api/stock-transfers",
            new CreateTransferRequest(tenant.MainWarehouseId, destination, [new(product.Id, 8)], "restock"))).Ok();
        Assert.Equal(8, (await tenant.BalanceAsync(product.Id)).QuantityReserved);

        (await tenant.Admin.PostAsync<StockTransferDto>($"/api/stock-transfers/{transfer.Id}/dispatch")).Ok();
        var source = await tenant.BalanceAsync(product.Id);
        Assert.Equal((12m, 0m), (source.QuantityOnHand, source.QuantityReserved));

        var received = (await tenant.Admin.PostAsync<StockTransferDto>($"/api/stock-transfers/{transfer.Id}/receive")).Ok();
        Assert.Equal(TransferStatus.Completed, received.Status);
        var target = await tenant.BalanceAsync(product.Id, destination);
        Assert.Equal(8, target.QuantityOnHand);
        Assert.Equal(4m, target.AverageUnitCost);

        var replay = await tenant.Admin.PostAsync<StockTransferDto>($"/api/stock-transfers/{transfer.Id}/receive");
        Assert.Equal("invalid_state", replay.Code); // Receiving twice is impossible.
        await AssertLedgerMatchesBalancesAsync(tenant);
    }

    [Fact]
    public async Task Low_stock_crossing_enqueues_exactly_one_alert_job()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync(reorderLevel: 10);
        await tenant.StockInAsync(product.Id, 15);

        await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-out", new StockOutRequest(tenant.MainWarehouseId, product.Id, 6, null, null, null));
        await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-out", new StockOutRequest(tenant.MainWarehouseId, product.Id, 2, null, null, null));

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alerts = await db.OutboxMessages.CountAsync(m => m.TenantId == tenant.TenantId && m.Type == "inventory.low_stock_alert");
        Assert.Equal(1, alerts); // Crossing 15 → 9 alerts; 9 → 7 does not alert again.
    }

    /// <summary>Invariant: every balance equals the sum of its ledger rows (on hand and reserved).</summary>
    private async Task AssertLedgerMatchesBalancesAsync(TestTenant tenant)
    {
        await using var connection = fixture.OpenOwnerConnection();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM inventory_items i
            LEFT JOIN (
                SELECT inventory_item_id, sum(quantity_delta) AS on_hand, sum(reserved_delta) AS reserved
                FROM stock_movements WHERE tenant_id = @tenant GROUP BY inventory_item_id) m ON m.inventory_item_id = i.id
            WHERE i.tenant_id = @tenant
              AND (i.quantity_on_hand <> coalesce(m.on_hand, 0) OR i.quantity_reserved <> coalesce(m.reserved, 0))
            """, connection);
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);

        // Active reservations account for every reserved unit.
        await using var reservations = new NpgsqlCommand("""
            SELECT coalesce(sum(i.quantity_reserved), 0) - coalesce((SELECT sum(quantity - quantity_consumed - quantity_released)
                   FROM stock_reservations WHERE tenant_id = @tenant AND status = 'Active'), 0)
            FROM inventory_items i WHERE i.tenant_id = @tenant
            """, connection);
        reservations.Parameters.AddWithValue("tenant", tenant.TenantId);
        Assert.Equal(0m, (decimal)(await reservations.ExecuteScalarAsync())!);
    }
}

public sealed class IdempotencyTests(ApiFixture fixture)
{
    [Fact]
    public async Task Retrying_with_the_same_key_never_applies_twice()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        var key = new Dictionary<string, string> { ["Idempotency-Key"] = $"k-{Guid.NewGuid():N}" };
        var request = new StockInRequest(tenant.MainWarehouseId, product.Id, 7, null, 2, "PO-1", null);

        var first = (await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in", request, key)).Ok();
        var retry = await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in", request, key);

        Assert.Equal(HttpStatusCode.Created, retry.Status);
        Assert.Equal(first.Id, retry.Value!.Id);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(7, (await tenant.BalanceAsync(product.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task Concurrent_duplicates_execute_once()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        var key = new Dictionary<string, string> { ["Idempotency-Key"] = $"k-{Guid.NewGuid():N}" };
        var request = new StockInRequest(tenant.MainWarehouseId, product.Id, 5, null, 2, null, null);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in", request, key))));

        Assert.All(results, r => Assert.True(r.Status is HttpStatusCode.Created or HttpStatusCode.Conflict, r.Status.ToString()));
        Assert.Equal(5, (await tenant.BalanceAsync(product.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_rejected()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        var key = new Dictionary<string, string> { ["Idempotency-Key"] = $"k-{Guid.NewGuid():N}" };

        (await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in", new StockInRequest(tenant.MainWarehouseId, product.Id, 1, null, null, null, null), key)).Ok();
        var other = await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in", new StockInRequest(tenant.MainWarehouseId, product.Id, 2, null, null, null, null), key);

        Assert.Equal("idempotency_key_reused", other.Code);
    }

    [Fact]
    public async Task Failed_requests_release_the_key_for_a_later_retry()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        var key = new Dictionary<string, string> { ["Idempotency-Key"] = $"k-{Guid.NewGuid():N}" };
        var stockOut = new StockOutRequest(tenant.MainWarehouseId, product.Id, 3, null, null, null);

        Assert.Equal("insufficient_stock", (await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-out", stockOut, key)).Code);
        await tenant.StockInAsync(product.Id, 3);
        (await tenant.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-out", stockOut, key)).Ok();
    }

    [Fact]
    public async Task Keys_are_scoped_per_tenant()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        var productA = await a.CreateProductAsync();
        var productB = await b.CreateProductAsync();
        var key = new Dictionary<string, string> { ["Idempotency-Key"] = "shared-key-1" };

        (await a.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in", new StockInRequest(a.MainWarehouseId, productA.Id, 1, null, null, null, null), key)).Ok();
        var replayB = await b.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in", new StockInRequest(b.MainWarehouseId, productB.Id, 1, null, null, null, null), key);

        Assert.Equal(HttpStatusCode.Created, replayB.Status);
        Assert.False(replayB.Headers.Contains("Idempotent-Replayed")); // B's request executed; A's response never leaks.
        Assert.Equal(productB.Id, replayB.Value!.ProductId);
    }
}
