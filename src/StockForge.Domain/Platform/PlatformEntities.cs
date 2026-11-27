using StockForge.Domain.Common;

namespace StockForge.Domain.Platform;

/// <summary>Immutable record of a security-relevant or business-critical action.</summary>
public sealed class AuditLog : Entity
{
    private AuditLog() { }

    public AuditLog(
        Guid? tenantId,
        Guid? userId,
        string? userEmail,
        string action,
        string entityType,
        string? entityId,
        string? oldValues,
        string? newValues,
        IReadOnlyCollection<string>? changedProperties,
        string? ipAddress,
        string? userAgent,
        string? correlationId,
        DateTimeOffset timestamp)
    {
        TenantId = tenantId;
        UserId = userId;
        UserEmail = userEmail;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OldValues = oldValues;
        NewValues = newValues;
        ChangedProperties = changedProperties?.ToList();
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent;
        CorrelationId = correlationId;
        Timestamp = timestamp;
    }

    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }

    /// <summary>JSON object of previous values (sensitive fields removed).</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON object of new values (sensitive fields removed).</summary>
    public string? NewValues { get; private set; }

    public List<string>? ChangedProperties { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
}

public enum OutboxStatus
{
    Pending = 1,
    Processing = 2,
    Succeeded = 3,
    Failed = 4,
}

/// <summary>
/// Durable background job / integration message written in the same transaction as the business change
/// (transactional outbox). Consumed by the worker with <c>FOR UPDATE SKIP LOCKED</c>.
/// </summary>
public sealed class OutboxMessage : Entity
{
    private OutboxMessage() { }

    public OutboxMessage(
        Guid? tenantId,
        string type,
        string payload,
        bool isPayloadProtected,
        int maxAttempts,
        DateTimeOffset createdAt,
        DateTimeOffset? notBefore,
        string? correlationId,
        string? deduplicationKey)
    {
        TenantId = tenantId;
        Type = type;
        Payload = payload;
        IsPayloadProtected = isPayloadProtected;
        MaxAttempts = maxAttempts;
        CreatedAt = createdAt;
        NextAttemptAt = notBefore ?? createdAt;
        CorrelationId = correlationId;
        DeduplicationKey = deduplicationKey;
        Status = OutboxStatus.Pending;
    }

    public Guid? TenantId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public bool IsPayloadProtected { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? DeduplicationKey { get; private set; }

    public void MarkSucceeded(DateTimeOffset at)
    {
        Status = OutboxStatus.Succeeded;
        ProcessedAt = at;
        LockedUntil = null;
        LockedBy = null;
        LastError = null;
        if (IsPayloadProtected)
        {
            // Sensitive payloads (e.g. password-reset links) are not kept once delivered.
            Payload = "{}";
        }
    }

    /// <summary>Records a failed attempt; schedules a retry or marks the message as permanently failed.</summary>
    public void MarkAttemptFailed(string error, DateTimeOffset at, TimeSpan retryDelay)
    {
        Attempts++;
        LastError = error.Length > 4000 ? error[..4000] : error;
        LockedUntil = null;
        LockedBy = null;
        if (Attempts >= MaxAttempts)
        {
            Status = OutboxStatus.Failed;
            ProcessedAt = at;
        }
        else
        {
            Status = OutboxStatus.Pending;
            NextAttemptAt = at + retryDelay;
        }
    }

    /// <summary>Manually re-queues a failed message (failed-job monitoring).</summary>
    public void Requeue(DateTimeOffset at)
    {
        if (Status != OutboxStatus.Failed)
        {
            throw new InvalidStateTransitionException("Job", Status.ToString(), OutboxStatus.Pending.ToString());
        }

        Status = OutboxStatus.Pending;
        Attempts = 0;
        NextAttemptAt = at;
        ProcessedAt = null;
    }
}

public enum IdempotencyStatus
{
    InProgress = 1,
    Completed = 2,
}

/// <summary>Stores the outcome of a request made with an <c>Idempotency-Key</c> so retries replay it.</summary>
public sealed class IdempotencyRecord : Entity, ITenantOwned
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(Guid tenantId, string key, Guid userId, string method, string path, string requestHash,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        TenantId = tenantId;
        Key = key;
        UserId = userId;
        Method = method;
        Path = path;
        RequestHash = requestHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = IdempotencyStatus.InProgress;
    }

    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public string Method { get; private set; } = string.Empty;
    public string Path { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public IdempotencyStatus Status { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public string? ResponseContentType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public void Complete(int statusCode, string? body, string? contentType, DateTimeOffset at)
    {
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = statusCode;
        ResponseBody = body;
        ResponseContentType = contentType;
        CompletedAt = at;
    }
}

/// <summary>Metadata of a stored binary (product image, invoice PDF). Content lives in <see cref="StoredFileContent"/>.</summary>
public sealed class StoredFile : Entity, ITenantOwned
{
    private StoredFile() { }

    public StoredFile(string fileName, string contentType, long sizeBytes, string sha256, string category,
        Guid? createdBy, DateTimeOffset createdAt)
    {
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        Category = category;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class StoredFileContent : ITenantOwned
{
    private StoredFileContent() { }

    public StoredFileContent(Guid fileId, byte[] data)
    {
        FileId = fileId;
        Data = data;
    }

    public Guid FileId { get; private set; }
    public Guid TenantId { get; private set; }
    public byte[] Data { get; private set; } = [];
}

/// <summary>Per-tenant counters for human-readable document numbers (SKU, order, invoice, …).</summary>
public sealed class TenantSequence : ITenantOwned
{
    private TenantSequence() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public long NextValue { get; private set; }
}

public enum ScheduledJobRunStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>
/// One execution of a scheduled job. The unique (job, run key) index guarantees a daily job runs once even
/// with many worker instances.
/// </summary>
public sealed class ScheduledJobRun : Entity
{
    private ScheduledJobRun() { }

    public ScheduledJobRun(string jobName, string runKey, string instanceId, DateTimeOffset startedAt)
    {
        JobName = jobName;
        RunKey = runKey;
        InstanceId = instanceId;
        StartedAt = startedAt;
        Status = ScheduledJobRunStatus.Running;
    }

    public string JobName { get; private set; } = string.Empty;
    public string RunKey { get; private set; } = string.Empty;
    public string InstanceId { get; private set; } = string.Empty;
    public ScheduledJobRunStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Error { get; private set; }

    public void Complete(DateTimeOffset at)
    {
        Status = ScheduledJobRunStatus.Succeeded;
        CompletedAt = at;
    }

    public void Fail(string error, DateTimeOffset at)
    {
        Status = ScheduledJobRunStatus.Failed;
        CompletedAt = at;
        Error = error.Length > 4000 ? error[..4000] : error;
    }
}
