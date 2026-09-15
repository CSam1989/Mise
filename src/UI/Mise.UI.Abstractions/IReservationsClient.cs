namespace Mise.UI.Abstractions;

/// <summary>
/// One implementation (HTTP+SignalR) satisfies this for both Mise.Web and Mise.Maui
/// (ADR-004). Deliberately its own types, not a reuse of Mise.Modules.Reservations.Contracts
/// — the RCL must not know a module exists at all (CLAUDE.md's UI boundary row).
/// </summary>
public interface IReservationsClient
{
    Task<CreateReservationResult> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken);
}

public sealed record CreateReservationRequest(
    Guid OperationId, string CustomerName, int PartySize, DateTimeOffset ReservationDateTime);

public sealed record ReservationDto(
    Guid Id, string CustomerName, int PartySize, DateTimeOffset ReservationDateTime, string Status);
