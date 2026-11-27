using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockForge.Application.Abstractions;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;
using StockForge.Domain.Platform;
using StockForge.Domain.Tenancy;

namespace StockForge.Infrastructure.Persistence.Interceptors;

/// <summary>Raised when code attempts to write a row belonging to another tenant (or without a tenant).</summary>
public sealed class TenantIsolationException(string message) : InvalidOperationException(message);

/// <summary>
/// Runs on every SaveChanges:
/// <list type="number">
/// <item>stamps <c>TenantId</c> on new tenant-owned rows from the trusted scope and rejects cross-tenant writes;</item>
/// <item>stamps created/updated timestamps and users;</item>
/// <item>rejects updates/deletes of append-only rows (ledger, audit log);</item>
/// <item>writes an audit row (old/new values, sensitive fields excluded) for every <see cref="IAuditable"/> change.</item>
/// </list>
/// Audit rows are added to the same unit of work, so they commit or roll back with the change itself.
/// </summary>
internal sealed class AuditingInterceptor(ScopeContext scope, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal)
    {
        "Version", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "PasswordHash", "SecurityStamp",
        "ConcurrencyStamp", "TokenHash", "Payload", "Content", "Data",
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            Process(context);
        }

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Process(context);
        }

        return result;
    }

    private void Process(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var audits = new List<AuditLog>();
        foreach (var entry in entries)
        {
            if (entry.Entity is StockMovement or AuditLog && entry.State != EntityState.Added)
            {
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} rows are append-only.");
            }

            if (entry.Entity is ITenantOwned)
            {
                EnforceTenant(entry);
            }

            if (entry.Entity is AuditableEntity)
            {
                Stamp(entry, now);
            }

            if (entry.Entity is IAuditable && !entry.Metadata.IsOwned())
            {
                audits.Add(CreateAudit(context, entry, now));
            }
        }

        // Owned values (addresses, tenant settings) changed while the owner row itself is unchanged.
        foreach (var owner in OwnersOfChangedOwnedEntries(context, entries))
        {
            audits.Add(CreateAudit(context, owner, now));
        }

        if (audits.Count > 0)
        {
            context.Set<AuditLog>().AddRange(audits);
        }
    }

    private void EnforceTenant(EntityEntry entry)
    {
        var property = entry.Property(nameof(ITenantOwned.TenantId));
        var current = scope.TenantId;
        if (entry.State == EntityState.Added)
        {
            var value = (Guid)property.CurrentValue!;
            if (value == Guid.Empty)
            {
                property.CurrentValue = current
                    ?? throw new TenantIsolationException($"Cannot create {entry.Metadata.ClrType.Name} without a tenant.");
            }
            else if (value != current)
            {
                throw new TenantIsolationException($"Cannot create {entry.Metadata.ClrType.Name} for another tenant (row {value}, scope {current?.ToString() ?? "none"}).");
            }

            return;
        }

        if (property.IsModified)
        {
            throw new TenantIsolationException("TenantId cannot be changed.");
        }

        if ((Guid)property.OriginalValue! != current)
        {
            throw new TenantIsolationException($"Cannot modify {entry.Metadata.ClrType.Name} of another tenant.");
        }
    }

    private void Stamp(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(AuditableEntity.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(AuditableEntity.CreatedBy)).CurrentValue = scope.UserId;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Property(nameof(AuditableEntity.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(AuditableEntity.UpdatedBy)).CurrentValue = scope.UserId;
        }
    }

    private AuditLog CreateAudit(DbContext context, EntityEntry entry, DateTimeOffset now)
    {
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        var changed = new List<string>();

        Collect(entry, prefix: null, oldValues, newValues, changed);
        foreach (var owned in OwnedEntriesOf(context, entry))
        {
            var navigation = owned.Metadata.FindOwnership()!.PrincipalToDependent!.Name;
            Collect(owned, navigation, oldValues, newValues, changed);
        }

        var state = entry.State == EntityState.Unchanged ? EntityState.Modified : entry.State;
        var action = state switch
        {
            EntityState.Added => "Created",
            EntityState.Deleted => "Deleted",
            _ => "Updated",
        };

        Guid? tenantId = entry.Entity switch
        {
            ITenantOwned owned => owned.TenantId == Guid.Empty ? scope.TenantId : owned.TenantId,
            Tenant tenant => tenant.Id,
            _ => scope.TenantId,
        };

        return new AuditLog(
            tenantId,
            scope.UserId,
            scope.Email,
            action,
            entry.Metadata.ClrType.Name,
            entry.Property("Id").CurrentValue?.ToString(),
            state == EntityState.Added ? null : Serialize(oldValues),
            state == EntityState.Deleted ? null : Serialize(newValues),
            state == EntityState.Modified ? changed : null,
            scope.IpAddress,
            scope.UserAgent,
            scope.CorrelationId,
            now);
    }

    private static void Collect(EntityEntry entry, string? prefix, Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues, List<string> changed)
    {
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (ExcludedProperties.Contains(name) || property.Metadata.IsShadowProperty() || property.Metadata.IsKey() && prefix is not null)
            {
                continue;
            }

            var key = CamelCase(prefix is null ? name : $"{prefix}.{name}");
            switch (entry.State)
            {
                case EntityState.Added:
                    newValues[key] = property.CurrentValue;
                    break;
                case EntityState.Deleted:
                    oldValues[key] = property.OriginalValue;
                    break;
                case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                    oldValues[key] = property.OriginalValue;
                    newValues[key] = property.CurrentValue;
                    changed.Add(key);
                    break;
            }
        }
    }

    private static IEnumerable<EntityEntry> OwnedEntriesOf(DbContext context, EntityEntry owner) =>
        context.ChangeTracker.Entries()
            .Where(e => e.Metadata.IsOwned() && e.State != EntityState.Unchanged && e.State != EntityState.Detached)
            .Where(e => IsOwnedBy(e, owner));

    private static IEnumerable<EntityEntry> OwnersOfChangedOwnedEntries(DbContext context, IReadOnlyCollection<EntityEntry> entries) =>
        entries
            .Where(e => e.Metadata.IsOwned())
            .Select(owned => context.ChangeTracker.Entries().FirstOrDefault(o =>
                o.State == EntityState.Unchanged && o.Entity is IAuditable && IsOwnedBy(owned, o)))
            .OfType<EntityEntry>()
            .Distinct();

    private static bool IsOwnedBy(EntityEntry owned, EntityEntry owner)
    {
        var ownership = owned.Metadata.FindOwnership();
        if (ownership is null || ownership.PrincipalEntityType.ClrType != owner.Metadata.ClrType)
        {
            return false;
        }

        var ownedKey = ownership.Properties.Select(p => owned.Property(p.Name).CurrentValue);
        var ownerKey = ownership.PrincipalKey.Properties.Select(p => owner.Property(p.Name).CurrentValue);
        return ownedKey.SequenceEqual(ownerKey);
    }

    // Matches the API's JSON casing: "settings.defaultTaxRate".
    private static string CamelCase(string path) =>
        string.Join('.', path.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));

    private static string? Serialize(Dictionary<string, object?> values) =>
        values.Count == 0 ? null : JsonSerializer.Serialize(values, Json);
}
