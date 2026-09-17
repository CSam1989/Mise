namespace Mise.Modules.StaffIdentity.Contracts;

/// <summary>
/// The API's response shape for a staff account — used by Mise.ApiService's register-staff
/// endpoint today. No cross-module consumer yet (Tables/Scheduling don't need it until
/// Phases 4-5), but this is the module's own public data shape either way, same role
/// Reservations.Contracts' ReservationDto already plays for its endpoint.
/// </summary>
public sealed record StaffSummaryDto(Guid Id, string Username, string FullName, string Role);
