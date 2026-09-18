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

/// <summary>Phase 6 adds CustomerPhone — FR-01 names it explicitly ("create a reservation with
/// customer name, phone number, party size, date/time") and the API now requires it. The richer
/// optional fields (email/notes/table assignment/duration) stay server-defaulted for now — the
/// full search/edit/cancel/table-assignment screens are Phase 10 scope, same deferral Tables/
/// Scheduling made for their entire UI.</summary>
public sealed record CreateReservationRequest(
    Guid OperationId, string CustomerName, string CustomerPhone, int PartySize, DateTimeOffset ReservationDateTime);

public sealed record ReservationDto(
    Guid Id, string CustomerName, int PartySize, DateTimeOffset ReservationDateTime, string Status);
