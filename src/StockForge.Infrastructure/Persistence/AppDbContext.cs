using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StockForge.Application.Abstractions;
using StockForge.Domain.Billing;
using StockForge.Domain.Catalog;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;
using StockForge.Domain.Platform;
using StockForge.Domain.Procurement;
using StockForge.Domain.Sales;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Identity;

namespace StockForge.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ScopeContext scope,
    DomainEventBuffer domainEvents)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    public const string TenantFilter = "Tenant";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<WarehouseAssignment> WarehouseAssignments => Set<WarehouseAssignment>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierProduct> SupplierProducts => Set<SupplierProduct>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<StoredFileContent> StoredFileContents => Set<StoredFileContent>();
    public DbSet<ScheduledJobRun> ScheduledJobRuns => Set<ScheduledJobRun>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Evaluated per query by the tenant query filter (EF parameterizes context members).</summary>
    public Guid? CurrentTenantId => scope.TenantId;

    public ScopeContext Scope => scope;

    public IQueryable<TResult> SqlQuery<TResult>(FormattableString sql) => Database.SqlQuery<TResult>(sql);

    public async Task EnsureInventoryItemsAsync(
        IReadOnlyCollection<(Guid WarehouseId, Guid ProductId)> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var tenantId = scope.TenantId ?? throw new InvalidOperationException("A tenant is required.");
        var ids = keys.Select(_ => Guid.CreateVersion7()).ToArray();
        var warehouseIds = keys.Select(k => k.WarehouseId).ToArray();
        var productIds = keys.Select(k => k.ProductId).ToArray();

        // Concurrent first receipts for the same pair block on the unique index and then no-op, instead of failing.
        await Database.ExecuteSqlAsync($"""
            INSERT INTO inventory_items (id, tenant_id, warehouse_id, product_id, quantity_on_hand, quantity_reserved, average_unit_cost, created_at)
            SELECT k.id, {tenantId}, k.warehouse_id, k.product_id, 0, 0, 0, now()
            FROM unnest({ids}, {warehouseIds}, {productIds}) AS k(id, warehouse_id, product_id)
            ON CONFLICT (tenant_id, warehouse_id, product_id) DO NOTHING
            """, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var events = ChangeTracker.Entries<Entity>()
            .SelectMany(e =>
            {
                var raised = e.Entity.GetDomainEvents().ToList();
                e.Entity.ClearDomainEvents();
                return raised;
            })
            .ToList();

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        domainEvents.Add(events);

        // Outside an explicit transaction the save is the commit: publish now. Otherwise the transaction
        // runner publishes after COMMIT (and discards the events on rollback).
        if (Database.CurrentTransaction is null)
        {
            await domainEvents.FlushAsync(cancellationToken);
        }

        return result;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Every id is a client-generated UUIDv7 (assigned in constructors). Telling EF the keys are never
        // generated (a) makes new children added to tracked aggregates INSERTs rather than UPDATEs, and
        // (b) stops EF propagating a generated principal key into composite (tenant_id, id) keys.
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => !t.IsOwned()))
        {
            if (entityType.FindPrimaryKey() is { Properties: [{ Name: "Id", ClrType: var keyType }] } && keyType == typeof(Guid))
            {
                builder.Entity(entityType.ClrType).Property("Id").ValueGeneratedNever();
            }
        }

        var setFilter = typeof(AppDbContext).GetMethod(nameof(ConfigureTenantOwned), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && !t.IsOwned() && t.BaseType is null)
                     .ToList())
        {
            setFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
        }
    }

    private void ConfigureTenantOwned<TEntity>(ModelBuilder builder) where TEntity : class, ITenantOwned
    {
        var entity = builder.Entity<TEntity>();

        // TenantId is part of the (tenant_id, id) alternate key; EF would otherwise generate a value for it.
        // It must only ever come from the trusted scope (stamped by the auditing interceptor).
        entity.Property(e => e.TenantId).ValueGeneratedNever();
        entity.HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        entity.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>PostgreSQL timestamptz only accepts UTC offsets; normalize every value on the way in.</summary>
    private sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        v => v.ToUniversalTime(), v => v);
}

/// <summary>Domain events captured during SaveChanges, held until the surrounding transaction commits.</summary>
public sealed class DomainEventBuffer(IDomainEventDispatcher dispatcher)
{
    private readonly List<IDomainEvent> _pending = [];

    public void Add(IEnumerable<IDomainEvent> events) => _pending.AddRange(events);

    public void Clear() => _pending.Clear();

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var events = _pending.ToList();
        _pending.Clear();
        await dispatcher.DispatchAsync(events, cancellationToken);
    }
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}

public sealed class NullDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
}
