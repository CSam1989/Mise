using System.Security.Claims;
using Mise.Modules.Tables.Application.CreateSection;
using Mise.Modules.Tables.Application.DeactivateSection;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Application.UpdateSection;
using Mise.Modules.Tables.Contracts;
using Mise.Modules.Tables.Domain;

namespace Mise.ApiService.Tables;

/// <summary>
/// GetActiveSectionsAsync depends on <see cref="ISectionsData"/> directly rather than a
/// query-handler class: the read has no logic beyond a gateway call and a DTO projection, and
/// no query-handler pattern exists yet anywhere in this codebase to mirror (docs/plan.md's
/// "Query handlers and gateway implementations are tested only against a real PostgreSQL" is
/// about the test tier, not a mandate for an extra layer on every read). Revisit if a future
/// query grows real logic worth isolating.
/// </summary>
internal static class SectionsEndpoints
{
    public static IEndpointRouteBuilder MapSectionsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sections", CreateSectionAsync).RequireAuthorization("Manager");
        app.MapGet("/api/sections", GetActiveSectionsAsync).RequireAuthorization("FloorStaff");
        app.MapPatch("/api/sections/{id:guid}", UpdateSectionAsync).RequireAuthorization("Manager");
        app.MapPatch("/api/sections/{id:guid}/deactivate", DeactivateSectionAsync).RequireAuthorization("Manager");
        return app;
    }

    private static async Task<IResult> CreateSectionAsync(
        CreateSectionRequest request,
        CreateSectionCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new CreateSectionCommand(request.OperationId, request.Name, request.DisplayOrder, performedByStaffId);

        var sectionId = await handler.HandleAsync(command, cancellationToken);

        var dto = new SectionDto(sectionId, request.Name, request.DisplayOrder, IsActive: true);
        return Results.Created($"/api/sections/{sectionId}", dto);
    }

    private static async Task<IResult> GetActiveSectionsAsync(ISectionsData sectionsData, CancellationToken cancellationToken)
    {
        var sections = await sectionsData.GetActiveSectionsAsync(cancellationToken);
        return Results.Ok(sections.Select(ToDto).ToArray());
    }

    private static async Task<IResult> UpdateSectionAsync(
        Guid id,
        UpdateSectionRequest request,
        UpdateSectionCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new UpdateSectionCommand(request.OperationId, id, request.Name, request.DisplayOrder, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(ToDto(result.Section));
    }

    private static async Task<IResult> DeactivateSectionAsync(
        Guid id,
        DeactivateSectionRequest request,
        DeactivateSectionCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new DeactivateSectionCommand(request.OperationId, id, performedByStaffId);

        var result = await handler.HandleAsync(command, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(ToDto(result.Section));
    }

    private static SectionDto ToDto(Section section) => new(section.Id, section.Name, section.DisplayOrder, section.IsActive);
}
