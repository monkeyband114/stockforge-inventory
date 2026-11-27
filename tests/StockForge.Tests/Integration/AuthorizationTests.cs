using System.Net;
using StockForge.Application.Audit;
using StockForge.Application.Catalog;
using StockForge.Application.Common;
using StockForge.Application.Identity;
using StockForge.Application.Inventory;
using StockForge.Application.Platform;
using StockForge.Domain.Inventory;
using StockForge.Tests.Integration.Support;

namespace StockForge.Tests.Integration;

/// <summary>Role/permission enforcement and warehouse scoping through the HTTP API.</summary>
public sealed class AuthorizationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Warehouse_staff_can_record_movements_but_not_manage_or_approve()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        await tenant.StockInAsync(product.Id, 20);
        var (staff, _) = await tenant.CreateMemberAsync("WarehouseStaff", tenant.MainWarehouseId);

        (await staff.PostAsync<StockMovementDto>("/api/inventory/stock-in",
            new StockInRequest(tenant.MainWarehouseId, product.Id, 2, null, null, null, null))).Ok();
        var count = (await staff.PostAsync<StockAdjustmentDto>("/api/stock-adjustments/counts",
            new StockCountRequest(tenant.MainWarehouseId, product.Id, 15, "cycle count"))).Ok();
        Assert.Equal(AdjustmentStatus.PendingApproval, count.Status); // Staff cannot self-approve.

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync<StockAdjustmentDto>($"/api/stock-adjustments/{count.Id}/approve", new ReviewAdjustmentRequest(null))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(null, "X", null, null, tenant.EachUnitId, null, false, 1, 1, null, 0, 0, null))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync<InventoryValuationDto>("/api/inventory/valuation")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync<PagedResult<AuditLogDto>>("/api/audit-logs")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync<PagedResult<MemberDto>>("/api/users")).Status);
    }

    [Fact]
    public async Task Warehouse_scoped_staff_cannot_touch_unassigned_warehouses()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var north = await tenant.CreateWarehouseAsync("NORTH");
        var product = await tenant.CreateProductAsync();
        await tenant.StockInAsync(product.Id, 10, tenant.MainWarehouseId);
        await tenant.StockInAsync(product.Id, 10, north);
        var (staff, auth) = await tenant.CreateMemberAsync("WarehouseStaff", tenant.MainWarehouseId);

        Assert.Equal([tenant.MainWarehouseId], auth.User.WarehouseIds!);
        var visible = (await staff.GetAsync<PagedResult<InventoryBalanceDto>>("/api/inventory?pageSize=50")).Ok();
        Assert.All(visible.Items, row => Assert.Equal(tenant.MainWarehouseId, row.WarehouseId));

        var forbidden = await staff.PostAsync<StockMovementDto>("/api/inventory/stock-out",
            new StockOutRequest(north, product.Id, 1, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);
        Assert.Equal(10, (await tenant.BalanceAsync(product.Id, north)).QuantityOnHand);
    }

    [Fact]
    public async Task Inventory_manager_approval_applies_the_adjustment_and_writes_the_ledger()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        await tenant.StockInAsync(product.Id, 20);
        var (staff, _) = await tenant.CreateMemberAsync("WarehouseStaff", tenant.MainWarehouseId);
        var (manager, _) = await tenant.CreateMemberAsync("InventoryManager");

        var pending = (await staff.PostAsync<StockAdjustmentDto>("/api/stock-adjustments/counts",
            new StockCountRequest(tenant.MainWarehouseId, product.Id, 17, "shelf count"))).Ok();
        Assert.Equal(20, (await tenant.BalanceAsync(product.Id)).QuantityOnHand); // Nothing applied yet.

        var approved = (await manager.PostAsync<StockAdjustmentDto>($"/api/stock-adjustments/{pending.Id}/approve", new ReviewAdjustmentRequest("ok"))).Ok();

        Assert.Equal(AdjustmentStatus.Applied, approved.Status);
        Assert.Equal(17, (await tenant.BalanceAsync(product.Id)).QuantityOnHand);
        var ledger = (await tenant.Admin.GetAsync<PagedResult<StockMovementDto>>($"/api/stock-movements?referenceId={pending.Id}")).Ok();
        Assert.Equal(-3, Assert.Single(ledger.Items).QuantityDelta);
    }

    [Fact]
    public async Task Finance_and_auditor_roles_are_restricted_to_their_duties()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var product = await tenant.CreateProductAsync();
        var (finance, _) = await tenant.CreateMemberAsync("FinanceManager");
        var (auditor, _) = await tenant.CreateMemberAsync("Auditor");

        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsync<StockMovementDto>("/api/inventory/stock-in",
            new StockInRequest(tenant.MainWarehouseId, product.Id, 1, null, null, null, null))).Status);
        (await finance.GetAsync<InventoryValuationDto>("/api/inventory/valuation")).Ok();

        (await auditor.GetAsync<PagedResult<AuditLogDto>>("/api/audit-logs")).Ok();
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PutAsync<ProductDto>($"/api/products/{product.Id}",
            new UpdateProductRequest("x", null, null, null, 1, 1, null, 0, 0, true, null, null))).Status);
    }

    [Fact]
    public async Task Members_cannot_grant_permissions_they_do_not_hold()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var (_, managerAuth) = await tenant.CreateMemberAsync("InventoryManager");

        // Give the manager user-management rights only, then try to escalate someone to admin.
        (await tenant.Admin.PutAsync<MemberDto>($"/api/users/{managerAuth.User.Id}",
            new UpdateMemberRequest(null, ["InventoryManager"], [], ["users.view", "users.manage"]))).Ok();
        using var anonymous = fixture.CreateClient();
        var relogin = (await anonymous.PostAsync<API.Controllers.AuthResponse>("/api/auth/login",
            new LoginRequest(managerAuth.User.Email, TestTenant.Password, null))).Ok();
        using var manager = fixture.CreateClient(relogin.AccessToken);

        var (_, staffAuth) = await tenant.CreateMemberAsync("WarehouseStaff", tenant.MainWarehouseId);
        var escalate = await manager.PutAsync<MemberDto>($"/api/users/{staffAuth.User.Id}",
            new UpdateMemberRequest(null, ["OrganizationAdmin"], [], []));
        Assert.Equal(HttpStatusCode.Forbidden, escalate.Status);
    }

    [Fact]
    public async Task Last_administrator_cannot_be_removed()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var demote = await tenant.Admin.PutAsync<MemberDto>($"/api/users/{tenant.AdminAuth.User.Id}",
            new UpdateMemberRequest(null, ["Auditor"], [], []));
        Assert.Equal("last_admin", demote.Code);
    }

    [Fact]
    public async Task Role_changes_take_effect_without_waiting_for_token_expiry()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var (manager, auth) = await tenant.CreateMemberAsync("InventoryManager");
        (await manager.GetAsync<InventoryValuationDto>("/api/inventory/valuation")).Ok();

        (await tenant.Admin.PutAsync<MemberDto>($"/api/users/{auth.User.Id}", new UpdateMemberRequest(null, ["WarehouseStaff"], [tenant.MainWarehouseId], []))).Ok();

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync<InventoryValuationDto>("/api/inventory/valuation")).Status);
    }

    [Fact]
    public async Task Deactivated_member_loses_access_immediately()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var (staff, auth) = await tenant.CreateMemberAsync("WarehouseStaff", tenant.MainWarehouseId);
        (await staff.GetAsync<PagedResult<InventoryBalanceDto>>("/api/inventory")).Ok();

        (await tenant.Admin.PostAsync<object>($"/api/users/{auth.User.Id}/deactivate")).Ok();

        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync<PagedResult<InventoryBalanceDto>>("/api/inventory")).Status);
    }

    [Fact]
    public async Task Platform_endpoints_require_the_super_admin()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.Admin.GetAsync<PagedResult<TenantSummaryDto>>("/api/platform/tenants")).Status);

        using var anonymous = fixture.CreateClient();
        var super = (await anonymous.PostAsync<API.Controllers.AuthResponse>("/api/auth/login",
            new LoginRequest("superadmin@tests.local", "SuperAdmin!2026", null))).Ok();
        using var platform = fixture.CreateClient(super.AccessToken);
        var tenants = (await platform.GetAsync<PagedResult<TenantSummaryDto>>($"/api/platform/tenants?search={Uri.EscapeDataString(tenant.AdminEmail)}")).Ok();
        Assert.Equal(tenant.TenantId, Assert.Single(tenants.Items).Id);

        // A platform session has no tenant and therefore sees no tenant business data.
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync<PagedResult<ProductListItemDto>>("/api/products")).Status);

        (await platform.PostAsync<object>($"/api/platform/tenants/{tenant.TenantId}/suspend", new SuspendTenantRequest("Unpaid"))).Ok();
        Assert.Equal(HttpStatusCode.Unauthorized, (await tenant.Admin.GetAsync<CurrentUserDto>("/api/auth/me")).Status);
    }

    [Fact]
    public async Task Super_admin_onboards_an_organization_whose_admin_accepts_the_invitation()
    {
        using var anonymous = fixture.CreateClient();
        var super = (await anonymous.PostAsync<API.Controllers.AuthResponse>("/api/auth/login",
            new LoginRequest("superadmin@tests.local", "SuperAdmin!2026", null))).Ok();
        using var platform = fixture.CreateClient(super.AccessToken);
        var adminEmail = $"founder-{Guid.NewGuid():N}@tests.local";

        var created = (await platform.PostAsync<CreatedOrganizationDto>("/api/platform/tenants",
            new CreateOrganizationRequest("Onboarded Co", adminEmail, "Fiona", "Founder", "GBP", "Europe/London"))).Ok();
        Assert.True(created.AdminInvited);

        await fixture.ProcessOutboxUntilAsync(() => Task.FromResult(fixture.Emails.LastTo(adminEmail) is not null));
        var invitation = fixture.Emails.LastTo(adminEmail)!;
        var token = Uri.UnescapeDataString(TestTenant.TokenPattern().Match(invitation.TextBody!).Groups[1].Value);
        var accepted = (await anonymous.PostAsync<API.Controllers.AuthResponse>("/api/auth/accept-invitation",
            new AcceptInvitationRequest(adminEmail, token, TestTenant.Password))).Ok();

        Assert.Equal(created.TenantId, accepted.User.Tenant!.Id);
        Assert.Contains("OrganizationAdmin", accepted.User.Roles);
        using var admin = fixture.CreateClient(accepted.AccessToken);
        var warehouses = (await admin.GetAsync<List<Application.Warehouses.WarehouseDto>>("/api/warehouses")).Ok();
        Assert.Contains(warehouses, w => w.Code == "MAIN");

        // The invitation link is single-use.
        var reuse = await anonymous.PostAsync<object>("/api/auth/accept-invitation", new AcceptInvitationRequest(adminEmail, token, TestTenant.Password));
        Assert.Equal(HttpStatusCode.BadRequest, reuse.Status);
    }
}
