using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace BuildingBlocks.Behaviors;

internal sealed class LoggingDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        ILogger<CommandHandler<TCommand, TResponse>> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            string requestName = typeof(TCommand).Name;

            logger.LogInformation(LogMessages.Command_Handling, requestName);

            Result<TResponse> result = await innerHandler.HandleAsync(command, ct);
            if (result.IsSuccess)
            {
                logger.LogInformation(LogMessages.Command_Succeeded, requestName);
            }
            else
            {
                logger.LogWarning(LogMessages.Command_Failed, requestName, result.Error.Description);
            }
            return result;
        }
    }
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        ILogger<CommandHandler<TCommand>> logger)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            string requestName = typeof(TCommand).Name;

            logger.LogInformation(LogMessages.Command_Handling, requestName);

            Result result = await innerHandler.HandleAsync(command, ct);
            if (result.IsSuccess)
            {
                logger.LogInformation(LogMessages.Command_Succeeded, requestName);
            }
            else
            {
                logger.LogWarning(LogMessages.Command_Failed, requestName, result.Error.Description);
            }
            return result;
        }
    }
    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler,
        ILogger<QueryHandler<TQuery, TResponse>> logger)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery command, CancellationToken ct = default)
        {
            string requestName = typeof(TQuery).Name;

            logger.LogInformation(LogMessages.Query_Handling, requestName);

            Result<TResponse> result = await innerHandler.HandleAsync(command, ct);
            if (result.IsSuccess)
            {
                logger.LogInformation(LogMessages.Query_Succeeded, requestName);
            }
            else
            {
                logger.LogWarning(LogMessages.Query_Failed, requestName, result.Error.Description);
            }
            return result;
        }
    }

}
