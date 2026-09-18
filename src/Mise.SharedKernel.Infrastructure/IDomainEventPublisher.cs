namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// Dispatched after the triggering commit, but awaited within the same request before the
/// response returns (docs/plan.md correction #10 / RISK-01) — never fire-and-forget. The
/// generic method is resolved at the call site with a compile-time-known <typeparamref
/// name="TEvent"/> (e.g. <c>publisher.PublishAsync(new ReservationSeated(...), ct)</c>), so
/// dispatch is ordinary DI resolution (<see cref="IServiceProvider.GetServices{T}"/>) — no
/// reflection over the event's runtime type.
/// </summary>
public interface IDomainEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent;
}
