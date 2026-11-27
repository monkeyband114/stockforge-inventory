using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockForge.Domain.Catalog;
using StockForge.Domain.Common;

namespace StockForge.Infrastructure.Persistence.Configurations;

internal static class AddressMapping
{
    public static void ConfigureAddress<TOwner>(this OwnedNavigationBuilder<TOwner, Address> a, string prefix)
        where TOwner : class
    {
        a.Property(x => x.Line1).HasColumnName($"{prefix}_line1").HasMaxLength(200);
        a.Property(x => x.Line2).HasColumnName($"{prefix}_line2").HasMaxLength(200);
        a.Property(x => x.City).HasColumnName($"{prefix}_city").HasMaxLength(100);
        a.Property(x => x.State).HasColumnName($"{prefix}_state").HasMaxLength(100);
        a.Property(x => x.PostalCode).HasColumnName($"{prefix}_postal_code").HasMaxLength(20);
        a.Property(x => x.Country).HasColumnName($"{prefix}_country").HasMaxLength(100);
    }
}

internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> b)
    {
        b.ToTable("units_of_measure");
        b.HasTenantKey();
        b.Property(u => u.Code).HasMaxLength(10);
        b.Property(u => u.Name).HasMaxLength(60);
        b.HasIndex(u => new { u.TenantId, u.Code }).IsUnique();
    }
}

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("product_categories");
        b.HasTenantKey();
        b.Property(c => c.Name).HasMaxLength(120);
        b.Property(c => c.Code).HasMaxLength(6);
        b.Property(c => c.Description).HasMaxLength(1000);
        b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
        b.HasIndex(c => new { c.TenantId, c.Name }).IsUnique();
        b.TenantReference<ProductCategory, ProductCategory>(null, c => new { c.TenantId, c.ParentId });
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", t =>
        {
            t.HasCheckConstraint("ck_products_prices_non_negative", "cost_price >= 0 AND selling_price >= 0");
            t.HasCheckConstraint("ck_products_thresholds_non_negative", "reorder_level >= 0 AND reorder_quantity >= 0");
            t.HasCheckConstraint("ck_products_tax_rate", "tax_rate IS NULL OR (tax_rate >= 0 AND tax_rate <= 1)");
        });
        b.HasTenantKey();
        b.Property(p => p.Sku).HasMaxLength(64);
        b.Property(p => p.Name).HasMaxLength(200);
        b.Property(p => p.Description).HasMaxLength(4000);
        b.Property(p => p.Barcode).HasMaxLength(64);
        b.Property(p => p.SellingPrice).HasPrecision(18, 2);
        b.Property(p => p.TaxRate).HasPrecision(6, 5);
        b.Property(p => p.ReorderLevel).HasPrecision(18, 3);
        b.Property(p => p.ReorderQuantity).HasPrecision(18, 3);
        b.Property(p => p.Version).IsRowVersion();
        b.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique();
        b.HasIndex(p => new { p.TenantId, p.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
        b.HasIndex(p => new { p.TenantId, p.Name });
        b.HasIndex(p => new { p.TenantId, p.CategoryId });

        b.TenantReference(p => p.Category, p => new { p.TenantId, p.CategoryId });
        b.TenantReference(p => p.BaseUnit, p => new { p.TenantId, p.BaseUnitId });
        b.HasMany(p => p.Units)
            .WithOne()
            .HasForeignKey(u => new { u.TenantId, u.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> b)
    {
        b.ToTable("product_units", t => t.HasCheckConstraint("ck_product_units_factor_positive", "conversion_factor > 0"));
        b.Property(u => u.ConversionFactor).HasPrecision(18, 6);
        b.Property(u => u.Barcode).HasMaxLength(64);
        b.HasIndex(u => new { u.TenantId, u.ProductId, u.UnitOfMeasureId }).IsUnique();
        b.HasIndex(u => new { u.TenantId, u.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
        b.TenantReference(u => u.UnitOfMeasure, u => new { u.TenantId, u.UnitOfMeasureId });
    }
}
