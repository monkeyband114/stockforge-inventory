using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockForge.Domain.Catalog;
using StockForge.Domain.Inventory;
using StockForge.Domain.Procurement;

namespace StockForge.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.HasTenantKey();
        b.Property(s => s.Code).HasMaxLength(32);
        b.Property(s => s.Name).HasMaxLength(200);
        b.Property(s => s.ContactName).HasMaxLength(120);
        b.Property(s => s.Email).HasMaxLength(256);
        b.Property(s => s.Phone).HasMaxLength(40);
        b.Property(s => s.TaxNumber).HasMaxLength(64);
        b.Property(s => s.Notes).HasMaxLength(2000);
        b.Property(s => s.Version).IsRowVersion();
        b.HasIndex(s => new { s.TenantId, s.Code }).IsUnique();
        b.HasIndex(s => new { s.TenantId, s.Name });
        b.OwnsOne(s => s.Address, a => a.ConfigureAddress("address"));
    }
}

internal sealed class SupplierProductConfiguration : IEntityTypeConfiguration<SupplierProduct>
{
    public void Configure(EntityTypeBuilder<SupplierProduct> b)
    {
        b.ToTable("supplier_products", t => t.HasCheckConstraint("ck_supplier_products_non_negative", "unit_cost >= 0 AND minimum_order_quantity >= 0"));
        b.Property(p => p.SupplierSku).HasMaxLength(64);
        b.Property(p => p.MinimumOrderQuantity).HasPrecision(18, 3);
        b.HasIndex(p => new { p.TenantId, p.SupplierId, p.ProductId }).IsUnique();
        b.HasIndex(p => new { p.TenantId, p.ProductId }).IsUnique().HasFilter("is_preferred"); // at most one preferred supplier per product
        b.TenantReference<SupplierProduct, Supplier>(null, p => new { p.TenantId, p.SupplierId }, DeleteBehavior.Cascade);
        b.TenantReference<SupplierProduct, Product>(null, p => new { p.TenantId, p.ProductId });
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_orders", t => t.HasCheckConstraint("ck_purchase_orders_totals", "subtotal >= 0 AND tax_total >= 0 AND total >= 0"));
        b.HasTenantKey();
        b.Property(o => o.Number).HasMaxLength(32);
        b.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(o => o.Notes).HasMaxLength(2000);
        b.Property(o => o.RejectionReason).HasMaxLength(1000);
        b.Property(o => o.IdempotencyKey).HasMaxLength(100);
        b.Property(o => o.Subtotal).HasPrecision(18, 2);
        b.Property(o => o.TaxTotal).HasPrecision(18, 2);
        b.Property(o => o.Total).HasPrecision(18, 2);
        b.Property(o => o.Version).IsRowVersion();
        b.Ignore(o => o.ReceivedValue);
        b.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
        b.HasIndex(o => new { o.TenantId, o.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        b.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt });
        b.HasIndex(o => new { o.TenantId, o.SupplierId });
        b.HasIndex(o => new { o.TenantId, o.WarehouseId, o.Status });
        b.TenantReference<PurchaseOrder, Supplier>(null, o => new { o.TenantId, o.SupplierId });
        b.TenantReference<PurchaseOrder, Warehouse>(null, o => new { o.TenantId, o.WarehouseId });
        b.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => new { l.TenantId, l.PurchaseOrderId })
            .HasPrincipalKey(o => new { o.TenantId, o.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("purchase_order_lines", t => t.HasCheckConstraint("ck_po_lines_quantities",
            "quantity > 0 AND quantity_received >= 0 AND quantity_received <= quantity"));
        b.HasTenantKey();
        b.Property(l => l.Sku).HasMaxLength(64);
        b.Property(l => l.ProductName).HasMaxLength(200);
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.Property(l => l.QuantityReceived).HasPrecision(18, 3);
        b.Property(l => l.TaxRate).HasPrecision(6, 5);
        b.Property(l => l.Subtotal).HasPrecision(18, 2);
        b.Property(l => l.TaxAmount).HasPrecision(18, 2);
        b.Property(l => l.Total).HasPrecision(18, 2);
        b.Ignore(l => l.QuantityOutstanding);
        b.HasIndex(l => new { l.TenantId, l.PurchaseOrderId, l.ProductId }).IsUnique();
        b.TenantReference<PurchaseOrderLine, Product>(null, l => new { l.TenantId, l.ProductId });
    }
}

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("goods_receipts");
        b.HasTenantKey();
        b.Property(r => r.Number).HasMaxLength(32);
        b.Property(r => r.DeliveryNote).HasMaxLength(64);
        b.Property(r => r.Notes).HasMaxLength(1000);
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique();
        b.HasIndex(r => new { r.TenantId, r.PurchaseOrderId });
        b.TenantReference<GoodsReceipt, PurchaseOrder>(null, r => new { r.TenantId, r.PurchaseOrderId });
        b.TenantReference<GoodsReceipt, Warehouse>(null, r => new { r.TenantId, r.WarehouseId });
        b.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => new { l.TenantId, l.GoodsReceiptId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> b)
    {
        b.ToTable("goods_receipt_lines", t => t.HasCheckConstraint("ck_grn_lines_quantity_positive", "quantity > 0 AND unit_cost >= 0"));
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.TenantReference<GoodsReceiptLine, PurchaseOrderLine>(null, l => new { l.TenantId, l.PurchaseOrderLineId });
        b.TenantReference<GoodsReceiptLine, Product>(null, l => new { l.TenantId, l.ProductId });
    }
}

internal sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> b)
    {
        b.ToTable("supplier_invoices", t =>
        {
            t.HasCheckConstraint("ck_supplier_invoices_amounts", "total >= 0 AND amount_paid >= 0 AND amount_paid <= total");
            t.HasCheckConstraint("ck_supplier_invoices_dates", "due_date >= invoice_date");
        });
        b.HasTenantKey();
        b.Property(i => i.Reference).HasMaxLength(32);
        b.Property(i => i.SupplierInvoiceNumber).HasMaxLength(64);
        b.Property(i => i.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(i => i.Notes).HasMaxLength(1000);
        b.Property(i => i.Subtotal).HasPrecision(18, 2);
        b.Property(i => i.TaxTotal).HasPrecision(18, 2);
        b.Property(i => i.Total).HasPrecision(18, 2);
        b.Property(i => i.AmountPaid).HasPrecision(18, 2);
        b.Property(i => i.Version).IsRowVersion();
        b.Ignore(i => i.BalanceDue);
        b.HasIndex(i => new { i.TenantId, i.Reference }).IsUnique();
        b.HasIndex(i => new { i.TenantId, i.SupplierId, i.SupplierInvoiceNumber }).IsUnique(); // never book a bill twice
        b.HasIndex(i => new { i.TenantId, i.Status, i.DueDate });
        b.TenantReference<SupplierInvoice, Supplier>(null, i => new { i.TenantId, i.SupplierId });
        b.TenantReference<SupplierInvoice, PurchaseOrder>(null, i => new { i.TenantId, i.PurchaseOrderId });
        b.HasMany(i => i.Payments)
            .WithOne()
            .HasForeignKey(p => new { p.TenantId, p.SupplierInvoiceId })
            .HasPrincipalKey(i => new { i.TenantId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> b)
    {
        b.ToTable("supplier_payments", t => t.HasCheckConstraint("ck_supplier_payments_amount_positive", "amount > 0"));
        b.Property(p => p.Amount).HasPrecision(18, 2);
        b.Property(p => p.Reference).HasMaxLength(100);
        b.HasIndex(p => new { p.TenantId, p.PaidAt });
    }
}
