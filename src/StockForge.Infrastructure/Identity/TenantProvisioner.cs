using System.Text;
using Microsoft.EntityFrameworkCore;
using StockForge.Domain.Catalog;
using StockForge.Domain.Inventory;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Identity;

/// <summary>Creates an organization with sensible defaults (units of measure and a main warehouse).</summary>
internal sealed class TenantProvisioner(AppDbContext db)
{
    public async Task<Tenant> PrepareAsync(string name, string contactEmail, string? baseCurrency, string? timeZone, CancellationToken cancellationToken)
    {
        var slug = await UniqueSlugAsync(name, cancellationToken);
        return new Tenant(name, slug, contactEmail, string.IsNullOrWhiteSpace(baseCurrency) ? "USD" : baseCurrency,
            string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone);
    }

    /// <summary>Adds the tenant and its defaults to the unit of work. The scope must already be inside the tenant.</summary>
    public void Provision(Tenant tenant, bool createDefaultWarehouse = true)
    {
        db.Tenants.Add(tenant);
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure("EA", "Each", false),
            new UnitOfMeasure("BOX", "Box", false),
            new UnitOfMeasure("CS", "Case", false),
            new UnitOfMeasure("PK", "Pack", false),
            new UnitOfMeasure("KG", "Kilogram", true),
            new UnitOfMeasure("L", "Litre", true),
            new UnitOfMeasure("M", "Metre", true));
        if (createDefaultWarehouse)
        {
            db.Warehouses.Add(new Warehouse("MAIN", "Main Warehouse", WarehouseType.Warehouse, null, null, null));
        }
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        for (var i = 2; await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken); i++)
        {
            slug = $"{baseSlug}-{i}";
        }

        return slug;
    }

    internal static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 60)
        {
            slug = slug[..60].Trim('-');
        }

        return slug.Length == 0 ? "org" : slug;
    }
}
