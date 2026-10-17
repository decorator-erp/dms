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
            ValidationFailure[] validationFailures = await ValidateAsync(command, Validators);
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
            ValidationFailure[] validationFailures = await ValidateAsync(command, Validators);
            if (validationFailures.Length == 0)
            {
                return await innerHandler.HandleAsync(command, ct);
            }

            return CreateValidationError(validationFailures);
        }
    }
    private static async Task<ValidationFailure[]> ValidateAsync<TCommand>(
        TCommand command,
        IEnumerable<IValidator> validators)
    {
        if (!validators.Any())
        {
            return [];
        }

        var context = new ValidationContext<TCommand>(command);

        ValidationResult[] validationResult = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context)));

        ValidationFailure[] validationFailures = [.. validationResult
            .Where(x => !x.IsValid)
            .SelectMany(x => x.Errors)];

        return validationFailures;
    }
    private static ValidationError CreateValidationError(ValidationFailure[] validationFailures) =>
        new([.. validationFailures.Select(f => Error.BadRequest(f.PropertyName, f.ErrorMessage))]);
}
