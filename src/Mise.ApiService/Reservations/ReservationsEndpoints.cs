using System.Security.Claims;
using Mise.Modules.Reservations.Application.CancelReservation;
using Mise.Modules.Reservations.Application.CreateReservation;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Application.UpdateReservation;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Domain;

namespace Mise.ApiService.Reservations;

/// <summary>
/// Every mutating endpoint sets the response's ETag header (docs/plan.md correction #5,
/// extended to Reservation in Phase 6); Update/Cancel additionally require a matching If-Match
/// request header, same shape as TablesEndpoints. FloorStaff policy — an OR of FloorStaff/
/// Manager — covers every endpoint here: FR-01–03 name no permission boundary between the two
/// roles for reservations, unlike Tables/Scheduling's Manager-only mutations.
/// </summary>
internal static class ReservationsEndpoints
{
    public static IEndpointRouteBuilder MapReservationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/reservations", CreateReservationAsync).RequireAuthorization("FloorStaff");
        app.MapGet("/api/reservations/search", SearchReservationsAsync).RequireAuthorization("FloorStaff");
        app.MapPatch("/api/reservations/{id:guid}", UpdateReservationAsync).RequireAuthorization("FloorStaff");
        app.MapPatch("/api/reservations/{id:guid}/cancel", CancelReservationAsync).RequireAuthorization("FloorStaff");
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
            request.OperationId, request.CustomerName, request.CustomerPhone, request.CustomerEmail, request.PartySize,
            request.ReservationDateTime, request.DurationMinutes, request.TableId, request.Notes, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);

        httpContext.Response.Headers.ETag = ETag.Format(result.Version);
        return Results.Created($"/api/reservations/{result.Reservation.Id}", ToDto(result.Reservation, result.Version));
    }

    private static async Task<IResult> SearchReservationsAsync(
        string? query, DateOnly? date, IReservationsData reservationsData, CancellationToken cancellationToken)
    {
        var reservations = await reservationsData.SearchReservationsAsync(query, date, cancellationToken);
        return Results.Ok(reservations.Select(r => ToDto(r.Reservation, r.Version)).ToArray());
    }

    private static async Task<IResult> UpdateReservationAsync(
        Guid id,
        UpdateReservationRequest request,
        UpdateReservationCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ETag.TryParse(httpContext.Request.Headers["If-Match"].ToString(), out var expectedVersion))
        {
            throw new PreconditionRequiredException(
                "An If-Match header carrying the reservation's current version (from a prior GET/POST/PATCH response's ETag) is required to update it.");
        }

        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new UpdateReservationCommand(
            request.OperationId, id, expectedVersion, request.CustomerName, request.CustomerPhone, request.CustomerEmail,
            request.PartySize, request.ReservationDateTime, request.DurationMinutes, request.TableId, request.Notes,
            performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ETag.Format(result.Version);
        return Results.Ok(ToDto(result.Reservation, result.Version));
    }

    private static async Task<IResult> CancelReservationAsync(
        Guid id,
        CancelReservationRequest request,
        CancelReservationCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ETag.TryParse(httpContext.Request.Headers["If-Match"].ToString(), out var expectedVersion))
        {
            throw new PreconditionRequiredException(
                "An If-Match header carrying the reservation's current version (from a prior GET/POST/PATCH response's ETag) is required to cancel it.");
        }

        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new CancelReservationCommand(request.OperationId, id, expectedVersion, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ETag.Format(result.Version);
        return Results.Ok(ToDto(result.Reservation, result.Version));
    }

    private static ReservationDto ToDto(Reservation reservation, uint version) => new(
        reservation.Id, reservation.CustomerName, reservation.CustomerPhone, reservation.CustomerEmail,
        reservation.PartySize, reservation.ReservationDateTime, reservation.DurationMinutes, reservation.Status.ToString(),
        reservation.TableId, reservation.Notes, reservation.CreatedByStaffId, reservation.CreatedAtUtc,
        reservation.UpdatedAtUtc, version);
}
