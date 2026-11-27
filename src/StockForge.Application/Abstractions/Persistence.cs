using Microsoft.EntityFrameworkCore;
using StockForge.Domain.Billing;
using StockForge.Domain.Catalog;
using StockForge.Domain.Inventory;
using StockForge.Domain.Platform;
using StockForge.Domain.Sales;
using StockForge.Domain.Tenancy;

namespace StockForge.Application.Abstractions;

/// <summary>
/// The application's view of the database. EF Core already implements repository + unit of work, so the
/// application layer uses <see cref="DbSet{TEntity}"/> directly instead of wrapping it in generic repositories.
/// Tenant-owned sets are automatically filtered to the current tenant.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantMembership> TenantMemberships { get; }
    DbSet<WarehouseAssignment> WarehouseAssignments { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductUnit> ProductUnits { get; }
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockReservation> StockReservations { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<Customer> Customers { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<SalesReturn> SalesReturns { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Payment> Payments { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<StoredFile> StoredFiles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure balance rows exist for the given (warehouse, product) pairs using
    /// <c>INSERT … ON CONFLICT DO NOTHING</c>, so concurrent first receipts never collide.
    /// </summary>
    Task EnsureInventoryItemsAsync(IReadOnlyCollection<(Guid WarehouseId, Guid ProductId)> keys, CancellationToken cancellationToken);

    /// <summary>
    /// Parameterized raw SQL for analytics. Global query filters do NOT apply, so every query must filter by
    /// tenant explicitly; PostgreSQL row-level security remains in force as a safety net.
    /// </summary>
    IQueryable<TResult> SqlQuery<TResult>(FormattableString sql);
}

/// <summary>
/// Runs a use case inside a database transaction with the platform's retry policy: transient errors
/// (deadlocks, serialization failures, dropped connections) and optimistic-concurrency conflicts re-run the
/// whole operation against fresh data. Domain events are published only after a successful commit.
/// </summary>
public interface ITransactionRunner
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}

/// <summary>Gap-free per-tenant counters (allocated inside the caller's transaction).</summary>
public interface ISequenceGenerator
{
    Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken);
}

public static class SequenceNames
{
    public const string Sku = "sku";
    public const string Barcode = "barcode";
    public const string StockTransfer = "stock_transfer";
    public const string StockAdjustment = "stock_adjustment";
    public const string SalesOrder = "sales_order";
    public const string Invoice = "invoice";
    public const string Shipment = "shipment";
    public const string PurchaseOrder = "purchase_order";
    public const string GoodsReceipt = "goods_receipt";
    public const string Customer = "customer";
    public const string SalesReturn = "sales_return";
}

/// <summary>Writes explicit audit entries (logins, approvals, security events) in the current unit of work.</summary>
public interface IAuditTrail
{
    void Record(string action, string entityType, string? entityId, object? details = null);
}

public sealed record StoredFileData(Guid Id, string FileName, string ContentType, byte[] Content);

/// <summary>Binary storage for product images and generated documents (PostgreSQL today, S3-compatible later).</summary>
public interface IFileStorage
{
    /// <summary>Stages a file in the current unit of work; it is persisted by the next SaveChanges.</summary>
    Guid Stage(string fileName, string contentType, byte[] content, string category);

    Task<StoredFileData?> GetAsync(Guid fileId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid fileId, CancellationToken cancellationToken);
}
