using Microsoft.AspNetCore.SignalR;
using Mise.SharedKernel.Infrastructure;

namespace Mise.ApiService.Realtime;

/// <summary>
/// The one implementation of <see cref="IRealtimeNotifier"/> — wraps <see cref="IHubContext{T}"/>
/// directly rather than adding a further indirection layer over it (docs/plan.md's own guidance:
/// pick one seam, not <see cref="IDomainEventPublisher"/> *and* a notifier interface *and*
/// <see cref="IHubContext{T}"/> all stacked for the same broadcast). The four event names below
/// are the frozen wire contract MAUI's future client depends on — never rename one without a
/// coordinated client-side change; add a new one rather than repurposing an existing name.
/// <para>
/// Every broadcast is wrapped in its own try/catch: a failure to push (a serialization bug, a
/// transport hiccup) is logged at <see cref="LogLevel.Error"/> and swallowed, never rethrown —
/// the triggering mutation has already committed successfully by the time this runs, so letting
/// a broadcast failure fail the HTTP response would falsely tell the caller their write didn't
/// take, when it did. A missed live-update push is a staleness/UX gap (a client's next
/// GET/floor-plan refresh still sees correct state), never a data-integrity one — unlike
/// <see cref="IAuditWriter"/>, which must propagate.
/// </para>
/// </summary>
public sealed partial class SignalRRealtimeNotifier(
    IHubContext<FloorPlanHub> hubContext, ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public const string TableStatusChangedEventName = "TableStatusChanged";
    public const string ReservationCreatedEventName = "ReservationCreated";
    public const string ReservationUpdatedEventName = "ReservationUpdated";
    public const string ReservationCancelledEventName = "ReservationCancelled";

    public Task NotifyTableStatusChangedAsync(TableStatusChangedNotification notification, CancellationToken cancellationToken) =>
        BroadcastAsync(TableStatusChangedEventName, notification, cancellationToken);

    public Task NotifyReservationCreatedAsync(ReservationChangedNotification notification, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationCreatedEventName, notification, cancellationToken);

    public Task NotifyReservationUpdatedAsync(ReservationChangedNotification notification, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationUpdatedEventName, notification, cancellationToken);

    public Task NotifyReservationCancelledAsync(ReservationChangedNotification notification, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationCancelledEventName, notification, cancellationToken);

    private async Task BroadcastAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(eventName, payload, cancellationToken);
            LogBroadcast(eventName);
        }
        catch (Exception ex)
        {
            LogBroadcastFailed(ex, eventName);
        }
    }

    [LoggerMessage(EventId = 101, Level = LogLevel.Debug, Message = "Broadcast {EventName} to all connected floor-plan clients.")]
    private partial void LogBroadcast(string eventName);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "Failed to broadcast {EventName}; the underlying mutation already committed.")]
    private partial void LogBroadcastFailed(Exception exception, string eventName);
}
