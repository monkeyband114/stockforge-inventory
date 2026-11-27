namespace StockForge.Application.Abstractions;

/// <summary>Resolves user ids to display names for read models (users live in the Identity store).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);

    Task<int> CountUsersAsync(CancellationToken cancellationToken);

    /// <summary>Emails of active members of the current tenant holding any of the given roles (for notifications).</summary>
    Task<IReadOnlyList<string>> GetMemberEmailsAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken);
}

/// <summary>Evicts cached authorization snapshots so security changes apply immediately.</summary>
public interface ISessionCache
{
    Task InvalidateTenantSessionsAsync(Guid tenantId, CancellationToken cancellationToken);
}

public static class UserDirectoryExtensions
{
    public static string? NameOf(this IReadOnlyDictionary<Guid, string> names, Guid? userId) =>
        userId is { } id && names.TryGetValue(id, out var name) ? name : null;
}
