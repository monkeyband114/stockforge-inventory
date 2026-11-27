using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Domain.Platform;

namespace StockForge.Infrastructure.Persistence;

public enum IdempotencyClaim
{
    /// <summary>This request owns the key and must execute.</summary>
    Acquired,

    /// <summary>The key completed earlier with the same request: replay the stored response.</summary>
    Completed,

    /// <summary>The same key is executing right now in another request.</summary>
    InProgress,

    /// <summary>The key was used for a different request or by a different user.</summary>
    Mismatch,
}

public sealed record IdempotencyLookup(IdempotencyClaim Claim, IdempotencyRecord? Record);

/// <summary>
/// Persists idempotency keys per tenant. The claim is an <c>INSERT … ON CONFLICT DO NOTHING</c> on the
/// unique <c>(tenant_id, key)</c> index, so two concurrent retries cannot both execute.
/// </summary>
public sealed class IdempotencyStore(AppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(5);

    public async Task<IdempotencyLookup> TryClaimAsync(string key, Guid userId, string method, string path, string requestHash, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequiredTenantId;
        var now = clock.GetUtcNow();
        var expires = now + Retention;
        var id = Guid.CreateVersion7();
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO idempotency_records (id, tenant_id, key, user_id, method, path, request_hash, status, created_at, expires_at)
            VALUES ({id}, {tenantId}, {key}, {userId}, {method}, {path}, {requestHash}, 'InProgress', {now}, {expires})
            ON CONFLICT (tenant_id, key) DO NOTHING
            """, cancellationToken);
        if (inserted == 1)
        {
            return new IdempotencyLookup(IdempotencyClaim.Acquired, null);
        }

        var existing = await db.IdempotencyRecords.AsNoTracking().FirstAsync(r => r.Key == key, cancellationToken);
        if (existing.UserId != userId || existing.RequestHash != requestHash || existing.Method != method || existing.Path != path)
        {
            return new IdempotencyLookup(IdempotencyClaim.Mismatch, existing);
        }

        // A claim left "in progress" by a crashed process (far older than any request timeout) is taken over
        // atomically, so the client is not locked out of its own key for the whole retention period.
        if (existing.Status == IdempotencyStatus.InProgress && existing.CreatedAt < now - AbandonedAfter)
        {
            var takenOver = await db.IdempotencyRecords
                .Where(r => r.Id == existing.Id && r.Status == IdempotencyStatus.InProgress && r.CreatedAt == existing.CreatedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, now), cancellationToken);
            if (takenOver == 1)
            {
                return new IdempotencyLookup(IdempotencyClaim.Acquired, null);
            }
        }

        return new IdempotencyLookup(
            existing.Status == IdempotencyStatus.Completed ? IdempotencyClaim.Completed : IdempotencyClaim.InProgress, existing);
    }

    public async Task CompleteAsync(string key, int statusCode, string? body, string? contentType, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.IdempotencyRecords.Where(r => r.Key == key).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, IdempotencyStatus.Completed)
            .SetProperty(r => r.ResponseStatusCode, statusCode)
            .SetProperty(r => r.ResponseBody, body)
            .SetProperty(r => r.ResponseContentType, contentType)
            .SetProperty(r => r.CompletedAt, now), cancellationToken);
    }

    /// <summary>Releases a key whose request failed, so the client may retry it.</summary>
    public Task ReleaseAsync(string key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.Where(r => r.Key == key && r.Status == IdempotencyStatus.InProgress).ExecuteDeleteAsync(cancellationToken);

    /// <summary>Deletes expired keys (scheduled by the worker per tenant).</summary>
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return db.IdempotencyRecords.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
    }
}
