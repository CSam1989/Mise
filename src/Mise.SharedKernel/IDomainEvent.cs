namespace Mise.SharedKernel;

/// <summary>
/// Marker for a domain event dispatched through the hand-rolled
/// IDomainEventPublisher/IDomainEventHandler pair in Mise.SharedKernel.Infrastructure
/// (ADR-005 — no mediator library).
/// </summary>
public interface IDomainEvent;
