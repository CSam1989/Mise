using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace Mise.ApiService;

internal sealed partial class ValidationExceptionHandler(ILogger<ValidationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
        {
            return false;
        }

        var errors = validationException.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        LogValidationRejected(httpContext.Request.Path.ToString(), errors.Count);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.ValidationProblem(errors).ExecuteAsync(httpContext);

        return true;
    }

    [LoggerMessage(EventId = 30, Level = LogLevel.Debug,
        Message = "Request to {Path} rejected: validation failed on {FieldCount} field(s).")]
    private partial void LogValidationRejected(string path, int fieldCount);
}
