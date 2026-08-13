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

        // G7-08: a multipart body over FormOptions.MultipartBodyLengthLimit (Documents' own upload
        // hardening) surfaces as Microsoft.AspNetCore.Http.BadHttpRequestException while the framework's
        // own request-delegate form binder is reading the body — before any handler runs, so there is no
        // Result to fail with. That exception type carries the framework's own correct StatusCode (413 for
        // a too-large body); forcing 500 for every exception, as this handler did before, was overriding a
        // status the framework had already determined correctly. Everything else still maps to 500.
        var statusCode = exception is Microsoft.AspNetCore.Http.BadHttpRequestException badHttpRequest
            ? badHttpRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = statusCode == StatusCodes.Status500InternalServerError ? "Server Error" : "Bad Request",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            },
            cancellationToken);

        return true;
    }
}
