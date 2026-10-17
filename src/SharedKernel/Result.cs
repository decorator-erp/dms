namespace SharedKernel;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None || !isSuccess && error == Error.None)
        {
            throw new ArgumentException("invalid argument result!");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success()
        => new(true, Error.None);
    public static Result Failure(Error error)
        => new(false, error);
    public static Result<T> Success<T>(T result)
        => new(result, true, Error.None);
    public static Result<T> Failure<T>(Error error)
        => new(default, false, error);

    public static implicit operator Result(Error error)
        => Failure(error);

}

public class Result<TValue>(TValue? value, bool isSuccess, Error error) : Result(isSuccess, error)
{
    public TValue? Value => IsSuccess
        ? value
        : default;


    public static implicit operator Result<TValue>(TValue value)
        => Success(value);

    public static implicit operator Result<TValue>(Error error)
        => Failure<TValue>(error);
}
