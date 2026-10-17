using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class AddClientChildCommandValidator : AbstractValidator<AddClientChildCommand>
{
    public AddClientChildCommandValidator() => RuleFor(x => x.ClientId).NotEmpty();
}

internal sealed class AddClientChildCommandHandler(CrmDbContext db) : ICommandHandler<AddClientChildCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AddClientChildCommand command, CancellationToken ct = default)
    {
        Client? client = await db.Clients.FindAsync([command.ClientId], ct);
        if (client is null)
        {
            return Result.Failure<Guid>(ClientErrors.NotFound);
        }

        Result<Guid> result = client.AddChild(command.Gender, command.Age);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await db.SaveChangesAsync(ct);

        return result.Value;
    }
}

internal sealed class UpdateClientChildCommandValidator : AbstractValidator<UpdateClientChildCommand>
{
    public UpdateClientChildCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ChildId).NotEmpty();
    }
}

internal sealed class UpdateClientChildCommandHandler(CrmDbContext db) : ICommandHandler<UpdateClientChildCommand>
{
    public async Task<Result> HandleAsync(UpdateClientChildCommand command, CancellationToken ct = default)
    {
        Client? client = await db.Clients.FindAsync([command.ClientId], ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        Result result = client.UpdateChild(command.ChildId, command.Gender, command.Age);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class RemoveClientChildCommandValidator : AbstractValidator<RemoveClientChildCommand>
{
    public RemoveClientChildCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ChildId).NotEmpty();
    }
}

internal sealed class RemoveClientChildCommandHandler(CrmDbContext db) : ICommandHandler<RemoveClientChildCommand>
{
    public async Task<Result> HandleAsync(RemoveClientChildCommand command, CancellationToken ct = default)
    {
        Client? client = await db.Clients.FindAsync([command.ClientId], ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        Result result = client.RemoveChild(command.ChildId);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
