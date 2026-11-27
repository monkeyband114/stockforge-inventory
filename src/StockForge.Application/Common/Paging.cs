using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;

namespace StockForge.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Common query-string paging parameters (bound with [AsParameters]/[FromQuery]).</summary>
public record PageQuery
{
    public const int MaxPageSize = 200;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? Search { get; init; }

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
}

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken cancellationToken)
    {
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page.SafePage, page.SafePageSize, total);
    }
}

public static class CurrentUserExtensions
{
    public static bool CanAccessWarehouse(this ICurrentUser user, Guid warehouseId) =>
        user.AccessibleWarehouseIds is null || user.AccessibleWarehouseIds.Contains(warehouseId);

    /// <summary>Resource-level check: warehouse-scoped staff may only act on their assigned warehouses.</summary>
    public static void EnsureWarehouseAccess(this ICurrentUser user, Guid warehouseId)
    {
        if (!user.CanAccessWarehouse(warehouseId))
        {
            throw new ForbiddenException("You are not assigned to this warehouse.");
        }
    }

    public static void EnsurePermission(this ICurrentUser user, string permission)
    {
        if (!user.HasPermission(permission))
        {
            throw new ForbiddenException($"Missing permission '{permission}'.");
        }
    }
}
