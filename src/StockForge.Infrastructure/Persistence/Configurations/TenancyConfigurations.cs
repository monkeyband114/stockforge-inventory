using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockForge.Domain.Inventory;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Identity;

namespace StockForge.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.Property(t => t.Name).HasMaxLength(200);
        b.Property(t => t.Slug).HasMaxLength(80);
        b.HasIndex(t => t.Slug).IsUnique();
        b.Property(t => t.ContactEmail).HasMaxLength(256);
        b.Property(t => t.Phone).HasMaxLength(40);
        b.Property(t => t.BaseCurrency).HasMaxLength(3).IsFixedLength();
        b.Property(t => t.TimeZone).HasMaxLength(64);
        b.Property(t => t.SuspensionReason).HasMaxLength(500);
        b.Property(t => t.Version).IsRowVersion();
        b.OwnsOne(t => t.Address, a => a.ConfigureAddress("address"));
        b.Navigation(t => t.Address).IsRequired(false);
        b.OwnsOne(t => t.Settings, s =>
        {
            s.Property(x => x.OrderApprovalAmountThreshold).HasColumnName("settings_order_approval_amount").HasPrecision(18, 2);
            s.Property(x => x.OrderApprovalQuantityThreshold).HasColumnName("settings_order_approval_quantity").HasPrecision(18, 3);
            s.Property(x => x.RequireApprovalForAllOrders).HasColumnName("settings_require_approval_for_all_orders");
            s.Property(x => x.DefaultTaxRate).HasColumnName("settings_default_tax_rate").HasPrecision(6, 5);
            s.Property(x => x.InvoiceDueDays).HasColumnName("settings_invoice_due_days");
            s.Property(x => x.StockAdjustmentRequiresApproval).HasColumnName("settings_adjustment_requires_approval");
            s.Property(x => x.LowStockAlertsEnabled).HasColumnName("settings_low_stock_alerts_enabled");
        });
        b.Navigation(t => t.Settings).IsRequired();
    }
}

internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> b)
    {
        b.ToTable("tenant_memberships");
        b.HasAlternateKey(m => new { m.TenantId, m.Id });
        b.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
        b.HasIndex(m => m.UserId);
        b.Property(m => m.JobTitle).HasMaxLength(120);
        b.Property(m => m.Roles).HasColumnType("text[]");
        b.Property(m => m.GrantedPermissions).HasColumnType("text[]");
        b.Property(m => m.Version).IsRowVersion();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(m => m.WarehouseAssignments)
            .WithOne()
            .HasForeignKey(a => new { a.TenantId, a.MembershipId })
            .HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(m => m.WarehouseAssignments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WarehouseAssignmentConfiguration : IEntityTypeConfiguration<WarehouseAssignment>
{
    public void Configure(EntityTypeBuilder<WarehouseAssignment> b)
    {
        b.ToTable("warehouse_assignments");
        b.HasIndex(a => new { a.TenantId, a.MembershipId, a.WarehouseId }).IsUnique();
        b.TenantReference<WarehouseAssignment, Warehouse>(null, a => new { a.TenantId, a.WarehouseId }, DeleteBehavior.Cascade);
    }
}
