namespace StockForge.Infrastructure.Persistence;

/// <summary>Row-level-security conventions shared by migrations and the isolation tests.</summary>
public static class TenantRls
{
    /// <summary>
    /// Tables that carry a tenant_id but are deliberately not row-level secured, because they are read before a
    /// tenant is known (sign-in, session validation) or by the platform worker. They are protected by the
    /// application layer instead; audit_logs has its own read-isolation policy.
    /// </summary>
    public static readonly IReadOnlyList<string> ExcludedTables =
    [
        "tenant_memberships", "warehouse_assignments", "user_sessions", "refresh_tokens", "outbox_messages", "audit_logs",
    ];

    public static string ExclusionSqlList => string.Join(", ", ExcludedTables.Select(t => $"'{t}'"));
}
