using System.Text.RegularExpressions;
using StockForge.API.Controllers;
using StockForge.Application.Catalog;
using StockForge.Application.Identity;
using StockForge.Application.Inventory;
using StockForge.Application.Warehouses;
using StockForge.Domain.Inventory;

namespace StockForge.Tests.Integration.Support;

/// <summary>
/// An organization created through the public API (self-service registration), with helpers to add
/// warehouses, products, stock and members. Everything goes through HTTP so authorization runs too.
/// </summary>
public sealed partial class TestTenant
{
    public const string Password = "Integration-Test-1";

    private TestTenant(ApiFixture fixture, AuthResponse auth, string email)
    {
        Fixture = fixture;
        AdminAuth = auth;
        AdminEmail = email;
        Admin = fixture.CreateClient(auth.AccessToken);
    }

    public ApiFixture Fixture { get; }
    public AuthResponse AdminAuth { get; }
    public string AdminEmail { get; }
    public ApiClient Admin { get; }
    public Guid TenantId => AdminAuth.User.Tenant!.Id;
    public Guid MainWarehouseId { get; private set; }
    public Guid EachUnitId { get; private set; }

    public static async Task<TestTenant> CreateAsync(ApiFixture fixture, string name = "Org")
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        var email = $"admin-{unique}@tests.local";
        using var anonymous = fixture.CreateClient();
        var auth = (await anonymous.PostAsync<AuthResponse>("/api/auth/register",
            new RegisterOrganizationRequest($"{name} {unique}", "Test", "Admin", email, Password, "USD", "UTC"))).Ok();
        var tenant = new TestTenant(fixture, auth, email);
        var warehouses = (await tenant.Admin.GetAsync<List<WarehouseDto>>("/api/warehouses")).Ok();
        tenant.MainWarehouseId = warehouses.Single(w => w.Code == "MAIN").Id;
        var units = (await tenant.Admin.GetAsync<List<UnitDto>>("/api/units")).Ok();
        tenant.EachUnitId = units.Single(u => u.Code == "EA").Id;
        return tenant;
    }

    public async Task<Guid> CreateWarehouseAsync(string code) =>
        (await Admin.PostAsync<WarehouseDto>("/api/warehouses",
            new CreateWarehouseRequest(code, $"Warehouse {code}", WarehouseType.Warehouse, null, null, null))).Ok().Id;

    public async Task<ProductDto> CreateProductAsync(string name = "Widget", decimal reorderLevel = 0, decimal cost = 2m, decimal price = 5m) =>
        (await Admin.PostAsync<ProductDto>("/api/products", new CreateProductRequest(
            null, $"{name} {Guid.NewGuid().ToString("N")[..6]}", null, null, EachUnitId, null, true, cost, price, null, reorderLevel, 0, null))).Ok();

    public async Task<StockMovementDto> StockInAsync(Guid productId, decimal quantity, Guid? warehouseId = null, decimal unitCost = 2m) =>
        (await Admin.PostAsync<StockMovementDto>("/api/inventory/stock-in",
            new StockInRequest(warehouseId ?? MainWarehouseId, productId, quantity, null, unitCost, "TEST", null))).Ok();

    public async Task<InventoryBalanceDto> BalanceAsync(Guid productId, Guid? warehouseId = null)
    {
        var page = (await Admin.GetAsync<Application.Common.PagedResult<InventoryBalanceDto>>(
            $"/api/inventory?productId={productId}&warehouseId={warehouseId ?? MainWarehouseId}")).Ok();
        return Assert.Single(page.Items);
    }

    /// <summary>Invites a member, accepts the invitation from the captured email, and returns a signed-in client.</summary>
    public async Task<(ApiClient Client, AuthResponse Auth)> CreateMemberAsync(string role, params Guid[] warehouseIds)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..8]}@tests.local";
        (await Admin.PostAsync<MemberDto>("/api/users", new InviteMemberRequest(email, "Test", role, null, [role], warehouseIds))).Ok();
        await Fixture.ProcessOutboxUntilAsync(() => Task.FromResult(Fixture.Emails.LastTo(email) is not null));

        var invitation = Fixture.Emails.LastTo(email)!;
        var token = Uri.UnescapeDataString(TokenPattern().Match(invitation.TextBody ?? invitation.HtmlBody).Groups[1].Value);
        using var anonymous = Fixture.CreateClient();
        var auth = (await anonymous.PostAsync<AuthResponse>("/api/auth/accept-invitation", new AcceptInvitationRequest(email, token, Password))).Ok();
        return (Fixture.CreateClient(auth.AccessToken), auth);
    }

    [GeneratedRegex(@"token=([^&\s""]+)")]
    internal static partial Regex TokenPattern();
}

public static class FixtureExtensions
{
    /// <summary>Drains the outbox like the worker would (handlers registered via AddWorkerJobs).</summary>
    public static async Task<int> ProcessOutboxAsync(this ApiFixture fixture)
    {
        var processor = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Infrastructure.Jobs.OutboxProcessor>(fixture.Factory.Services);
        var total = 0;
        for (var i = 0; i < 20; i++)
        {
            var processed = await processor.ProcessBatchAsync(CancellationToken.None);
            total += processed;
            if (processed == 0)
            {
                break;
            }
        }

        return total;
    }

    /// <summary>Keeps draining the outbox until a condition holds (tests run in parallel and share the outbox).</summary>
    public static async Task ProcessOutboxUntilAsync(this ApiFixture fixture, Func<Task<bool>> condition)
    {
        for (var i = 0; i < 40; i++)
        {
            await fixture.ProcessOutboxAsync();
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Timed out waiting for background jobs.");
    }
}
