using SharedKernel;

namespace BuildingBlocks.Messaging;

// Error-handling convention: command handlers wrap their body in a try/catch, return a Result
// failure on unexpected exceptions, and roll back any transaction they opened inside the catch.
// Expected business failures are returned as Result errors, never thrown. The global exception
// handler is only the last-resort net.
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken ct = default);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default);
}
