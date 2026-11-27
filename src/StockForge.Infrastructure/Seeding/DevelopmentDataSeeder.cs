using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Application.Catalog;
using StockForge.Application.Inventory;
using StockForge.Application.Security;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Identity;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Seeding;

/// <summary>
/// Realistic demo data for local development: two organizations (to make isolation visible), users for
/// every role, warehouses, catalog, and 30 days of stock history generated through the real application
/// services, so every balance is backed by ledger rows exactly as in production.
/// </summary>
internal sealed class DevelopmentDataSeeder(IServiceScopeFactory scopeFactory, IOptions<SeedOptions> options, ILogger<DevelopmentDataSeeder> logger)
{
    private sealed record DemoUser(string Email, string First, string Last, string Role, string? Title, string[] Warehouses);

    private sealed record DemoProduct(string Name, string Category, decimal Cost, decimal Price, decimal Reorder, string Unit = "EA",
        string? AltUnit = null, decimal AltFactor = 0, string? Description = null);

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using (var probe = scopeFactory.CreateAsyncScope())
        {
            var db = probe.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Tenants.AnyAsync(t => t.Slug == "acme-distribution", cancellationToken))
            {
                return;
            }
        }

        logger.LogInformation("Seeding development demo data…");
        var inventoryManagerId = await SeedAcmeAsync(cancellationToken);
        await SeedGlobexAsync(inventoryManagerId, cancellationToken);
        logger.LogInformation("Demo data ready. Sign in as admin@acme.test with the configured demo password");
    }

    private async Task<Guid> SeedAcmeAsync(CancellationToken ct)
    {
        var warehouses = new (string Code, string Name, WarehouseType Type, Address Address)[]
        {
            ("CDC", "Central Distribution Center", WarehouseType.DistributionCenter, new Address("400 Logistics Pkwy", null, "Newark", "NJ", "07114", "United States")),
            ("NHB", "North Hub", WarehouseType.Warehouse, new Address("85 Harbor St", "Unit 4", "Boston", "MA", "02210", "United States")),
            ("RS01", "Manhattan Retail Store", WarehouseType.RetailStore, new Address("220 W 34th St", null, "New York", "NY", "10001", "United States")),
        };
        var users = new DemoUser[]
        {
            new("admin@acme.test", "Olivia", "Bennett", RoleNames.OrganizationAdmin, "Operations Director", []),
            new("inventory@acme.test", "Marcus", "Reed", RoleNames.InventoryManager, "Inventory Manager", []),
            new("whmanager@acme.test", "Priya", "Nair", RoleNames.WarehouseManager, "Warehouse Manager", ["CDC"]),
            new("staff@acme.test", "Diego", "Alvarez", RoleNames.WarehouseStaff, "Warehouse Associate", ["CDC"]),
            new("staff.north@acme.test", "Hannah", "Kim", RoleNames.WarehouseStaff, "Warehouse Associate", ["NHB"]),
            new("procurement@acme.test", "Samuel", "Osei", RoleNames.ProcurementOfficer, "Buyer", []),
            new("sales@acme.test", "Chloe", "Martin", RoleNames.SalesOfficer, "Account Executive", []),
            new("finance@acme.test", "Daniel", "Novak", RoleNames.FinanceManager, "Finance Manager", []),
            new("auditor@acme.test", "Grace", "Liu", RoleNames.Auditor, "Internal Auditor", []),
        };
        var categories = new (string Code, string Name)[]
        {
            ("ELEC", "Electronics & Devices"), ("OFF", "Office Supplies"), ("PKG", "Packaging Materials"),
            ("CLN", "Cleaning & Janitorial"), ("SAFE", "Safety Equipment"), ("BEV", "Beverages"),
        };
        var products = new DemoProduct[]
        {
            new("Wireless Barcode Scanner", "ELEC", 48m, 89m, 10, Description: "2.4 GHz handheld 1D/2D scanner with charging cradle."),
            new("Thermal Label Printer", "ELEC", 165m, 279m, 4, Description: "203 dpi direct-thermal printer for shipping labels."),
            new("USB-C Charging Cable 2m", "ELEC", 2.4m, 8.99m, 100),
            new("Rugged Handheld Terminal", "ELEC", 410m, 699m, 3, Description: "Android mobile computer, IP65, integrated scanner."),
            new("LED Work Light", "ELEC", 14m, 29.5m, 15),
            new("A4 Copy Paper (500 sheets)", "OFF", 3.1m, 5.99m, 200, AltUnit: "BOX", AltFactor: 5),
            new("Ballpoint Pens (Box of 50)", "OFF", 6.5m, 12.99m, 30),
            new("Lever Arch File", "OFF", 1.8m, 3.99m, 60),
            new("Sticky Notes 76x76 (12 pack)", "OFF", 4.2m, 8.49m, 40),
            new("Desk Organizer", "OFF", 7.8m, 16.9m, 12),
            new("Corrugated Box 40x30x30", "PKG", 0.85m, 1.9m, 500, AltUnit: "PK", AltFactor: 25),
            new("Stretch Wrap Film 500mm", "PKG", 9.4m, 18.5m, 40),
            new("Packing Tape 48mm x 66m", "PKG", 1.15m, 2.79m, 150, AltUnit: "CS", AltFactor: 36),
            new("Bubble Wrap Roll 1m x 50m", "PKG", 14.6m, 27.9m, 20),
            new("Pallet Labels (1000)", "PKG", 11m, 21m, 10),
            new("Industrial Floor Cleaner 5L", "CLN", 12.3m, 24.9m, 25),
            new("Nitrile Gloves (Box of 100)", "CLN", 5.6m, 11.5m, 80),
            new("Microfiber Cloths (10 pack)", "CLN", 4.9m, 9.99m, 30),
            new("Hand Sanitizer 1L", "CLN", 3.7m, 7.49m, 50),
            new("Hi-Vis Safety Vest", "SAFE", 3.2m, 7.95m, 50),
            new("Safety Helmet EN397", "SAFE", 6.9m, 15.5m, 25),
            new("Steel Toe Boots", "SAFE", 32m, 64.9m, 12),
            new("Ear Defenders", "SAFE", 8.5m, 17.9m, 20),
            new("First Aid Kit (Workplace)", "SAFE", 18m, 34.9m, 8),
            new("Bottled Water 500ml (24 pack)", "BEV", 4.1m, 7.99m, 80),
            new("Instant Coffee 750g", "BEV", 9.3m, 16.49m, 20),
            new("Green Tea Bags (100)", "BEV", 3.4m, 6.79m, 20),
        };

        var ids = await SeedTenantAsync("Acme Distribution", "ops@acme.test", "USD", "America/New_York", warehouses, users,
            categories, products, historyDays: 30, ct);

        // Workflows in progress, so every screen has something to act on.
        await RunAsAsync(ids, "admin@acme.test", async services =>
        {
            var transfers = services.GetRequiredService<StockTransferService>();
            var product = ids.Products;
            var completed = await transfers.CreateAsync(new CreateTransferRequest(ids.Warehouses["CDC"], ids.Warehouses["RS01"],
                [new(product["Hi-Vis Safety Vest"], 40), new(product["Nitrile Gloves (Box of 100)"], 30)], "Weekly store replenishment"), ct);
            await transfers.DispatchAsync(completed.Id, ct);
            await transfers.ReceiveAsync(completed.Id, ct);

            var inTransit = await transfers.CreateAsync(new CreateTransferRequest(ids.Warehouses["CDC"], ids.Warehouses["NHB"],
                [new(product["Corrugated Box 40x30x30"], 600), new(product["Packing Tape 48mm x 66m"], 120)], "North hub packaging top-up"), ct);
            await transfers.DispatchAsync(inTransit.Id, ct);

            await transfers.CreateAsync(new CreateTransferRequest(ids.Warehouses["NHB"], ids.Warehouses["RS01"],
                [new(product["Bottled Water 500ml (24 pack)"], 20)], "Store beverage restock"), ct);

            await services.GetRequiredService<ReservationService>().CreateAsync(new CreateReservationRequest(
                ids.Warehouses["CDC"], product["Rugged Handheld Terminal"], 2, "HOLD-CUST-1042", null, "Held for key-account demo"), ct);
        }, ct);

        await RunAsAsync(ids, "staff@acme.test", async services =>
        {
            var adjustments = services.GetRequiredService<StockAdjustmentService>();
            var balances = await services.GetRequiredService<IAppDbContext>().InventoryItems.AsNoTracking()
                .Where(i => i.WarehouseId == ids.Warehouses["CDC"] && i.ProductId == ids.Products["Lever Arch File"])
                .Select(i => i.QuantityOnHand)
                .FirstAsync(ct);
            await adjustments.RequestCountAsync(new StockCountRequest(ids.Warehouses["CDC"], ids.Products["Lever Arch File"],
                Math.Max(0, balances - 7), "Cycle count aisle 14 — shelf damage"), ct);
            await adjustments.RequestCorrectionAsync(new StockCorrectionRequest(ids.Warehouses["CDC"], ids.Products["Desk Organizer"],
                AdjustmentReason.Found, 3, "Found behind returns cage"), ct);
        }, ct);

        return ids.Users["inventory@acme.test"];
    }

    private async Task SeedGlobexAsync(Guid sharedUserId, CancellationToken ct)
    {
        var warehouses = new (string Code, string Name, WarehouseType Type, Address Address)[]
        {
            ("HAM", "Hamburg Distribution", WarehouseType.DistributionCenter, new Address("Hafenstraße 12", null, "Hamburg", null, "20457", "Germany")),
            ("BER", "Berlin Flagship Store", WarehouseType.RetailStore, new Address("Friedrichstraße 88", null, "Berlin", null, "10117", "Germany")),
        };
        var users = new DemoUser[]
        {
            new("admin@globex.test", "Lena", "Fischer", RoleNames.OrganizationAdmin, "Managing Director", []),
            new("staff@globex.test", "Jonas", "Weber", RoleNames.WarehouseStaff, "Store Associate", ["BER"]),
        };
        var categories = new (string Code, string Name)[] { ("COF", "Coffee & Tea"), ("BAK", "Bakery"), ("SUP", "Store Supplies") };
        var products = new DemoProduct[]
        {
            new("Espresso Beans 1kg", "COF", 11m, 21.9m, 30, Unit: "KG"),
            new("Oat Milk 1L", "COF", 1.2m, 2.49m, 60),
            new("Chai Concentrate 1L", "COF", 4.8m, 9.5m, 15),
            new("Chocolate Croissant (frozen, 50)", "BAK", 22m, 39m, 10),
            new("Sourdough Loaf (frozen, 20)", "BAK", 18m, 33m, 8),
            new("Paper Cups 12oz (1000)", "SUP", 38m, 59m, 6),
            new("Reusable Coffee Cup", "SUP", 3.5m, 9.9m, 40),
            new("Napkins (5000)", "SUP", 18m, 32m, 5),
        };

        var ids = await SeedTenantAsync("Globex Retail", "hello@globex.test", "EUR", "Europe/Berlin", warehouses, users,
            categories, products, historyDays: 21, ct);

        // A user who belongs to both organizations, to demonstrate switching (and that data never mixes).
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
        context.SetSystem(ids.TenantId, "seed");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantMemberships.Add(new TenantMembership(ids.TenantId, sharedUserId, [RoleNames.Auditor], "Group Auditor (shared)", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    private sealed record SeededTenant(Guid TenantId, Dictionary<string, Guid> Warehouses, Dictionary<string, Guid> Users, Dictionary<string, Guid> Products);

    private async Task<SeededTenant> SeedTenantAsync(
        string name, string email, string currency, string timeZone,
        (string Code, string Name, WarehouseType Type, Address Address)[] warehouses,
        DemoUser[] users,
        (string Code, string Name)[] categories,
        DemoProduct[] products,
        int historyDays,
        CancellationToken ct)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-historyDays).Date;
        var startAt = new DateTimeOffset(start, TimeSpan.Zero).AddHours(8);
        SeededTenant ids;

        // 1. Organization, warehouses and members.
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ScopeContext>();
            context.SetRequestInfo(null, "seed", "seed");
            var provisioner = services.GetRequiredService<TenantProvisioner>();
            var db = services.GetRequiredService<AppDbContext>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            var tenant = await provisioner.PrepareAsync(name, email, currency, timeZone, ct);
            context.EnterTenant(tenant.Id);
            using var _ = AdjustableClock.Use(startAt.AddDays(-1));
            provisioner.Provision(tenant, createDefaultWarehouse: false);
            var warehouseEntities = warehouses.ToDictionary(w => w.Code, w => new Warehouse(w.Code, w.Name, w.Type, w.Address, null, null));
            db.Warehouses.AddRange(warehouseEntities.Values);
            await db.SaveChangesAsync(ct);

            var userIds = new Dictionary<string, Guid>();
            foreach (var u in users)
            {
                var user = await userManager.FindByEmailAsync(u.Email);
                if (user is null)
                {
                    user = new ApplicationUser
                    {
                        UserName = u.Email, Email = u.Email, EmailConfirmed = true, FirstName = u.First, LastName = u.Last,
                        CreatedAt = startAt.AddDays(-1),
                    };
                    (await userManager.CreateAsync(user, options.Value.DemoPassword)).EnsureSucceeded();
                }

                var membership = new TenantMembership(tenant.Id, user.Id, [u.Role], u.Title, startAt.AddDays(-1));
                membership.AssignWarehouses(u.Warehouses.Select(code => warehouseEntities[code].Id));
                db.TenantMemberships.Add(membership);
                userIds[u.Email] = user.Id;
            }

            await db.SaveChangesAsync(ct);
            ids = new SeededTenant(tenant.Id, warehouseEntities.ToDictionary(w => w.Key, w => w.Value.Id), userIds, []);
        }

        // 2. Catalog and opening stock, as the organization administrator.
        await RunAsAsync(ids, users[0].Email, async services =>
        {
            using var _ = AdjustableClock.Use(startAt);
            var db = services.GetRequiredService<IAppDbContext>();
            var units = await db.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Code, u => u.Id, ct);
            var categoryService = services.GetRequiredService<CategoryService>();
            var categoryIds = new Dictionary<string, Guid>();
            foreach (var (code, categoryName) in categories)
            {
                categoryIds[code] = (await categoryService.CreateAsync(new CreateCategoryRequest(categoryName, code, null, null), ct)).Id;
            }

            var productService = services.GetRequiredService<ProductService>();
            foreach (var p in products)
            {
                var created = await productService.CreateAsync(new CreateProductRequest(
                    null, p.Name, p.Description, categoryIds[p.Category], units[p.Unit], null, GenerateBarcode: true,
                    p.Cost, p.Price, null, p.Reorder, p.Reorder * 3,
                    p.AltUnit is null ? null : [new ProductUnitRequest(units[p.AltUnit], p.AltFactor, null)]), ct);
                ids.Products[p.Name] = created.Id;
            }

            var inventory = services.GetRequiredService<InventoryService>();
            var random = new Random(name.Length * 7919);
            var warehouseCodes = ids.Warehouses.Keys.ToList();
            foreach (var p in products)
            {
                foreach (var (code, index) in warehouseCodes.Select((c, i) => (c, i)))
                {
                    // Primary warehouse carries the most stock; some items start deliberately thin.
                    var multiplier = index == 0 ? 6 : index == 1 ? 3 : 1.5;
                    var quantity = Math.Round(p.Reorder * (decimal)(multiplier * (0.6 + (random.NextDouble() * 0.9))));
                    if (quantity <= 0)
                    {
                        continue;
                    }

                    await inventory.StockInAsync(new StockInRequest(ids.Warehouses[code], ids.Products[p.Name], quantity, null,
                        p.Cost, $"OPEN-{code}", "Opening balance"), ct);
                }
            }

            // 3. Daily activity: issues (sales/consumption), replenishment receipts, occasional damage and returns.
            for (var day = 1; day <= historyDays; day++)
            {
                foreach (var code in warehouseCodes)
                {
                    var events = random.Next(2, 6);
                    for (var e = 0; e < events; e++)
                    {
                        var p = products[random.Next(products.Length)];
                        var at = startAt.AddDays(day).AddHours(random.Next(0, 9)).AddMinutes(random.Next(0, 60));
                        using var clock = AdjustableClock.Use(at);
                        var productId = ids.Products[p.Name];
                        var roll = random.NextDouble();
                        try
                        {
                            if (roll < 0.62)
                            {
                                var qty = Math.Max(1, Math.Round(p.Reorder * (decimal)(0.05 + (random.NextDouble() * 0.25))));
                                await inventory.StockOutAsync(new StockOutRequest(ids.Warehouses[code], productId, qty, null,
                                    $"ISS-{day:D2}{e}", "Customer order (pre-order-module history)"), ct);
                            }
                            else if (roll < 0.90)
                            {
                                var qty = Math.Max(1, Math.Round(p.Reorder * (decimal)(0.4 + random.NextDouble())));
                                var cost = decimal.Round(p.Cost * (decimal)(0.95 + (random.NextDouble() * 0.1)), 4);
                                await inventory.StockInAsync(new StockInRequest(ids.Warehouses[code], productId, qty, null, cost,
                                    $"GRN-{day:D2}{e}", "Supplier delivery"), ct);
                            }
                            else if (roll < 0.95)
                            {
                                await inventory.RecordDamageAsync(new DamagedStockRequest(ids.Warehouses[code], productId, 1, null,
                                    "Damaged during handling"), ct);
                            }
                            else
                            {
                                await inventory.RecordReturnAsync(new ReturnedStockRequest(ids.Warehouses[code], productId, 1, null, null,
                                    $"RMA-{day:D2}{e}", "Customer return — resaleable"), ct);
                            }
                        }
                        catch (InsufficientStockException)
                        {
                            // History is random; skipping an issue that would oversell is exactly what production does.
                        }
                    }
                }
            }
        }, ct);

        return ids;
    }

    private async Task RunAsAsync(SeededTenant ids, string email, Func<IServiceProvider, Task> action, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var userId = ids.Users[email];
        var snapshot = await LoadSnapshotAsync(services, ids.TenantId, userId, ct);
        var context = services.GetRequiredService<ScopeContext>();
        context.SetRequestInfo(null, "seed", "seed");
        context.SetUser(userId, email, email, null, false, ids.TenantId, snapshot.Permissions, snapshot.Warehouses);
        await action(services);
    }

    private static async Task<(IReadOnlySet<string> Permissions, IReadOnlySet<Guid>? Warehouses)> LoadSnapshotAsync(
        IServiceProvider services, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var membership = await db.TenantMemberships.IgnoreQueryFilters().AsNoTracking().Include(m => m.WarehouseAssignments)
            .FirstAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);
        var roles = membership.Roles;
        var permissions = await (
                from rc in db.RoleClaims
                join r in db.Roles on rc.RoleId equals r.Id
                where roles.Contains(r.Name!) && rc.ClaimType == Permissions.ClaimType
                select rc.ClaimValue!)
            .ToListAsync(ct);
        var set = permissions.ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<Guid>? warehouses = set.Contains(Permissions.Inventory.ViewAllWarehouses)
            ? null
            : membership.WarehouseAssignments.Select(a => a.WarehouseId).ToHashSet();
        return (set, warehouses);
    }
}
