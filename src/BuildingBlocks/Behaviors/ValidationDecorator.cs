using BuildingBlocks.Messaging;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;

namespace BuildingBlocks.Behaviors;

internal sealed class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        IEnumerable<IValidator<TCommand>> Validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            ValidationFailure[] validationFailures = await ValidateAsync(command, Validators, ct);
            if (validationFailures.Length == 0)
            {
                return await innerHandler.HandleAsync(command, ct);
            }

            return CreateValidationError(validationFailures);
        }
    }
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        IEnumerable<IValidator<TCommand>> Validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            ValidationFailure[] validationFailures = await ValidateAsync(command, Validators, ct);
            if (validationFailures.Length == 0)
            {
                return await innerHandler.HandleAsync(command, ct);
            }

            return CreateValidationError(validationFailures);
        }
    }
    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler,
        IEnumerable<IValidator<TQuery>> Validators)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            ValidationFailure[] validationFailures = await ValidateAsync(query, Validators, ct);
            if (validationFailures.Length == 0)
            {
                return await innerHandler.HandleAsync(query, ct);
            }

            return CreateValidationError(validationFailures);
        }
    }
    private static async Task<ValidationFailure[]> ValidateAsync<TRequest>(
        TRequest request,
        IEnumerable<IValidator> validators,
        CancellationToken ct)
    {
        if (!validators.Any())
        {
            return [];
        }

        var context = new ValidationContext<TRequest>(request);

        // Sequential, not Task.WhenAll: validators are scoped and an async rule (MustAsync) typically
        // queries the module's scoped DbContext, which is not thread-safe - two validators awaiting at
        // once is "a second operation was started on this context instance". It also keeps the order of
        // the reported failures stable. Validation is cheap next to the handler; the parallelism bought
        // nothing worth that.
        List<ValidationFailure> validationFailures = [];
        foreach (IValidator validator in validators)
        {
            ValidationResult validationResult = await validator.ValidateAsync(context, ct);
            if (!validationResult.IsValid)
            {
                validationFailures.AddRange(validationResult.Errors);
            }
        }

        return [.. validationFailures];
    }
    private static ValidationError CreateValidationError(ValidationFailure[] validationFailures) =>
        new([.. validationFailures.Select(f => Error.BadRequest(f.PropertyName, f.ErrorMessage))]);
}
