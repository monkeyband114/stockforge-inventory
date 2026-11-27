using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockForge.Application.Abstractions;

namespace StockForge.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Publishes the scope's tenant to PostgreSQL as the session setting <c>app.tenant_id</c>, which the
/// row-level-security policies compare against. Applied whenever a connection opens and whenever a
/// transaction starts (so flows that enter a tenant before beginning a transaction are covered).
/// Npgsql resets session state when a connection returns to the pool.
/// </summary>
internal static class TenantSessionSetting
{
    public const string Name = "app.tenant_id";

    public static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, ITenantContext tenant)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT set_config('{Name}', @tenant, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenant.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);
        return command;
    }
}

internal sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = TenantSessionSetting.CreateCommand(connection, null, tenant);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = TenantSessionSetting.CreateCommand(connection, null, tenant);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed class TenantTransactionInterceptor(ITenantContext tenant) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        using var command = TenantSessionSetting.CreateCommand(connection, result, tenant);
        command.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        await using var command = TenantSessionSetting.CreateCommand(connection, result, tenant);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }
}
