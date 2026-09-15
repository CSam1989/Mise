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
        // The placeholder auth spine (Phase 2) issues a system identity, not a staff one —
        // StaffIdentity (Phase 3) starts passing a real staff id as PerformedBy instead.
        var performedBy = httpContext.User.Identity?.Name ?? "unknown";

        var command = new CreateReservationCommand(
            request.OperationId, request.CustomerName, request.PartySize, request.ReservationDateTime, performedBy);

        var reservationId = await handler.HandleAsync(command, cancellationToken);

        var dto = new ReservationDto(
            reservationId, request.CustomerName, request.PartySize, request.ReservationDateTime, "Confirmed");

        return Results.Created($"/api/reservations/{reservationId}", dto);
    }
}
