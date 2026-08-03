using SharedKernel;

namespace Api.Results;

/// <summary>
/// Ported from <c>../backend/API/Infrastructure/CustomResults.cs</c>, with the reflection hop the
/// original used to attach the <c>errors</c> extension removed — <c>Results.Problem</c> already takes
/// an <c>extensions</c> dictionary directly, so building a <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/>
/// by hand and reflecting a property off it back out was unnecessary. Also moved out of the global
/// namespace the backend left it in.
/// </summary>
public static class CustomResults
{
    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("Cannot convert a successful result to a problem.");

        Dictionary<string, object?> errors = result.Error is ValidationError validationError
            ? validationError.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, object? (g) => g.Select(e => e.Description).ToList())
            : new Dictionary<string, object?> { [result.Error.Code] = new List<string> { result.Error.Description } };

        return Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: result.Error.Status ?? StatusCodes.Status400BadRequest,
            title: result.Error.Code,
            detail: result.Error.Description,
            extensions: new Dictionary<string, object?> { ["errors"] = errors });
    }
}
