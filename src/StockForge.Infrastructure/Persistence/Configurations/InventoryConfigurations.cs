using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockForge.Domain.Catalog;
using StockForge.Domain.Inventory;

namespace StockForge.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses");
        b.HasTenantKey();
        b.Property(w => w.Code).HasMaxLength(20);
        b.Property(w => w.Name).HasMaxLength(120);
        b.Property(w => w.Phone).HasMaxLength(40);
        b.Property(w => w.Email).HasMaxLength(256);
        b.Property(w => w.Version).IsRowVersion();
        b.HasIndex(w => new { w.TenantId, w.Code }).IsUnique();
        b.OwnsOne(w => w.Address, a => a.ConfigureAddress("address"));
        b.Navigation(w => w.Address).IsRequired(false);
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        // The CHECK constraints are the last line of defence against overselling: even a buggy code path
        // cannot persist a negative or over-reserved balance.
        b.ToTable("inventory_items", t =>
        {
            t.HasCheckConstraint("ck_inventory_on_hand_non_negative", "quantity_on_hand >= 0");
            t.HasCheckConstraint("ck_inventory_reserved_non_negative", "quantity_reserved >= 0");
            t.HasCheckConstraint("ck_inventory_reserved_within_on_hand", "quantity_reserved <= quantity_on_hand");
        });
        b.HasTenantKey();
        b.Property(i => i.QuantityOnHand).HasPrecision(18, 3);
        b.Property(i => i.QuantityReserved).HasPrecision(18, 3);
        b.Property(i => i.Version).IsRowVersion();
        b.Ignore(i => i.QuantityAvailable);
        b.HasIndex(i => new { i.TenantId, i.WarehouseId, i.ProductId }).IsUnique();
        b.HasIndex(i => new { i.TenantId, i.ProductId });
        b.TenantReference(i => i.Warehouse, i => new { i.TenantId, i.WarehouseId });
        b.TenantReference(i => i.Product, i => new { i.TenantId, i.ProductId });
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements");
        b.Property(m => m.QuantityDelta).HasPrecision(18, 3);
        b.Property(m => m.ReservedDelta).HasPrecision(18, 3);
        b.Property(m => m.OnHandAfter).HasPrecision(18, 3);
        b.Property(m => m.ReservedAfter).HasPrecision(18, 3);
        b.Property(m => m.ReferenceNumber).HasMaxLength(64);
        b.Property(m => m.Reason).HasMaxLength(500);
        b.Property(m => m.CorrelationId).HasMaxLength(64);
        b.HasIndex(m => new { m.TenantId, m.WarehouseId, m.CreatedAt });
        b.HasIndex(m => new { m.TenantId, m.ProductId, m.CreatedAt });
        b.HasIndex(m => new { m.TenantId, m.ReferenceType, m.ReferenceId });
        b.HasIndex(m => new { m.TenantId, m.CreatedAt });
        b.TenantReference<StockMovement, InventoryItem>(null, m => new { m.TenantId, m.InventoryItemId });
        b.TenantReference<StockMovement, Warehouse>(null, m => new { m.TenantId, m.WarehouseId });
        b.TenantReference<StockMovement, Product>(null, m => new { m.TenantId, m.ProductId });
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> b)
    {
        b.ToTable("stock_reservations", t =>
        {
            t.HasCheckConstraint("ck_reservations_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_reservations_within_quantity", "quantity_consumed >= 0 AND quantity_released >= 0 AND quantity_consumed + quantity_released <= quantity");
        });
        b.Property(r => r.Quantity).HasPrecision(18, 3);
        b.Property(r => r.QuantityConsumed).HasPrecision(18, 3);
        b.Property(r => r.QuantityReleased).HasPrecision(18, 3);
        b.Property(r => r.ReferenceNumber).HasMaxLength(64);
        b.Property(r => r.Notes).HasMaxLength(500);
        b.Property(r => r.Version).IsRowVersion();
        b.Ignore(r => r.RemainingQuantity);
        b.HasIndex(r => new { r.TenantId, r.ReferenceType, r.ReferenceId });
        b.HasIndex(r => new { r.TenantId, r.WarehouseId, r.ProductId, r.Status });
        b.HasIndex(r => new { r.TenantId, r.Status, r.ExpiresAt });
        b.TenantReference<StockReservation, Warehouse>(null, r => new { r.TenantId, r.WarehouseId });
        b.TenantReference<StockReservation, Product>(null, r => new { r.TenantId, r.ProductId });
    }
}

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("stock_adjustments");
        b.Property(a => a.Number).HasMaxLength(32);
        b.Property(a => a.QuantityChange).HasPrecision(18, 3);
        b.Property(a => a.SystemQuantity).HasPrecision(18, 3);
        b.Property(a => a.CountedQuantity).HasPrecision(18, 3);
        b.Property(a => a.Notes).HasMaxLength(1000);
        b.Property(a => a.ReviewNotes).HasMaxLength(1000);
        b.Property(a => a.Version).IsRowVersion();
        b.HasIndex(a => new { a.TenantId, a.Number }).IsUnique();
        b.HasIndex(a => new { a.TenantId, a.Status, a.RequestedAt });
        b.TenantReference<StockAdjustment, Warehouse>(null, a => new { a.TenantId, a.WarehouseId });
        b.TenantReference<StockAdjustment, Product>(null, a => new { a.TenantId, a.ProductId });
    }
}

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("stock_transfers", t =>
            t.HasCheckConstraint("ck_transfers_distinct_warehouses", "source_warehouse_id <> destination_warehouse_id"));
        b.HasTenantKey();
        b.Property(t => t.Number).HasMaxLength(32);
        b.Property(t => t.Notes).HasMaxLength(1000);
        b.Property(t => t.Version).IsRowVersion();
        b.HasIndex(t => new { t.TenantId, t.Number }).IsUnique();
        b.HasIndex(t => new { t.TenantId, t.Status, t.RequestedAt });
        b.TenantReference<StockTransfer, Warehouse>(null, t => new { t.TenantId, t.SourceWarehouseId });
        b.TenantReference<StockTransfer, Warehouse>(null, t => new { t.TenantId, t.DestinationWarehouseId });
        b.HasMany(t => t.Lines)
            .WithOne()
            .HasForeignKey(l => new { l.TenantId, l.TransferId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(t => t.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> b)
    {
        b.ToTable("stock_transfer_lines", t => t.HasCheckConstraint("ck_transfer_lines_quantity_positive", "quantity > 0"));
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.HasIndex(l => new { l.TenantId, l.TransferId, l.ProductId }).IsUnique();
        b.TenantReference<StockTransferLine, Product>(null, l => new { l.TenantId, l.ProductId });
    }
}
