using Mise.Modules.StaffIdentity.Application.Login;

namespace Mise.ApiService.Staff;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, LoginCommandHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Username, request.Password), cancellationToken);

        if (result.Outcome != LoginOutcome.Success)
        {
            // Deliberately the same 401 regardless of unknown-username vs wrong-password vs
            // inactive account — distinguishing them in the response would let a caller
            // enumerate valid usernames.
            return Results.Unauthorized();
        }

        return Results.Ok(new LoginResponse(
            result.Token, result.ExpiresAtUtc, result.StaffUserId, result.FullName, result.Role.ToString()));
    }
}

internal sealed record LoginResponse(
    string Token, DateTimeOffset ExpiresAtUtc, Guid StaffUserId, string FullName, string Role);
