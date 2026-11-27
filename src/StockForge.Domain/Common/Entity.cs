namespace StockForge.Domain.Common;

/// <summary>Base type for all persisted entities. Identifiers are UUIDv7 (time-ordered, index friendly).</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity() => Id = Guid.CreateVersion7();

    public Guid Id { get; protected set; }

    /// <summary>Events raised by this entity since it was loaded. Methods (not properties) so EF ignores them.</summary>
    public IReadOnlyCollection<IDomainEvent> GetDomainEvents() => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>Entity with creation/modification stamps, populated by the persistence layer.</summary>
public abstract class AuditableEntity : Entity, IHasTimestamps
{
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
}

/// <summary>
/// Auditable entity owned by a tenant. <see cref="TenantId"/> is stamped by the persistence layer from the
/// trusted tenant context and can never be changed afterwards.
/// </summary>
public abstract class TenantEntity : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
}

/// <summary>Marks an entity whose rows belong to exactly one tenant.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>Marks an entity that carries created/updated stamps.</summary>
public interface IHasTimestamps
{
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset? UpdatedAt { get; }
}

/// <summary>Marks an entity whose inserts, updates and deletes are written to the audit log.</summary>
public interface IAuditable;

/// <summary>Domain event raised by an aggregate; published after the surrounding transaction commits.</summary>
public interface IDomainEvent
{
    Guid TenantId { get; }
}
