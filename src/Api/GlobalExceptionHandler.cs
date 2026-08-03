using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api;

/// <summary>Ported from <c>../backend/API/Infrastructure/GlobalExceptionHandler.cs</c> — last-resort
/// net; command/query handlers are expected to catch their own exceptions and return a
/// <c>Result</c> failure (see the doc comment on <c>ICommandHandler</c>).</summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "An unhandled exception occurred");

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server Error",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            },
            cancellationToken);

        return true;
    }
}
