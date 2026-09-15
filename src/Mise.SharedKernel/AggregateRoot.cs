namespace Mise.SharedKernel;

/// <summary>
/// An <see cref="Entity{TId}"/> that raises <see cref="IDomainEvent"/>s. Events accumulate
/// until the composition root dispatches and clears them (decision #10 — synchronous,
/// within the same request, not fire-and-forget).
/// </summary>
public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id)
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
