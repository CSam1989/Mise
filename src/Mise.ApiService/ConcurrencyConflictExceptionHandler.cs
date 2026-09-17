using Microsoft.AspNetCore.Diagnostics;
using Mise.SharedKernel;

namespace Mise.ApiService;

/// <summary>
/// docs/plan.md correction #5's "stale value → 409 with the current state." The current
/// version travels in the JSON body's <c>currentVersion</c> extension, **not** a response ETag
/// header: ASP.NET Core's <c>ExceptionHandlerMiddleware</c> registers an <c>OnStarting</c>
/// callback (<c>ClearCacheHeaders</c>) on every response it processes that unconditionally
/// strips <c>ETag</c> (along with setting Cache-Control/Pragma/Expires) as anti-caching
/// hardening for error pages — it runs *after* any <see cref="IExceptionHandler"/>, so setting
/// the header here is silently undone before the response is ever sent. This only affects
/// exception-handler responses; the success path's <c>Results.Ok/Created</c> ETag (see
/// TablesEndpoints) is unaffected because no exception was thrown for those.
/// </summary>
internal sealed partial class ConcurrencyConflictExceptionHandler(ILogger<ConcurrencyConflictExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ConcurrencyConflictException concurrencyConflictException)
        {
            return false;
        }

        LogConcurrencyConflict(concurrencyConflictException.EntityType, concurrencyConflictException.EntityId);

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await Results.Problem(
            title: $"{concurrencyConflictException.EntityType} was modified by someone else since it was last read.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["entityId"] = concurrencyConflictException.EntityId,
                ["currentVersion"] = ETag.Format(concurrencyConflictException.CurrentVersion),
                ["currentState"] = concurrencyConflictException.CurrentState,
            }
        ).ExecuteAsync(httpContext);

        return true;
    }

    [LoggerMessage(EventId = 36, Level = LogLevel.Warning,
        Message = "Concurrency conflict on {EntityType} {EntityId}: caller's If-Match version no longer matches.")]
    private partial void LogConcurrencyConflict(string entityType, Guid entityId);
}
