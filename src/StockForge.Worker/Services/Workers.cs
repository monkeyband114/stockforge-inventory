using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Application.Billing;
using StockForge.Application.Inventory;
using StockForge.Application.Jobs;
using StockForge.Domain.Platform;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Jobs;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Worker.Services;

/// <summary>Continuously drains the transactional outbox. Safe to run on any number of instances.</summary>
public sealed class OutboxWorker(OutboxProcessor processor, IOptions<OutboxOptions> options, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await processor.ProcessBatchAsync(stoppingToken) == 0)
                {
                    await Task.Delay(options.Value.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox polling failed; backing off");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}

public sealed class SchedulerOptions
{
    /// <summary>UTC hour after which the daily inventory summary is sent.</summary>
    public int DailySummaryHourUtc { get; set; } = 6;

    public TimeSpan Tick { get; set; } = TimeSpan.FromMinutes(1);

    public int SucceededOutboxRetentionDays { get; set; } = 7;
}

/// <summary>
/// Cron-like scheduler. Each run is claimed by inserting <c>(job, run key)</c> into
/// <c>scheduled_job_runs</c>; the unique index guarantees one execution per period across all instances.
/// </summary>
public sealed class ScheduledJobsWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<SchedulerOptions> options,
    ILogger<ScheduledJobsWorker> logger) : BackgroundService
{
    private readonly string _instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Tick);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        await RunOnceAsync("release-expired-reservations", $"{now:yyyy-MM-ddTHH}:{now.Minute / 15}", ct =>
            ForEachTenantAsync(async services => await services.GetRequiredService<ReservationService>().ReleaseExpiredAsync(ct), ct), cancellationToken);

        await RunOnceAsync("purge-idempotency-keys", $"{now:yyyy-MM-ddTHH}", ct =>
            ForEachTenantAsync(async services => await services.GetRequiredService<IdempotencyStore>().PurgeExpiredAsync(ct), ct), cancellationToken);

        await RunOnceAsync("purge-outbox", today.ToString("O"), PurgeOutboxAsync, cancellationToken);

        await RunOnceAsync("mark-overdue-invoices", today.ToString("O"), ct =>
            ForEachTenantAsync(async services => await services.GetRequiredService<InvoiceService>().MarkOverdueAsync(ct), ct), cancellationToken);

        if (now.Hour >= options.Value.DailySummaryHourUtc)
        {
            await RunOnceAsync("daily-inventory-summary", today.ToString("O"), ct =>
                ForEachTenantAsync(async services =>
                {
                    services.GetRequiredService<IJobQueue>().Enqueue(new DailyInventorySummaryJob(today),
                        new JobOptions { DeduplicationKey = $"daily-summary:{services.GetRequiredService<ITenantContext>().TenantId}:{today:O}" });
                    await services.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
                }, ct), cancellationToken);
        }
    }

    private async Task RunOnceAsync(string jobName, string runKey, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.CreateVersion7();
        var startedAt = clock.GetUtcNow();
        var claimed = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO scheduled_job_runs (id, job_name, run_key, instance_id, status, started_at)
            VALUES ({id}, {jobName}, {runKey}, {_instanceId}, 'Running', {startedAt})
            ON CONFLICT (job_name, run_key) DO NOTHING
            """, cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var run = await db.ScheduledJobRuns.FirstAsync(r => r.Id == id, cancellationToken);
        try
        {
            await work(cancellationToken);
            run.Complete(clock.GetUtcNow());
            logger.LogInformation("Scheduled job {Job} ({RunKey}) completed", jobName, runKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Fail(ex.Message, clock.GetUtcNow());
            logger.LogError(ex, "Scheduled job {Job} ({RunKey}) failed", jobName, runKey);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Runs work once per active organization, each in its own scope with that tenant set.</summary>
    private async Task ForEachTenantAsync(Func<IServiceProvider, Task> work, CancellationToken cancellationToken)
    {
        List<Guid> tenantIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            tenantIds = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.AsNoTracking()
                .Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(cancellationToken);
        }

        foreach (var tenantId in tenantIds)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ScopeContext>().SetSystem(tenantId, $"scheduler:{Guid.NewGuid():N}");
            try
            {
                await work(scope.ServiceProvider);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One organization's failure must not block the others.
                logger.LogError(ex, "Scheduled work failed for tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task PurgeOutboxAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.SucceededOutboxRetentionDays);
        var deleted = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Succeeded && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Purged {Count} delivered outbox messages", deleted);
    }
}
