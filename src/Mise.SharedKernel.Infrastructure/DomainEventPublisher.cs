using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// The ~40-line hand-rolled publisher ADR-005 chose over MediatR. Zero handlers registered for
/// an event type is not an error — an event with no subscriber yet (e.g. <c>ReservationCreated</c>,
/// still unused as of Phase 7) is a normal, unfinished-wiring state, not a bug to guard against.
/// A handler that throws propagates to the caller uncaught: correction #10's whole point is that
/// a cross-module side effect failing must be as visible as any other failure in the same
/// request, never silently swallowed.
/// </summary>
public sealed partial class DomainEventPublisher(IServiceProvider serviceProvider, ILogger<DomainEventPublisher> logger)
    : IDomainEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TEvent>>().ToArray();
        LogDispatching(typeof(TEvent).Name, handlers.Length);

        foreach (var handler in handlers)
        {
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Debug,
        Message = "Dispatching domain event {EventType} to {HandlerCount} handler(s).")]
    private partial void LogDispatching(string eventType, int handlerCount);
}
