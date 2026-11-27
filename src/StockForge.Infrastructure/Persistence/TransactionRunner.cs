using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using StockForge.Application.Abstractions;

namespace StockForge.Infrastructure.Persistence;

/// <summary>
/// Transaction policy for every multi-step use case:
/// <list type="bullet">
/// <item>READ COMMITTED transaction inside the Npgsql execution strategy, which retries transient failures
/// (deadlock 40P01, serialization failure 40001, connection loss);</item>
/// <item>optimistic-concurrency conflicts (<c>xmin</c> changed) roll back, clear the change tracker and re-run
/// the whole operation against fresh data, with jittered back-off;</item>
/// <item>domain events are published only after COMMIT and discarded on rollback.</item>
/// </list>
/// Nested calls join the outer transaction.
/// </summary>
internal sealed class TransactionRunner(
    AppDbContext db,
    DomainEventBuffer domainEvents,
    ILogger<TransactionRunner> logger) : ITransactionRunner
{
    internal const int MaxConcurrencyAttempts = 8;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                var result = await strategy.ExecuteAsync(async token =>
                {
                    // Every (re)try starts from a clean slate so no state from a failed attempt leaks in.
                    db.ChangeTracker.Clear();
                    domainEvents.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
                    var value = await operation(token);
                    await transaction.CommitAsync(token);
                    return value;
                }, cancellationToken);

                await domainEvents.FlushAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxConcurrencyAttempts)
            {
                domainEvents.Clear();
                db.ChangeTracker.Clear();
                var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(5, 25) * attempt);
                logger.LogDebug(ex, "Concurrency conflict on attempt {Attempt}; retrying in {Delay} ms", attempt, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                domainEvents.Clear();
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Concurrency conflict persisted after {Attempts} attempts", attempt);
                throw new ConflictException("concurrency_conflict",
                    "The data changed while your request was processed. Please retry.");
            }
            catch
            {
                domainEvents.Clear();
                db.ChangeTracker.Clear();
                throw;
            }
        }
    }
}

/// <summary>
/// Allocates per-tenant numbers with an atomic upsert on <c>tenant_sequences</c>. Inside a transaction the
/// row stays locked until commit, so numbers are gap-free and never duplicated.
/// </summary>
internal sealed class SequenceGenerator(AppDbContext db, ITenantContext tenant) : ISequenceGenerator
{
    public async Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequiredTenantId;
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO tenant_sequences (tenant_id, name, next_value) VALUES (@tenant, @name, 2)
                ON CONFLICT (tenant_id, name) DO UPDATE SET next_value = tenant_sequences.next_value + 1
                RETURNING next_value - 1
                """;
            AddParameter(command, "tenant", tenantId);
            AddParameter(command, "name", sequenceName);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedHere)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
