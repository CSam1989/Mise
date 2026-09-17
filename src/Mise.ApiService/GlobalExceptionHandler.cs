using Microsoft.AspNetCore.Diagnostics;

namespace Mise.ApiService;

/// <summary>
/// The catch-all safety net every other <see cref="IExceptionHandler"/> falls through to.
/// Registered after <see cref="ValidationExceptionHandler"/> (handlers run in registration
/// order; the first to return true wins) — anything not a validation failure lands here:
/// logged with the full exception at Error level (never leaked to the response — CLAUDE.md's
/// "never leak internals" rule applies to more than just secrets), and the caller gets a
/// generic 500 Problem Details body carrying only a trace id, which is what a bug report can
/// actually be matched back to a log line with.
/// </summary>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandledException(exception, httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(
            title: "An unexpected error occurred.",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier }
        ).ExecuteAsync(httpContext);

        return true;
    }

    [LoggerMessage(EventId = 31, Level = LogLevel.Error,
        Message = "Unhandled exception processing {Method} {Path}.")]
    private partial void LogUnhandledException(Exception exception, string method, string path);
}
