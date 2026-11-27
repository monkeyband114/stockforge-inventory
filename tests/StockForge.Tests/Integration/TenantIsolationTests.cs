using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StockForge.Application.Abstractions;
using StockForge.Application.Catalog;
using StockForge.Application.Common;
using StockForge.Application.Inventory;
using StockForge.Infrastructure.Persistence;
using StockForge.Tests.Integration.Support;

namespace StockForge.Tests.Integration;

/// <summary>Proves that two organizations cannot access each other's data at any of the four isolation layers.</summary>
public sealed class TenantIsolationTests(ApiFixture fixture)
{
    // ---------- Layer 1: API / application ----------

    [Fact]
    public async Task Tenant_B_cannot_read_update_or_delete_tenant_A_records_by_id()
    {
        var a = await TestTenant.CreateAsync(fixture, "Alpha");
        var b = await TestTenant.CreateAsync(fixture, "Beta");
        var product = await a.CreateProductAsync();
        var movement = await a.StockInAsync(product.Id, 10);

        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync<ProductDto>($"/api/products/{product.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.PutAsync<ProductDto>($"/api/products/{product.Id}",
            new UpdateProductRequest("Hijacked", null, null, null, 0, 0, null, 0, 0, true, null, null))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.DeleteAsync<object>($"/api/products/{product.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync<StockMovementDto>($"/api/stock-movements/{movement.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync<object>($"/api/warehouses/{a.MainWarehouseId}")).Status);

        // The record is untouched for its owner.
        Assert.Equal(product.Name, (await a.Admin.GetAsync<ProductDto>($"/api/products/{product.Id}")).Ok().Name);
    }

    [Fact]
    public async Task Lists_only_contain_the_callers_own_records()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        var productA = await a.CreateProductAsync("OnlyInA");
        await a.StockInAsync(productA.Id, 5);
        await b.CreateProductAsync("OnlyInB");

        var listB = (await b.Admin.GetAsync<PagedResult<ProductListItemDto>>("/api/products?pageSize=200")).Ok();
        Assert.DoesNotContain(listB.Items, p => p.Id == productA.Id);
        var movementsB = (await b.Admin.GetAsync<PagedResult<StockMovementDto>>("/api/stock-movements?pageSize=200")).Ok();
        Assert.DoesNotContain(movementsB.Items, m => m.ProductId == productA.Id);
        var inventoryB = (await b.Admin.GetAsync<PagedResult<InventoryBalanceDto>>("/api/inventory?pageSize=200")).Ok();
        Assert.Empty(inventoryB.Items);
    }

    [Fact]
    public async Task Tenant_B_cannot_reference_tenant_A_ids_in_its_own_operations()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        var productA = await a.CreateProductAsync();
        await a.StockInAsync(productA.Id, 10);
        var productB = await b.CreateProductAsync();
        await b.StockInAsync(productB.Id, 10);

        // Another tenant's warehouse / product ids are simply "not found" in B's world.
        var stockIntoForeignWarehouse = await b.Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in",
            new StockInRequest(a.MainWarehouseId, productB.Id, 1, null, null, null, null));
        var transferForeignProduct = await b.Admin.PostAsync<StockTransferDto>("/api/stock-transfers",
            new CreateTransferRequest(b.MainWarehouseId, await b.CreateWarehouseAsync("B2"), [new(productA.Id, 1)], null));

        Assert.Equal(HttpStatusCode.NotFound, stockIntoForeignWarehouse.Status);
        Assert.Equal(HttpStatusCode.NotFound, transferForeignProduct.Status);
        Assert.Equal(10, (await a.BalanceAsync(productA.Id)).QuantityOnHand);
    }

    [Fact]
    public async Task Tenant_id_in_request_body_is_ignored()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);

        // An attacker adds a tenantId field: the server never binds it, the product lands in B.
        var body = new Dictionary<string, object?>
        {
            ["tenantId"] = a.TenantId, ["name"] = "Smuggled", ["baseUnitId"] = b.EachUnitId,
            ["costPrice"] = 1, ["sellingPrice"] = 2, ["reorderLevel"] = 0, ["reorderQuantity"] = 0, ["generateBarcode"] = false,
        };
        var created = (await b.Admin.PostAsync<ProductDto>("/api/products", body)).Ok();

        Assert.Equal(HttpStatusCode.NotFound, (await a.Admin.GetAsync<ProductDto>($"/api/products/{created.Id}")).Status);
        (await b.Admin.GetAsync<ProductDto>($"/api/products/{created.Id}")).Ok();
    }

    // ---------- Layer 2: EF Core global query filters ----------

    [Fact]
    public async Task Query_filters_hide_other_tenants_and_fail_closed_without_a_tenant()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        var productA = await a.CreateProductAsync();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Products.AnyAsync()); // No tenant in scope: nothing is visible.

        context.SetSystem(b.TenantId, "test");
        Assert.False(await db.Products.AnyAsync(p => p.Id == productA.Id));

        // Defense in depth: even code that bypasses the EF filter is stopped by row-level security.
        Assert.False(await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == productA.Id));
    }

    [Fact]
    public async Task Saving_a_row_for_another_tenant_is_rejected_by_the_interceptor()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetSystem(b.TenantId, "test");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var membership = new Domain.Tenancy.TenantMembership(a.TenantId, a.AdminAuth.User.Id, ["Auditor"], null, DateTimeOffset.UtcNow);
        db.TenantMemberships.Add(membership);

        await Assert.ThrowsAsync<Infrastructure.Persistence.Interceptors.TenantIsolationException>(() => db.SaveChangesAsync());
    }

    // ---------- Layer 3: database constraints ----------

    [Fact]
    public async Task Composite_foreign_keys_reject_cross_tenant_references_even_for_the_owner()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        var productA = await a.CreateProductAsync();

        await using var owner = fixture.OpenOwnerConnection();
        await using var command = new NpgsqlCommand("""
            INSERT INTO inventory_items (id, tenant_id, warehouse_id, product_id, quantity_on_hand, quantity_reserved, average_unit_cost, created_at)
            VALUES (@id, @tenant, @warehouse, @product, 0, 0, 0, now())
            """, owner);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant", b.TenantId);      // B's row...
        command.Parameters.AddWithValue("warehouse", b.MainWarehouseId);
        command.Parameters.AddWithValue("product", productA.Id);    // ...pointing at A's product.

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    // ---------- Layer 4: PostgreSQL row-level security ----------

    [Fact]
    public async Task Row_level_security_limits_the_runtime_role_to_the_session_tenant()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var b = await TestTenant.CreateAsync(fixture);
        await a.CreateProductAsync();
        await b.CreateProductAsync();

        await using var connection = fixture.OpenAppConnection();
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM products")); // No tenant set: nothing.

        await ExecuteAsync(connection, $"SELECT set_config('app.tenant_id', '{a.TenantId}', false)");
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT count(*) FROM products WHERE tenant_id = '{b.TenantId}'"));
        Assert.True(await ScalarAsync(connection, $"SELECT count(*) FROM products WHERE tenant_id = '{a.TenantId}'") > 0);

        // Writing a row for another tenant violates the policy's WITH CHECK.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"INSERT INTO units_of_measure (id, tenant_id, code, name, allows_fractions, is_active, created_at) VALUES ('{Guid.CreateVersion7()}', '{b.TenantId}', 'ZZ', 'Evil', false, true, now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Every_tenant_owned_table_has_row_level_security()
    {
        await using var owner = fixture.OpenOwnerConnection();
        await using var command = new NpgsqlCommand("""
            SELECT c.table_name
            FROM information_schema.columns c
            JOIN pg_class pc ON pc.relname = c.table_name
            JOIN pg_namespace n ON n.oid = pc.relnamespace AND n.nspname = c.table_schema
            WHERE c.table_schema = 'public' AND c.column_name = 'tenant_id' AND pc.relkind = 'r' AND NOT pc.relrowsecurity
            """, owner);
        var unprotected = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                unprotected.Add(reader.GetString(0));
            }
        }

        Assert.Empty(unprotected.Except(TenantRls.ExcludedTables));
    }

    [Fact]
    public async Task Runtime_role_does_not_own_tables_so_rls_cannot_be_bypassed()
    {
        await using var connection = fixture.OpenAppConnection();
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner = current_user"));
        Assert.False((bool)(await new NpgsqlCommand("SELECT rolbypassrls FROM pg_roles WHERE rolname = current_user", connection).ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Ledger_and_audit_rows_are_append_only()
    {
        var a = await TestTenant.CreateAsync(fixture);
        var product = await a.CreateProductAsync();
        await a.StockInAsync(product.Id, 3);

        await using var app = fixture.OpenAppConnection();
        await ExecuteAsync(app, $"SELECT set_config('app.tenant_id', '{a.TenantId}', false)");
        var appEx = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, "UPDATE stock_movements SET quantity_delta = 1000"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, appEx.SqlState);

        // Even the owner is stopped by the trigger.
        await using var owner = fixture.OpenOwnerConnection();
        var ownerEx = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(owner, "DELETE FROM audit_logs"));
        Assert.Contains("append-only", ownerEx.MessageText, StringComparison.Ordinal);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
