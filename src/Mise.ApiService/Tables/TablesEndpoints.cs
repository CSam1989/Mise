using System.Security.Claims;
using Mise.Modules.Tables.Application.CreateTable;
using Mise.Modules.Tables.Application.DeactivateTable;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Application.UpdateTable;
using Mise.Modules.Tables.Contracts;
using Mise.Modules.Tables.Domain;

namespace Mise.ApiService.Tables;

/// <summary>
/// Every mutating endpoint here also sets the response's ETag header (docs/plan.md
/// correction #5) — Update/Deactivate additionally require a matching If-Match request header,
/// checked before the command handler runs at all: a missing/malformed one is purely an
/// HTTP-shape concern (see PreconditionRequiredException's doc comment), not something the
/// Application layer should have to know about.
/// </summary>
internal static class TablesEndpoints
{
    public static IEndpointRouteBuilder MapTablesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tables", CreateTableAsync).RequireAuthorization("Manager");
        app.MapGet("/api/tables/floor-plan", GetFloorPlanAsync).RequireAuthorization("FloorStaff");
        app.MapPatch("/api/tables/{id:guid}", UpdateTableAsync).RequireAuthorization("Manager");
        app.MapPatch("/api/tables/{id:guid}/deactivate", DeactivateTableAsync).RequireAuthorization("Manager");
        return app;
    }

    private static async Task<IResult> CreateTableAsync(
        CreateTableRequest request,
        CreateTableCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new CreateTableCommand(
            request.OperationId, request.SectionId, request.Name, request.MinCapacity, request.MaxCapacity,
            request.IsCombinable, request.PositionX, request.PositionY, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);

        var dto = new TableDto(
            result.TableId, request.SectionId, request.Name, request.MinCapacity, request.MaxCapacity,
            request.IsCombinable, request.PositionX, request.PositionY, nameof(TableStatus.Available),
            IsActive: true, result.Version);

        httpContext.Response.Headers.ETag = ETag.Format(result.Version);
        return Results.Created($"/api/tables/{result.TableId}", dto);
    }

    private static async Task<IResult> GetFloorPlanAsync(
        Guid? sectionId, ITablesData tablesData, CancellationToken cancellationToken)
    {
        var tables = await tablesData.GetFloorPlanAsync(sectionId, cancellationToken);
        return Results.Ok(tables.Select(ToDto).ToArray());
    }

    private static async Task<IResult> UpdateTableAsync(
        Guid id,
        UpdateTableRequest request,
        UpdateTableCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ETag.TryParse(httpContext.Request.Headers["If-Match"].ToString(), out var expectedVersion))
        {
            throw new PreconditionRequiredException(
                "An If-Match header carrying the table's current version (from a prior GET/POST/PATCH response's ETag) is required to update it.");
        }

        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new UpdateTableCommand(
            request.OperationId, id, expectedVersion, request.SectionId, request.Name, request.MinCapacity,
            request.MaxCapacity, request.IsCombinable, request.PositionX, request.PositionY, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ETag.Format(result.Version);
        return Results.Ok(ToDto(new TableWithVersion(result.Table, result.Version)));
    }

    private static async Task<IResult> DeactivateTableAsync(
        Guid id,
        DeactivateTableRequest request,
        DeactivateTableCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ETag.TryParse(httpContext.Request.Headers["If-Match"].ToString(), out var expectedVersion))
        {
            throw new PreconditionRequiredException(
                "An If-Match header carrying the table's current version (from a prior GET/POST/PATCH response's ETag) is required to deactivate it.");
        }

        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new DeactivateTableCommand(request.OperationId, id, expectedVersion, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ETag.Format(result.Version);
        return Results.Ok(ToDto(new TableWithVersion(result.Table, result.Version)));
    }

    private static TableDto ToDto(TableWithVersion tableWithVersion)
    {
        var table = tableWithVersion.Table;
        return new TableDto(
            table.Id, table.SectionId, table.Name, table.MinCapacity, table.MaxCapacity, table.IsCombinable,
            table.PositionX, table.PositionY, table.Status.ToString(), table.IsActive, tableWithVersion.Version);
    }
}
