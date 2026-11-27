using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockForge.Domain.Billing;
using StockForge.Domain.Catalog;
using StockForge.Domain.Inventory;
using StockForge.Domain.Sales;

namespace StockForge.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers", t => t.HasCheckConstraint("ck_customers_credit_limit", "credit_limit IS NULL OR credit_limit >= 0"));
        b.HasTenantKey();
        b.Property(c => c.Code).HasMaxLength(32);
        b.Property(c => c.Name).HasMaxLength(200);
        b.Property(c => c.Email).HasMaxLength(256);
        b.Property(c => c.Phone).HasMaxLength(40);
        b.Property(c => c.TaxNumber).HasMaxLength(64);
        b.Property(c => c.Notes).HasMaxLength(2000);
        b.Property(c => c.CreditLimit).HasPrecision(18, 2);
        b.Property(c => c.Version).IsRowVersion();
        b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
        b.HasIndex(c => new { c.TenantId, c.Name });
        b.OwnsOne(c => c.BillingAddress, a => a.ConfigureAddress("billing"));
        b.OwnsOne(c => c.ShippingAddress, a => a.ConfigureAddress("shipping"));
    }
}

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("sales_orders", t =>
        {
            t.HasCheckConstraint("ck_sales_orders_totals_non_negative", "subtotal >= 0 AND discount_total >= 0 AND tax_total >= 0 AND total >= 0");
            t.HasCheckConstraint("ck_sales_orders_discount", "order_discount_percent >= 0 AND order_discount_percent <= 100");
        });
        b.HasTenantKey();
        b.Property(o => o.Number).HasMaxLength(32);
        b.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(o => o.CustomerReference).HasMaxLength(64);
        b.Property(o => o.Notes).HasMaxLength(2000);
        b.Property(o => o.ApprovalReason).HasMaxLength(500);
        b.Property(o => o.RejectionReason).HasMaxLength(1000);
        b.Property(o => o.CancellationReason).HasMaxLength(1000);
        b.Property(o => o.IdempotencyKey).HasMaxLength(100);
        b.Property(o => o.OrderDiscountPercent).HasPrecision(5, 2);
        b.Property(o => o.Subtotal).HasPrecision(18, 2);
        b.Property(o => o.DiscountTotal).HasPrecision(18, 2);
        b.Property(o => o.TaxTotal).HasPrecision(18, 2);
        b.Property(o => o.Total).HasPrecision(18, 2);
        b.Property(o => o.TotalQuantity).HasPrecision(18, 3);
        b.Property(o => o.Version).IsRowVersion();
        b.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
        b.HasIndex(o => new { o.TenantId, o.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        b.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt });
        b.HasIndex(o => new { o.TenantId, o.CustomerId });
        b.HasIndex(o => new { o.TenantId, o.WarehouseId, o.Status });
        b.TenantReference<SalesOrder, Customer>(null, o => new { o.TenantId, o.CustomerId });
        b.TenantReference<SalesOrder, Warehouse>(null, o => new { o.TenantId, o.WarehouseId });
        b.OwnsOne(o => o.ShippingAddress, a => a.ConfigureAddress("ship_to"));
        b.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => new { l.TenantId, l.OrderId })
            .HasPrincipalKey(o => new { o.TenantId, o.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(o => o.HasShipments);
        b.Ignore(o => o.IsFullyShipped);
        b.Ignore(o => o.HoldsReservation);
    }
}

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> b)
    {
        // Fulfillment can never run ahead of the order: shipped ≤ packed ≤ picked ≤ ordered, returned ≤ shipped.
        b.ToTable("sales_order_lines", t =>
        {
            t.HasCheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_order_lines_fulfillment_order",
                "quantity_picked >= 0 AND quantity_picked <= quantity AND quantity_packed <= quantity_picked AND quantity_shipped <= quantity_packed AND quantity_returned <= quantity_shipped AND quantity_returned >= 0");
        });
        b.HasTenantKey();
        b.Property(l => l.Sku).HasMaxLength(64);
        b.Property(l => l.ProductName).HasMaxLength(200);
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.Property(l => l.UnitPrice).HasPrecision(18, 2);
        b.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        b.Property(l => l.TaxRate).HasPrecision(6, 5);
        b.Property(l => l.Subtotal).HasPrecision(18, 2);
        b.Property(l => l.DiscountAmount).HasPrecision(18, 2);
        b.Property(l => l.TaxAmount).HasPrecision(18, 2);
        b.Property(l => l.Total).HasPrecision(18, 2);
        b.Property(l => l.QuantityPicked).HasPrecision(18, 3);
        b.Property(l => l.QuantityPacked).HasPrecision(18, 3);
        b.Property(l => l.QuantityShipped).HasPrecision(18, 3);
        b.Property(l => l.QuantityReturned).HasPrecision(18, 3);
        b.Ignore(l => l.NetUnitPriceInclTax);
        b.HasIndex(l => new { l.TenantId, l.OrderId, l.ProductId }).IsUnique();
        b.TenantReference<SalesOrderLine, Product>(null, l => new { l.TenantId, l.ProductId });
    }
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("shipments", t => t.HasCheckConstraint("ck_shipments_packages", "package_count > 0"));
        b.HasTenantKey();
        b.Property(s => s.Number).HasMaxLength(32);
        b.Property(s => s.Carrier).HasMaxLength(100);
        b.Property(s => s.TrackingNumber).HasMaxLength(100);
        b.Property(s => s.Version).IsRowVersion();
        b.HasIndex(s => new { s.TenantId, s.Number }).IsUnique();
        b.HasIndex(s => new { s.TenantId, s.SalesOrderId });
        b.TenantReference<Shipment, SalesOrder>(null, s => new { s.TenantId, s.SalesOrderId });
        b.TenantReference<Shipment, Warehouse>(null, s => new { s.TenantId, s.WarehouseId });
        b.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey(l => new { l.TenantId, l.ShipmentId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> b)
    {
        b.ToTable("shipment_lines", t => t.HasCheckConstraint("ck_shipment_lines_quantity_positive", "quantity > 0"));
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.TenantReference<ShipmentLine, SalesOrderLine>(null, l => new { l.TenantId, l.SalesOrderLineId });
        b.TenantReference<ShipmentLine, Product>(null, l => new { l.TenantId, l.ProductId });
    }
}

internal sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> b)
    {
        b.ToTable("sales_returns");
        b.HasTenantKey();
        b.Property(r => r.Number).HasMaxLength(32);
        b.Property(r => r.Reason).HasMaxLength(1000);
        b.Property(r => r.CreditAmount).HasPrecision(18, 2);
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique();
        b.HasIndex(r => new { r.TenantId, r.SalesOrderId });
        b.TenantReference<SalesReturn, SalesOrder>(null, r => new { r.TenantId, r.SalesOrderId });
        b.TenantReference<SalesReturn, Warehouse>(null, r => new { r.TenantId, r.WarehouseId });
        b.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => new { l.TenantId, l.ReturnId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnLine> b)
    {
        b.ToTable("sales_return_lines", t => t.HasCheckConstraint("ck_return_lines_quantity_positive", "quantity > 0 AND credit_amount >= 0"));
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.Property(l => l.CreditAmount).HasPrecision(18, 2);
        b.TenantReference<SalesReturnLine, SalesOrderLine>(null, l => new { l.TenantId, l.SalesOrderLineId });
        b.TenantReference<SalesReturnLine, Product>(null, l => new { l.TenantId, l.ProductId });
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices", t =>
        {
            t.HasCheckConstraint("ck_invoices_amounts", "total >= 0 AND amount_paid >= 0 AND credited_amount >= 0 AND amount_paid + credited_amount <= total");
            t.HasCheckConstraint("ck_invoices_dates", "due_date >= issue_date");
        });
        b.HasTenantKey();
        b.Property(i => i.Number).HasMaxLength(32);
        b.Property(i => i.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(i => i.BillingName).HasMaxLength(200);
        b.Property(i => i.BillingEmail).HasMaxLength(256);
        b.Property(i => i.BillingTaxNumber).HasMaxLength(64);
        b.Property(i => i.Subtotal).HasPrecision(18, 2);
        b.Property(i => i.DiscountTotal).HasPrecision(18, 2);
        b.Property(i => i.TaxTotal).HasPrecision(18, 2);
        b.Property(i => i.Total).HasPrecision(18, 2);
        b.Property(i => i.AmountPaid).HasPrecision(18, 2);
        b.Property(i => i.CreditedAmount).HasPrecision(18, 2);
        b.Property(i => i.Version).IsRowVersion();
        b.Ignore(i => i.BalanceDue);

        // One invoice per order: a retried or concurrent approval can never bill twice.
        b.HasIndex(i => new { i.TenantId, i.SalesOrderId }).IsUnique().HasFilter("sales_order_id IS NOT NULL");
        b.HasIndex(i => new { i.TenantId, i.Number }).IsUnique();
        b.HasIndex(i => new { i.TenantId, i.Status, i.DueDate });
        b.HasIndex(i => new { i.TenantId, i.CustomerId });
        b.TenantReference<Invoice, SalesOrder>(null, i => new { i.TenantId, i.SalesOrderId });
        b.TenantReference<Invoice, Customer>(null, i => new { i.TenantId, i.CustomerId });
        b.OwnsOne(i => i.BillingAddress, a => a.ConfigureAddress("billing"));
        b.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => new { l.TenantId, l.InvoiceId })
            .HasPrincipalKey(i => new { i.TenantId, i.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasMany(i => i.Payments)
            .WithOne()
            .HasForeignKey(p => new { p.TenantId, p.InvoiceId })
            .HasPrincipalKey(i => new { i.TenantId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("invoice_lines");
        b.Property(l => l.Sku).HasMaxLength(64);
        b.Property(l => l.Description).HasMaxLength(200);
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.Property(l => l.UnitPrice).HasPrecision(18, 2);
        b.Property(l => l.DiscountAmount).HasPrecision(18, 2);
        b.Property(l => l.TaxRate).HasPrecision(6, 5);
        b.Property(l => l.TaxAmount).HasPrecision(18, 2);
        b.Property(l => l.Total).HasPrecision(18, 2);
        b.HasIndex(l => new { l.TenantId, l.InvoiceId, l.LineNumber }).IsUnique();
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", t => t.HasCheckConstraint("ck_payments_amount_positive", "amount > 0"));
        b.Property(p => p.Amount).HasPrecision(18, 2);
        b.Property(p => p.Reference).HasMaxLength(100);
        b.HasIndex(p => new { p.TenantId, p.PaidAt });
        b.HasIndex(p => new { p.TenantId, p.InvoiceId });
    }
}
