using Microsoft.AspNetCore.Diagnostics;
using Mise.SharedKernel;

namespace Mise.ApiService;

/// <summary>docs/plan.md correction #1's "a violation surfaces as a clean 409 with a friendly
/// message rather than a raw constraint-violation exception."</summary>
internal sealed partial class ReservationOverlapExceptionHandler(ILogger<ReservationOverlapExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ReservationOverlapException overlapException)
        {
            return false;
        }

        LogReservationOverlap(overlapException.TableId);

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await Results.Problem(
            title: "This table already has an overlapping reservation for the requested time.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["tableId"] = overlapException.TableId }
        ).ExecuteAsync(httpContext);

        return true;
    }

    [LoggerMessage(EventId = 80, Level = LogLevel.Warning,
        Message = "BR-01 overlap rejected for table {TableId}.")]
    private partial void LogReservationOverlap(Guid tableId);
}
