namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// ADR-005 (no mediator library) / ADR-007's cross-module event handler contract. Registered
/// directly in DI per event type (e.g. <c>services.AddScoped&lt;IDomainEventHandler&lt;ReservationSeated&gt;,
/// ReservationSeatedTableOccupiedHandler&gt;()</c>) at Mise.ApiService's composition root — no
/// assembly scanning, no reflection-based discovery. A module's Application layer both
/// publishes (via <see cref="IDomainEventPublisher"/>) and subscribes to another module's
/// events by depending on that module's Contracts project directly (an allowed cross-module
/// reference), never its Domain/Application/Infrastructure.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
