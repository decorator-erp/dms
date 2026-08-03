using SharedKernel;

namespace Api.Results;

/// <summary>Ported from <c>../backend/API/Extensions/ResultExtensions.cs</c>.</summary>
public static class ResultExtensions
{
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Result, TOut> onFailure) =>
        result.IsSuccess ? onSuccess() : onFailure(result);

    public static TOut Match<TIn, TOut>(
        this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Result<TIn>, TOut> onFailure) =>
        result.IsSuccess ? onSuccess(result.Value!) : onFailure(result);
}
