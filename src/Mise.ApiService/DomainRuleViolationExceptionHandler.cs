using Microsoft.AspNetCore.Diagnostics;
using Mise.SharedKernel;

namespace Mise.ApiService;

/// <summary>
/// The message is deliberately shown to the caller — like FluentValidation's ValidationException,
/// DomainRuleViolationException's message is authored to be user-facing (e.g. "Cannot deactivate
/// table 'T12' while its status is Occupied."), never an internal detail (CLAUDE.md's Error
/// handling rules on what a generic handler may and may not leak don't apply here — this isn't
/// the generic catch-all, it's a specific, expected-outcome handler).
/// </summary>
internal sealed partial class DomainRuleViolationExceptionHandler(ILogger<DomainRuleViolationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainRuleViolationException domainRuleViolationException)
        {
            return false;
        }

        LogDomainRuleViolation(httpContext.Request.Path.ToString(), domainRuleViolationException.Message);

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await Results.Problem(
            title: "The request conflicts with the current state of the resource.",
            detail: domainRuleViolationException.Message,
            statusCode: StatusCodes.Status409Conflict
        ).ExecuteAsync(httpContext);

        return true;
    }

    [LoggerMessage(EventId = 37, Level = LogLevel.Debug, Message = "Request to {Path} rejected: {Reason}")]
    private partial void LogDomainRuleViolation(string path, string reason);
}
