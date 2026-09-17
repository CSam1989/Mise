using System.Security.Claims;
using Mise.Modules.Scheduling.Application.CreateServicePeriod;
using Mise.Modules.Scheduling.Application.DeleteServicePeriod;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.Modules.Scheduling.Application.UpdateServicePeriod;
using Mise.Modules.Scheduling.Contracts;
using Mise.Modules.Scheduling.Domain;

namespace Mise.ApiService.Scheduling;

/// <summary>
/// GetServicePeriodsForDateAsync depends on <see cref="ISchedulingData"/> directly rather than
/// a query-handler class — same reasoning as SectionsEndpoints.GetActiveSectionsAsync: the read
/// has no logic beyond a gateway call and a DTO projection.
///
/// The delete endpoint takes its OperationId as a query parameter rather than a JSON body —
/// unlike POST/PATCH, a DELETE with a request body has no precedent anywhere else in this
/// codebase, and minimal APIs bind query parameters far more naturally than a DELETE body. The
/// OperationId idempotency contract (CLAUDE.md's mutating-endpoint row) still applies in full.
/// </summary>
internal static class ServicePeriodsEndpoints
{
    public static IEndpointRouteBuilder MapServicePeriodsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/service-periods", CreateServicePeriodAsync).RequireAuthorization("Manager");
        app.MapGet("/api/service-periods", GetServicePeriodsForDateAsync).RequireAuthorization("FloorStaff");
        app.MapPatch("/api/service-periods/{id:guid}", UpdateServicePeriodAsync).RequireAuthorization("Manager");
        app.MapDelete("/api/service-periods/{id:guid}", DeleteServicePeriodAsync).RequireAuthorization("Manager");
        return app;
    }

    private static async Task<IResult> CreateServicePeriodAsync(
        CreateServicePeriodRequest request,
        CreateServicePeriodCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new CreateServicePeriodCommand(
            request.OperationId, request.Date, request.Label, request.StartTime, request.EndTime,
            request.EndsNextDay, request.IsClosed, performedByStaffId);

        var servicePeriodId = await handler.HandleAsync(command, cancellationToken);

        var dto = new ServicePeriodDto(
            servicePeriodId, request.Date, request.Label, request.StartTime, request.EndTime,
            request.EndsNextDay, request.IsClosed);
        return Results.Created($"/api/service-periods/{servicePeriodId}", dto);
    }

    private static async Task<IResult> GetServicePeriodsForDateAsync(
        DateOnly date, ISchedulingData schedulingData, CancellationToken cancellationToken)
    {
        var servicePeriods = await schedulingData.GetServicePeriodsForDateAsync(date, cancellationToken);
        return Results.Ok(servicePeriods.Select(ToDto).ToArray());
    }

    private static async Task<IResult> UpdateServicePeriodAsync(
        Guid id,
        UpdateServicePeriodRequest request,
        UpdateServicePeriodCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new UpdateServicePeriodCommand(
            request.OperationId, id, request.Date, request.Label, request.StartTime, request.EndTime,
            request.EndsNextDay, request.IsClosed, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(ToDto(result.ServicePeriod));
    }

    private static async Task<IResult> DeleteServicePeriodAsync(
        Guid id,
        Guid operationId,
        DeleteServicePeriodCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new DeleteServicePeriodCommand(operationId, id, performedByStaffId);

        var existed = await handler.HandleAsync(command, cancellationToken);
        return existed is null ? Results.NotFound() : Results.NoContent();
    }

    private static ServicePeriodDto ToDto(ServicePeriod servicePeriod) => new(
        servicePeriod.Id, servicePeriod.Date, servicePeriod.Label, servicePeriod.StartTime, servicePeriod.EndTime,
        servicePeriod.EndsNextDay, servicePeriod.IsClosed);
}
