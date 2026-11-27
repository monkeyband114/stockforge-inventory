namespace StockForge.Application.Abstractions;

/// <summary>A durable background job. <see cref="JobType"/> is the stable name stored in the outbox.</summary>
public interface IJob
{
    static abstract string JobType { get; }
}

public sealed record JobOptions
{
    public DateTimeOffset? NotBefore { get; init; }
    public int MaxAttempts { get; init; } = 8;

    /// <summary>Encrypts the payload at rest and wipes it after successful delivery (e.g. reset links).</summary>
    public bool Sensitive { get; init; }

    public string? DeduplicationKey { get; init; }
}

/// <summary>
/// Enqueues durable jobs. The default implementation writes to the transactional outbox, so a job is
/// persisted if and only if the surrounding business transaction commits. Swappable for Hangfire.
/// </summary>
public interface IJobQueue
{
    void Enqueue<TJob>(TJob job, JobOptions? options = null) where TJob : class, IJob;
}

/// <summary>Executes one job type inside the worker. The job's tenant is already set on the scope.</summary>
public interface IJobHandler<in TJob> where TJob : class, IJob
{
    Task HandleAsync(TJob job, CancellationToken cancellationToken);
}

/// <summary>Who should receive a realtime event. Groups are always derived from the event's tenant.</summary>
public enum RealtimeAudience
{
    /// <summary>Every connected member of the tenant.</summary>
    Tenant = 1,

    /// <summary>Members with access to <see cref="RealtimeMessage.WarehouseId"/>.</summary>
    Warehouse = 2,

    /// <summary>Members holding <see cref="RealtimeMessage.Permission"/>.</summary>
    Permission = 3,
}

public sealed record RealtimeMessage(
    Guid TenantId,
    string Event,
    object Payload,
    RealtimeAudience Audience,
    Guid? WarehouseId = null,
    string? Permission = null);

public static class RealtimeEvents
{
    public const string InventoryUpdated = "inventory.updated";
    public const string LowStock = "inventory.low_stock";
    public const string WarehouseActivity = "warehouse.activity";
    public const string TransferUpdated = "transfer.updated";
    public const string AdjustmentUpdated = "adjustment.updated";
    public const string OrderCreated = "order.created";
    public const string OrderStatusChanged = "order.status_changed";
    public const string ShipmentUpdated = "shipment.updated";
    public const string Notification = "notification";
}

/// <summary>Pushes events to connected clients (SignalR over a Redis backplane in production).</summary>
public interface IRealtimeNotifier
{
    Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken);
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null, IReadOnlyList<EmailAttachment>? Attachments = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
