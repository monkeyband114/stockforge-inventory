using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Domain.Platform;

namespace StockForge.Application.Jobs;

public sealed record JobDto(
    Guid Id,
    Guid? TenantId,
    string Type,
    OutboxStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? ProcessedAt,
    string? LastError,
    string? CorrelationId);

public sealed record JobStatsDto(int Pending, int Processing, int Failed, int SucceededLast24Hours);

public sealed record JobQuery : PageQuery
{
    public OutboxStatus? Status { get; init; }
    public string? Type { get; init; }
}

/// <summary>
/// Failed-job monitoring. Organization members see their tenant's jobs; platform administrators without a
/// selected tenant see every job. Payloads are never exposed (they may contain personal data).
/// </summary>
public sealed class JobMonitoringService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PagedResult<JobDto>> ListAsync(JobQuery query, CancellationToken cancellationToken)
    {
        var jobs = Scoped();
        if (query.Status is { } status)
        {
            jobs = jobs.Where(j => j.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            jobs = jobs.Where(j => j.Type == query.Type);
        }

        return await jobs.OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobDto(j.Id, j.TenantId, j.Type, j.Status, j.Attempts, j.MaxAttempts, j.CreatedAt,
                j.NextAttemptAt, j.ProcessedAt, j.LastError, j.CorrelationId))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<JobStatsDto> GetStatsAsync(CancellationToken cancellationToken)
    {
        var since = clock.GetUtcNow().AddHours(-24);
        var jobs = Scoped();
        return new JobStatsDto(
            await jobs.CountAsync(j => j.Status == OutboxStatus.Pending, cancellationToken),
            await jobs.CountAsync(j => j.Status == OutboxStatus.Processing, cancellationToken),
            await jobs.CountAsync(j => j.Status == OutboxStatus.Failed, cancellationToken),
            await jobs.CountAsync(j => j.Status == OutboxStatus.Succeeded && j.ProcessedAt >= since, cancellationToken));
    }

    public async Task RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await Scoped(tracking: true).FirstOrDefaultAsync(j => j.Id == id, cancellationToken)
                  ?? throw new NotFoundException("Job", id);
        job.Requeue(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<OutboxMessage> Scoped(bool tracking = false)
    {
        var jobs = tracking ? db.OutboxMessages : db.OutboxMessages.AsNoTracking();
        if (tenantContext.TenantId is { } tenantId)
        {
            return jobs.Where(j => j.TenantId == tenantId);
        }

        return currentUser.IsPlatformAdmin
            ? jobs
            : throw new ForbiddenException("No organization is selected for this request.");
    }
}
