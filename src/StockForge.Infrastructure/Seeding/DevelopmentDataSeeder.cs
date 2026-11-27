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

        await SeedSalesAsync(ids, ct);
        await SeedProcurementAsync(ids, ct);
        return ids.Users["inventory@acme.test"];
    }

    /// <summary>Suppliers with negotiated terms, purchase orders in every state, goods receipts and supplier bills.</summary>
    private async Task SeedProcurementAsync(SeededTenant ids, CancellationToken ct)
    {
        var suppliers = new (string Name, string Contact, string Email, int Terms, int Lead, string[] Products)[]
        {
            ("SafeGuard Industrial Supply", "Rachel Moore", "orders@safeguard.test", 30, 5,
                ["Hi-Vis Safety Vest", "Safety Helmet EN397", "Steel Toe Boots", "Ear Defenders", "First Aid Kit (Workplace)"]),
            ("PackRight Packaging Co.", "Tom Becker", "sales@packright.test", 45, 7,
                ["Corrugated Box 40x30x30", "Stretch Wrap Film 500mm", "Packing Tape 48mm x 66m", "Bubble Wrap Roll 1m x 50m", "Pallet Labels (1000)"]),
            ("CleanPro Distributors", "Aisha Khan", "ar@cleanpro.test", 30, 4,
                ["Industrial Floor Cleaner 5L", "Nitrile Gloves (Box of 100)", "Microfiber Cloths (10 pack)", "Hand Sanitizer 1L"]),
            ("OfficeHub Wholesale", "Ben Carter", "trade@officehub.test", 15, 3,
                ["A4 Copy Paper (500 sheets)", "Ballpoint Pens (Box of 50)", "Lever Arch File", "Sticky Notes 76x76 (12 pack)", "Desk Organizer"]),
        };

        var supplierIds = new Dictionary<string, Guid>();
        var start = DateTimeOffset.UtcNow.AddDays(-26);
        await RunAsAsync(ids, "procurement@acme.test", async services =>
        {
            using var _ = AdjustableClock.Use(start);
            var service = services.GetRequiredService<Application.Procurement.SupplierService>();
            var db = services.GetRequiredService<IAppDbContext>();
            foreach (var s in suppliers)
            {
                var supplier = await service.CreateAsync(new Application.Procurement.SupplierRequest(s.Name, s.Contact, s.Email, "+1 555 0199",
                    null, null, s.Terms, s.Lead, null), ct);
                supplierIds[s.Name] = supplier.Id;
                foreach (var productName in s.Products)
                {
                    var cost = await db.Products.Where(p => p.Id == ids.Products[productName]).Select(p => p.CostPrice).FirstAsync(ct);
                    await service.UpsertProductAsync(supplier.Id, new Application.Procurement.SupplierProductRequest(
                        ids.Products[productName], $"{s.Name[..3].ToUpperInvariant()}-{productName.Length:D3}", decimal.Round(cost * 0.97m, 4),
                        10, s.Lead, IsPreferred: true), ct);
                }
            }
        }, ct);

        // (supplier, product lines, days ago, stage)
        var plans = new (string Supplier, (string Product, decimal Qty)[] Lines, int DaysAgo, string Stage)[]
        {
            ("SafeGuard Industrial Supply", [("Hi-Vis Safety Vest", 200), ("Safety Helmet EN397", 80)], 24, "received-paid"),
            ("PackRight Packaging Co.", [("Corrugated Box 40x30x30", 1500), ("Packing Tape 48mm x 66m", 300)], 20, "received-billed"),
            ("CleanPro Distributors", [("Nitrile Gloves (Box of 100)", 240), ("Hand Sanitizer 1L", 120)], 14, "partial"),
            ("OfficeHub Wholesale", [("A4 Copy Paper (500 sheets)", 400), ("Ballpoint Pens (Box of 50)", 60)], 8, "approved"),
            ("PackRight Packaging Co.", [("Stretch Wrap Film 500mm", 600), ("Bubble Wrap Roll 1m x 50m", 150)], 3, "pending"),
            ("SafeGuard Industrial Supply", [("Steel Toe Boots", 30)], 1, "draft"),
        };

        foreach (var plan in plans)
        {
            var day = DateTimeOffset.UtcNow.Date.AddDays(-plan.DaysAgo);
            var at = (double hours) => new DateTimeOffset(day, TimeSpan.Zero).AddHours(hours);
            Guid poId = Guid.Empty;
            await RunAsAsync(ids, "procurement@acme.test", async services =>
            {
                using var _ = AdjustableClock.Use(at(10));
                var po = await services.GetRequiredService<Application.Procurement.PurchaseOrderService>().CreateAsync(
                    new Application.Procurement.CreatePurchaseOrderRequest(supplierIds[plan.Supplier], ids.Warehouses["CDC"],
                        DateOnly.FromDateTime(day.AddDays(7)), null,
                        plan.Lines.Select(l => new Application.Procurement.PurchaseOrderLineRequest(ids.Products[l.Product], l.Qty)).ToList(),
                        Submit: plan.Stage != "draft"), null, ct);
                poId = po.Id;
            }, ct);

            if (plan.Stage is "draft" or "pending")
            {
                continue;
            }

            await RunAsAsync(ids, "finance@acme.test", async services =>
            {
                using var _ = AdjustableClock.Use(at(14));
                var orders = services.GetRequiredService<Application.Procurement.PurchaseOrderService>();
                if ((await orders.GetAsync(poId, ct)).Status == Domain.Procurement.PurchaseOrderStatus.PendingApproval)
                {
                    await orders.ApproveAsync(poId, ct);
                }
            }, ct);

            if (plan.Stage == "approved")
            {
                continue;
            }

            await RunAsAsync(ids, "whmanager@acme.test", async services =>
            {
                using var _ = AdjustableClock.Use(at(24 * 5 + 9));
                var orders = services.GetRequiredService<Application.Procurement.PurchaseOrderService>();
                var po = await orders.GetAsync(poId, ct);
                var partial = plan.Stage == "partial";
                await orders.ReceiveAsync(poId, new Application.Procurement.ReceiveGoodsRequest(
                    po.Lines.Select(l => new Application.Procurement.ReceiveLineRequest(l.Id, partial ? Math.Floor(l.Quantity * 0.6m) : l.Quantity)).ToList(),
                    $"DN-{plan.DaysAgo:D3}", partial ? "Balance back-ordered by supplier" : null), ct);
            }, ct);

            if (plan.Stage == "partial")
            {
                continue;
            }

            await RunAsAsync(ids, "finance@acme.test", async services =>
            {
                using var _ = AdjustableClock.Use(at(24 * 6 + 11));
                var orders = services.GetRequiredService<Application.Procurement.PurchaseOrderService>();
                var invoices = services.GetRequiredService<Application.Procurement.SupplierInvoiceService>();
                var po = await orders.GetAsync(poId, ct);
                var invoiceDate = DateOnly.FromDateTime(day.AddDays(6));
                var bill = await invoices.CreateAsync(new Application.Procurement.CreateSupplierInvoiceRequest(po.SupplierId, po.Id,
                    $"{plan.Supplier[..3].ToUpperInvariant()}-INV-{plan.DaysAgo:D4}", invoiceDate,
                    plan.Stage == "received-billed" ? invoiceDate.AddDays(10) : null, po.Subtotal, po.TaxTotal, null), ct);
                if (plan.Stage == "received-paid")
                {
                    using var __ = AdjustableClock.Use(at(24 * 12));
                    await invoices.RecordPaymentAsync(bill.Summary.Id, new Application.Billing.RecordPaymentRequest(bill.Summary.Total,
                        Domain.Billing.PaymentMethod.BankTransfer, "WIRE-0001", null), ct);
                }
            }, ct);
        }

        // What the worker's daily job would have done by now: flag unpaid bills and invoices past their due date.
        await RunAsAsync(ids, "finance@acme.test", async services =>
        {
            await services.GetRequiredService<Application.Procurement.SupplierInvoiceService>().MarkOverdueAsync(ct);
            await services.GetRequiredService<Application.Billing.InvoiceService>().MarkOverdueAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Customers and ~20 orders across every lifecycle state, created by the sales officer, approved by the
    /// finance manager and fulfilled by the CDC warehouse manager — through the real services.
    /// </summary>
    private async Task SeedSalesAsync(SeededTenant ids, CancellationToken ct)
    {
        var customers = new (string Name, string Email, string City, string State, int? Terms, decimal? Credit)[]
        {
            ("Brightline Logistics", "ap@brightline.test", "Jersey City", "NJ", 30, null),
            ("Northwind Facilities Management", "invoices@northwind-fm.test", "Stamford", "CT", 45, null),
            ("Harbor City Schools", "procurement@harborschools.test", "Boston", "MA", 30, null),
            ("Summit Health Clinics", "finance@summithealth.test", "Albany", "NY", 30, 6000m),
            ("Metro Office Partners", "orders@metro-office.test", "Brooklyn", "NY", 15, null),
            ("Greenfield Construction", "site@greenfield.test", "Newark", "NJ", 30, null),
            ("Riverside Hospitality Group", "purchasing@riverside.test", "Hoboken", "NJ", 30, null),
        };

        var customerIds = new List<Guid>();
        await RunAsAsync(ids, "sales@acme.test", async services =>
        {
            var service = services.GetRequiredService<Application.Sales.CustomerService>();
            foreach (var c in customers)
            {
                using var _ = AdjustableClock.Use(DateTimeOffset.UtcNow.AddDays(-29));
                var address = new Application.Warehouses.AddressDto($"{100 + customerIds.Count * 17} Commerce Ave", null, c.City, c.State, "07000", "United States");
                customerIds.Add((await service.CreateAsync(new Application.Sales.CustomerRequest(
                    c.Name, c.Email, "+1 555 0100", null, address, address, c.Terms, c.Credit, null), ct)).Id);
            }
        }, ct);

        // (customer, lines, days ago, final stage, warehouse)
        string[] catalog = ["Hi-Vis Safety Vest", "Nitrile Gloves (Box of 100)", "Packing Tape 48mm x 66m", "A4 Copy Paper (500 sheets)",
            "Bottled Water 500ml (24 pack)", "Safety Helmet EN397", "Corrugated Box 40x30x30", "Stretch Wrap Film 500mm", "Industrial Floor Cleaner 5L",
            "Wireless Barcode Scanner", "Ballpoint Pens (Box of 50)", "First Aid Kit (Workplace)"];
        var random = new Random(4242);
        var plans = new List<(int Customer, (string Product, decimal Qty)[] Lines, int DaysAgo, string Stage, string Warehouse)>();
        string[] stages = ["paid", "paid", "delivered", "paid", "delivered", "shipped", "paid", "returned", "delivered", "shipped", "partial",
            "packed", "picking", "processing", "approved", "approved", "pending", "draft", "cancelled", "rejected"];
        for (var i = 0; i < stages.Length; i++)
        {
            var lineCount = random.Next(1, 4);
            var lines = catalog.OrderBy(_ => random.Next()).Take(lineCount)
                .Select(p => (p, (decimal)random.Next(4, 30))).ToArray();
            if (stages[i] is "pending" or "rejected")
            {
                lines = [("Corrugated Box 40x30x30", 650m)]; // Bulk order → manager approval.
            }

            plans.Add((random.Next(customers.Length), lines, 28 - i, stages[i], i is 14 ? "NHB" : "CDC"));
        }

        foreach (var plan in plans)
        {
            try
            {
                await SeedOrderAsync(ids, customerIds[plan.Customer], plan.Lines, plan.DaysAgo, plan.Stage, plan.Warehouse, ct);
            }
            catch (InsufficientStockException)
            {
                // Random demo history may hit an empty shelf; production would reject exactly the same way.
            }
        }
    }

    private async Task SeedOrderAsync(SeededTenant ids, Guid customerId, (string Product, decimal Qty)[] lines, int daysAgo,
        string stage, string warehouse, CancellationToken ct)
    {
        var day = DateTimeOffset.UtcNow.Date.AddDays(-daysAgo);
        var at = (double hours) => new DateTimeOffset(day, TimeSpan.Zero).AddHours(hours);
        Guid orderId = Guid.Empty;

        await RunAsAsync(ids, "sales@acme.test", async services =>
        {
            using var _ = AdjustableClock.Use(at(13));
            var order = await services.GetRequiredService<Application.Sales.SalesOrderService>().CreateAsync(new Application.Sales.CreateOrderRequest(
                customerId, ids.Warehouses[warehouse], DateOnly.FromDateTime(day.AddDays(5)), $"PO-{random4(daysAgo)}", null,
                stage == "paid" ? 5 : 0, null,
                lines.Select(l => new Application.Sales.OrderLineRequest(ids.Products[l.Product], l.Qty)).ToList(),
                Submit: stage != "draft"), null, ct);
            orderId = order.Id;
        }, ct);

        if (stage is "draft" or "pending")
        {
            return;
        }

        if (stage == "rejected")
        {
            await RunAsAsync(ids, "finance@acme.test", async services =>
            {
                using var _ = AdjustableClock.Use(at(15));
                var orders = services.GetRequiredService<Application.Sales.SalesOrderService>();
                if ((await orders.GetAsync(orderId, ct)).Status == Domain.Sales.SalesOrderStatus.PendingApproval)
                {
                    await orders.RejectAsync(orderId, new Application.Sales.ReasonRequest("Pricing not agreed with the customer"), ct);
                }
                else
                {
                    await orders.CancelAsync(orderId, new Application.Sales.ReasonRequest("Customer withdrew the order"), ct);
                }
            }, ct);
            return;
        }

        if (stage == "cancelled")
        {
            await RunAsAsync(ids, "sales@acme.test", async services =>
            {
                using var _ = AdjustableClock.Use(at(16));
                await services.GetRequiredService<Application.Sales.SalesOrderService>()
                    .CancelAsync(orderId, new Application.Sales.ReasonRequest("Duplicate order placed by the customer"), ct);
            }, ct);
            return;
        }

        if (stage == "approved")
        {
            return;
        }

        string[] order = ["processing", "picking", "packed", "partial", "shipped", "delivered", "paid", "returned"];
        var rank = Array.IndexOf(order, stage);
        await RunAsAsync(ids, "whmanager@acme.test", async services =>
        {
            var orders = services.GetRequiredService<Application.Sales.SalesOrderService>();
            using (AdjustableClock.Use(at(24 + 8)))
            {
                await orders.StartProcessingAsync(orderId, ct);
            }

            if (rank < 1)
            {
                return;
            }

            using (AdjustableClock.Use(at(24 + 9)))
            {
                var dto = await orders.StartPickingAsync(orderId, ct);
                if (rank < 2)
                {
                    return;
                }

                var partial = stage == "partial";
                await orders.RecordPickAsync(orderId, new Application.Sales.RecordPickRequest(dto.Lines
                    .Select(l => new Application.Sales.PickLineRequest(l.Id, partial ? Math.Max(1, Math.Floor(l.Quantity / 2)) : l.Quantity)).ToList()), ct);
                await orders.PackAsync(orderId, ct);
            }

            if (rank < 3)
            {
                return;
            }

            using (AdjustableClock.Use(at(24 + 14)))
            {
                await orders.ShipAsync(orderId, new Application.Sales.ShipOrderRequest("UPS Ground", $"1Z{random4(daysAgo)}{random4(daysAgo + 7)}", 2), ct);
            }

            if (rank < 5)
            {
                return;
            }

            using (AdjustableClock.Use(at(72 + 11)))
            {
                var shipped = await orders.GetAsync(orderId, ct);
                foreach (var shipment in shipped.Shipments)
                {
                    await orders.DeliverShipmentAsync(shipment.Id, ct);
                }
            }

            if (stage == "returned")
            {
                using var _ = AdjustableClock.Use(at(120 + 10));
                var dto = await orders.GetAsync(orderId, ct);
                var line = dto.Lines[0];
                await orders.CreateReturnAsync(orderId, new Application.Sales.CreateReturnRequest(
                    [new Application.Sales.ReturnLineRequest(line.Id, Math.Max(1, Math.Floor(line.Quantity / 3)), Restock: true)],
                    "Customer over-ordered; unopened cartons returned"), ct);
            }
        }, ct);

        if (stage == "paid")
        {
            await RunAsAsync(ids, "finance@acme.test", async services =>
            {
                using var _ = AdjustableClock.Use(at(24 * 8));
                var orders = services.GetRequiredService<Application.Sales.SalesOrderService>();
                var invoice = (await orders.GetAsync(orderId, ct)).Invoice;
                if (invoice is not null && invoice.BalanceDue > 0)
                {
                    await services.GetRequiredService<Application.Billing.InvoiceService>().RecordPaymentAsync(invoice.Id,
                        new Application.Billing.RecordPaymentRequest(invoice.BalanceDue, Domain.Billing.PaymentMethod.BankTransfer, $"ACH-{random4(daysAgo)}", null), ct);
                }
            }, ct);
        }

        static string random4(int seed) => (1000 + (seed * 7919 % 9000)).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
