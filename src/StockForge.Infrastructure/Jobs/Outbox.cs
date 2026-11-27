using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Domain.Platform;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Jobs;

internal static class JobSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Encrypts sensitive job payloads at rest with ASP.NET Core Data Protection (keys stored in PostgreSQL).</summary>
public sealed class JobPayloadProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("StockForge.Outbox.Payload.v1");

    public string Protect(string payload) => _protector.Protect(payload);

    public string Unprotect(string payload) => _protector.Unprotect(payload);
}

/// <summary>Writes jobs to the transactional outbox; they are persisted by the caller's SaveChanges.</summary>
internal sealed class OutboxJobQueue(
    AppDbContext db,
    ScopeContext scope,
    JobPayloadProtector protector,
    TimeProvider clock) : IJobQueue
{
    public void Enqueue<TJob>(TJob job, JobOptions? options = null) where TJob : class, IJob
    {
        options ??= new JobOptions();
        var json = JsonSerializer.Serialize(job, JobSerialization.Options);
        db.OutboxMessages.Add(new OutboxMessage(
            scope.TenantId,
            TJob.JobType,
            options.Sensitive ? protector.Protect(json) : json,
            options.Sensitive,
            options.MaxAttempts,
            clock.GetUtcNow(),
            options.NotBefore,
            scope.CorrelationId,
            options.DeduplicationKey));
    }
}

/// <summary>Maps stored job type names to CLR job types and their handlers.</summary>
public sealed class JobRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, string, CancellationToken, Task>> _handlers = new(StringComparer.Ordinal);

    public void Register<TJob, THandler>()
        where TJob : class, IJob
        where THandler : IJobHandler<TJob>
    {
        _handlers[TJob.JobType] = async (services, payload, token) =>
        {
            var job = JsonSerializer.Deserialize<TJob>(payload, JobSerialization.Options)
                      ?? throw new InvalidOperationException($"Payload for {TJob.JobType} is empty.");
            await services.GetRequiredService<THandler>().HandleAsync(job, token);
        };
    }

    public bool TryGet(string type, out Func<IServiceProvider, string, CancellationToken, Task> handler) =>
        _handlers.TryGetValue(type, out handler!);

    public IReadOnlyCollection<string> Types => _handlers.Keys;
}

public static class JobRegistrationExtensions
{
    public static IServiceCollection AddJobHandler<TJob, THandler>(this IServiceCollection services)
        where TJob : class, IJob
        where THandler : class, IJobHandler<TJob>
    {
        services.AddScoped<THandler>();
        services.AddSingleton<Action<JobRegistry>>(registry => registry.Register<TJob, THandler>());
        return services;
    }
}

public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 20;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Claims due outbox rows with <c>FOR UPDATE SKIP LOCKED</c> (safe across any number of worker instances),
/// runs each job in its own DI scope with the job's tenant set, and records success, a scheduled retry with
/// exponential back-off, or permanent failure. Rows whose lock expired (crashed worker) are reclaimed.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    JobRegistry registry,
    JobPayloadProtector protector,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessor> logger)
{
    private readonly string _instanceId = Truncate($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}", 100);

    /// <summary>Processes one batch; returns the number of jobs handled.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var ids = await ClaimAsync(cancellationToken);
        foreach (var id in ids)
        {
            await ProcessAsync(id, cancellationToken);
        }

        return ids.Count;
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var lockedUntil = now + options.Value.LockDuration;
        var batch = options.Value.BatchSize;
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE outbox_messages
            SET status = 'Processing', locked_until = {lockedUntil}, locked_by = {_instanceId}
            WHERE id IN (
                SELECT id FROM outbox_messages
                WHERE (status = 'Pending' AND next_attempt_at <= {now})
                   OR (status = 'Processing' AND locked_until < {now})
                ORDER BY next_attempt_at
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var bookkeeping = scopeFactory.CreateAsyncScope();
        var db = bookkeeping.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobId"] = message.Id,
            ["JobType"] = message.Type,
            ["TenantId"] = message.TenantId,
            ["CorrelationId"] = message.CorrelationId,
        });

        try
        {
            if (!registry.TryGet(message.Type, out var handler))
            {
                throw new InvalidOperationException($"No handler is registered for job type '{message.Type}'.");
            }

            var payload = message.IsPayloadProtected ? protector.Unprotect(message.Payload) : message.Payload;
            await using (var jobScope = scopeFactory.CreateAsyncScope())
            {
                jobScope.ServiceProvider.GetRequiredService<ScopeContext>().SetSystem(message.TenantId, message.CorrelationId);
                await handler(jobScope.ServiceProvider, payload, cancellationToken);
            }

            message.MarkSucceeded(clock.GetUtcNow());
            logger.LogInformation("Job {JobType} {JobId} succeeded", message.Type, message.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var delay = RetryDelay(message.Attempts + 1);
            message.MarkAttemptFailed($"{ex.GetType().Name}: {ex.Message}", clock.GetUtcNow(), delay);
            if (message.Status == OutboxStatus.Failed)
            {
                logger.LogError(ex, "Job {JobType} {JobId} failed permanently after {Attempts} attempts", message.Type, message.Id, message.Attempts);
            }
            else
            {
                logger.LogWarning(ex, "Job {JobType} {JobId} failed (attempt {Attempts}); retrying in {Delay}", message.Type, message.Id, message.Attempts, delay);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>Exponential back-off (10 s, 20 s, 40 s, …) with ±20% jitter, capped.</summary>
    internal TimeSpan RetryDelay(int attempt)
    {
        var seconds = Math.Min(options.Value.MaxRetryDelay.TotalSeconds, 10 * Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(seconds * (0.8 + (Random.Shared.NextDouble() * 0.4)));
    }
}
