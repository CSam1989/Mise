using System.Security.Claims;
using Mise.Modules.Tables.Application.CreateTableGroup;
using Mise.Modules.Tables.Contracts;

namespace Mise.ApiService.Tables;

/// <summary>Create-only this phase (CLAUDE.md's Reservations Phase 6 section / ADR-006) — no
/// xmin, no Update/Deactivate, same minimalism Section had before its own concurrency needs
/// forced Table's hand in Phase 4.</summary>
internal static class TableGroupsEndpoints
{
    public static IEndpointRouteBuilder MapTableGroupsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/table-groups", CreateTableGroupAsync).RequireAuthorization("Manager");
        return app;
    }

    private static async Task<IResult> CreateTableGroupAsync(
        CreateTableGroupRequest request,
        CreateTableGroupCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new CreateTableGroupCommand(request.OperationId, request.Name, request.TableIds, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);

        var dto = new TableGroupDto(result.TableGroupId, request.Name, IsActive: true, request.TableIds);
        return Results.Created($"/api/table-groups/{result.TableGroupId}", dto);
    }
}
