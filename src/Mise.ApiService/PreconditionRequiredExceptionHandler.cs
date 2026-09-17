using Microsoft.AspNetCore.Diagnostics;

namespace Mise.ApiService;

internal sealed partial class PreconditionRequiredExceptionHandler(ILogger<PreconditionRequiredExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not PreconditionRequiredException preconditionRequiredException)
        {
            return false;
        }

        LogPreconditionRequired(httpContext.Request.Path.ToString());

        httpContext.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
        await Results.Problem(
            title: "An If-Match header carrying the resource's current version is required.",
            detail: preconditionRequiredException.Message,
            statusCode: StatusCodes.Status428PreconditionRequired
        ).ExecuteAsync(httpContext);

        return true;
    }

    [LoggerMessage(EventId = 35, Level = LogLevel.Debug,
        Message = "Request to {Path} rejected: missing or malformed If-Match header.")]
    private partial void LogPreconditionRequired(string path);
}
