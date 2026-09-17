using System.Security.Claims;
using Mise.Modules.Reservations.Application.CreateReservation;
using Mise.Modules.Reservations.Contracts;

namespace Mise.ApiService.Reservations;

internal static class ReservationsEndpoints
{
    public static IEndpointRouteBuilder MapReservationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/reservations", CreateReservationAsync);
        return app;
    }

    private static async Task<IResult> CreateReservationAsync(
        CreateReservationRequest request,
        CreateReservationCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Every caller here is a real, authenticated staff member (StaffIdentity, Phase 3) —
        // the global fallback policy already guarantees authentication, and JwtTokenIssuer
        // always sets this claim, so a missing/malformed one is a genuine bug worth a loud
        // failure rather than a silent "unknown" fallback (Phase 2's placeholder behavior).
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var command = new CreateReservationCommand(
            request.OperationId, request.CustomerName, request.PartySize, request.ReservationDateTime, performedByStaffId);

        var reservationId = await handler.HandleAsync(command, cancellationToken);

        var dto = new ReservationDto(
            reservationId, request.CustomerName, request.PartySize, request.ReservationDateTime, "Confirmed");

        return Results.Created($"/api/reservations/{reservationId}", dto);
    }
}
