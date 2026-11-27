namespace StockForge.Infrastructure;

/// <summary>
/// System clock that can be pinned to a fixed instant for the current async flow only. Used by the
/// development seeder (to backdate history) and by tests; production code never sets an override.
/// </summary>
public sealed class AdjustableClock : TimeProvider
{
    private static readonly AsyncLocal<DateTimeOffset?> Override = new();

    public override DateTimeOffset GetUtcNow() => Override.Value ?? base.GetUtcNow();

    public static IDisposable Use(DateTimeOffset instant)
    {
        var previous = Override.Value;
        Override.Value = instant.ToUniversalTime();
        return new Restore(previous);
    }

    private sealed class Restore(DateTimeOffset? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
