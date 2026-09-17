using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using Mise.Modules.StaffIdentity.Application.RegisterStaff;
using Mise.Modules.StaffIdentity.Contracts;
using Mise.Modules.StaffIdentity.Domain;

namespace Mise.ApiService.Staff;

internal static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/staff", RegisterStaffAsync).RequireAuthorization("Manager");
        return app;
    }

    private static async Task<IResult> RegisterStaffAsync(
        RegisterStaffRequest request,
        RegisterStaffCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // A malformed Role string is a wire-contract concern the endpoint itself is
        // responsible for, not the domain enum — routed through the same ValidationException
        // -> 400 pipeline every other field-scoped validation failure uses, so the response
        // shape stays consistent regardless of which layer caught the problem.
        if (!Enum.TryParse<StaffRole>(request.Role, ignoreCase: true, out var role))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Role), "Role must be FloorStaff or Manager.")]);
        }

        // The Manager policy already proved this claim's role server-side; PerformedByStaffId
        // is only for attributing the resulting audit entry to that Manager.
        var performedByStaffId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var command = new RegisterStaffCommand(
            request.OperationId, request.Username, request.Password, request.FullName, role, performedByStaffId);

        var staffUserId = await handler.HandleAsync(command, cancellationToken);

        var dto = new StaffSummaryDto(staffUserId, request.Username, request.FullName, role.ToString());

        return Results.Created($"/api/staff/{staffUserId}", dto);
    }
}
