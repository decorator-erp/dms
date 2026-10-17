using Microsoft.AspNetCore.Http;
using SharedKernel;

namespace BuildingBlocks.Web.Results;

/// <summary>
/// Moved here from <c>Api/Results</c> at G1-07 — every module's endpoints need to turn a
/// <see cref="Result"/>/<see cref="Result{TValue}"/> into an <see cref="IResult"/>, not just the
/// composition root's own ad hoc routes, and a module's <c>*.Infrastructure</c> project cannot
/// reference <c>Api</c> (the composition root depends on modules, never the reverse). Originally ported
/// from <c>../backend/API/Infrastructure/CustomResults.cs</c> and
/// <c>../backend/API/Extensions/ResultExtensions.cs</c>, with the reflection hop the backend's
/// <c>CustomResults</c> used to attach the <c>errors</c> extension removed — <c>Results.Problem</c>
/// already takes an <c>extensions</c> dictionary directly.
/// </summary>
public static class ResultHttpExtensions
{
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Result, TOut> onFailure) =>
        result.IsSuccess ? onSuccess() : onFailure(result);

    public static TOut Match<TIn, TOut>(
        this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Result<TIn>, TOut> onFailure) =>
        result.IsSuccess ? onSuccess(result.Value!) : onFailure(result);

    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Cannot convert a successful result to a problem.");
        }

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
