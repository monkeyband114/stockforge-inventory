using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockForge.Application.Security;
using StockForge.Infrastructure.Identity;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    /// <summary>Platform super-administrator created on first start (if both values are set).</summary>
    public string? SuperAdminEmail { get; set; }

    public string? SuperAdminPassword { get; set; }

    /// <summary>Creates two demo organizations with users, catalog, stock and history (development only).</summary>
    public bool DemoData { get; set; }

    public string DemoPassword { get; set; } = "StockForge!2026";
}

/// <summary>
/// Synchronizes role definitions and their permission claims with <see cref="RolePermissionDefaults"/> and
/// creates the configured super-administrator. Idempotent; runs on every start.
/// </summary>
internal sealed class IdentitySeeder(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    IOptions<SeedOptions> options,
    TimeProvider clock,
    ILogger<IdentitySeeder> logger)
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [RoleNames.SuperAdmin] = "Platform administrator: manages organizations and platform jobs.",
        [RoleNames.OrganizationAdmin] = "Full access to the organization, its users and configuration.",
        [RoleNames.InventoryManager] = "Manages products, approves adjustments and transfers stock across all warehouses.",
        [RoleNames.WarehouseManager] = "Runs assigned warehouses: receiving, counts, transfers and fulfillment.",
        [RoleNames.WarehouseStaff] = "Records stock movements, counts and picking/packing in assigned warehouses.",
        [RoleNames.ProcurementOfficer] = "Manages suppliers and raises purchase orders.",
        [RoleNames.SalesOfficer] = "Manages customers and creates sales orders.",
        [RoleNames.FinanceManager] = "Invoices, payments, financial reports and order/PO approvals.",
        [RoleNames.Auditor] = "Read-only access to operations, finance and the audit trail.",
    };

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToListAsync(cancellationToken);
        foreach (var (name, permissions) in RolePermissionDefaults.Map)
        {
            var role = roles.FirstOrDefault(r => r.Name == name);
            if (role is null)
            {
                role = new ApplicationRole(name, Descriptions.GetValueOrDefault(name, name), name == RoleNames.SuperAdmin);
                db.Roles.Add(role);
            }
            else
            {
                role.Description = Descriptions.GetValueOrDefault(name, role.Description ?? name);
            }

            var existing = await db.RoleClaims.Where(c => c.RoleId == role.Id && c.ClaimType == Permissions.ClaimType)
                .ToListAsync(cancellationToken);
            db.RoleClaims.RemoveRange(existing.Where(c => !permissions.Contains(c.ClaimValue)));
            foreach (var permission in permissions.Where(p => existing.All(c => c.ClaimValue != p)))
            {
                db.RoleClaims.Add(new IdentityRoleClaim<Guid> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = permission });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await EnsureSuperAdminAsync(cancellationToken);
    }

    private async Task EnsureSuperAdminAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.SuperAdminEmail) || string.IsNullOrWhiteSpace(seed.SuperAdminPassword))
        {
            return;
        }

        var user = await users.FindByEmailAsync(seed.SuperAdminEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = seed.SuperAdminEmail,
                Email = seed.SuperAdminEmail,
                EmailConfirmed = true,
                FirstName = "Platform",
                LastName = "Administrator",
                CreatedAt = clock.GetUtcNow(),
            };
            user.LockoutEnabled = true;
            (await users.CreateAsync(user, seed.SuperAdminPassword)).EnsureSucceeded();
            logger.LogInformation("Created platform super-administrator account");
        }

        if (!await users.IsInRoleAsync(user, RoleNames.SuperAdmin))
        {
            (await users.AddToRoleAsync(user, RoleNames.SuperAdmin)).EnsureSucceeded();
        }

        _ = cancellationToken;
    }
}
