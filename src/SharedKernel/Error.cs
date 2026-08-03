namespace SharedKernel;

public record Error
{
    public Error(string Code, string Description, int? Status)
    {
        this.Code = Code;
        this.Description = Description;
        this.Status = Status;
    }

    public string Code { get; }
    public string Description { get; }
    public int? Status { get; }
    public static Error None
        => new(string.Empty, string.Empty, null);

    public static Error BadRequest(string Code, string Description)
        => new(Code, Description, 400);

    public static Error NotFound(string Code, string Description)
        => new(Code, Description, 404);

    public static Error Conflict(string Code, string Description)
        => new(Code, Description, 409);

    public static Error Unauthorized(string Code, string Description)
        => new(Code, Description, 401);

    public static Error Forbidden(string Code, string Description)
        => new(Code, Description, 403);

    public static Error FromException(string Code, string Description)
        => new(Code, Description, 500);

    public static Error Unexpected(string Description)
        => new("Error.Unexpected", Description, 500);
    public override string ToString()
        => $"The Code Is: {Code}, With Message: {Description}, and the status code is: {Status}";
}
public sealed record ValidationError : Error
{
    public ValidationError(Error[] errors)
        : base(
            "Validation.General",
            "One or more validation errors occurred",
            400)
    {
        Errors = errors;
    }

    public Error[] Errors { get; }

    public static ValidationError FromResults(IEnumerable<Result> results) =>
        new(results.Where(r => r.IsFailure).Select(r => r.Error).ToArray());
}
