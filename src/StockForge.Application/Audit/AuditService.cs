using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Domain.Platform;

namespace StockForge.Application.Audit;

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string EntityType,
    string? EntityId,
    JsonElement? OldValues,
    JsonElement? NewValues,
    IReadOnlyList<string>? ChangedProperties,
    string? IpAddress,
    string? CorrelationId);

public sealed record AuditQuery : PageQuery
{
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public Guid? UserId { get; init; }
    public string? Action { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

/// <summary>Read access to the tenant's immutable audit trail.</summary>
public sealed class AuditService(IAppDbContext db, ITenantContext tenantContext)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequiredTenantId;
        var logs = db.AuditLogs.AsNoTracking().Where(a => a.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            logs = logs.Where(a => a.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            logs = logs.Where(a => a.EntityId == query.EntityId);
        }

        if (query.UserId is { } userId)
        {
            logs = logs.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            logs = logs.Where(a => a.Action == query.Action);
        }

        if (query.From is { } from)
        {
            logs = logs.Where(a => a.Timestamp >= from);
        }

        if (query.To is { } to)
        {
            logs = logs.Where(a => a.Timestamp < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            logs = logs.Where(a => (a.UserEmail != null && a.UserEmail.ToLower().Contains(term)) || a.EntityType.ToLower().Contains(term));
        }

        var page = await logs.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .ToPagedResultAsync(query, cancellationToken);
        return new PagedResult<AuditLogDto>(page.Items.Select(ToDto).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    private static AuditLogDto ToDto(AuditLog a) => new(
        a.Id, a.Timestamp, a.UserId, a.UserEmail, a.Action, a.EntityType, a.EntityId, Parse(a.OldValues),
        Parse(a.NewValues), a.ChangedProperties, a.IpAddress, a.CorrelationId);

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
