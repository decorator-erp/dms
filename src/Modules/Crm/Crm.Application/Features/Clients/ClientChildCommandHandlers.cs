using System.Linq.Expressions;
using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class AddClientChildCommandValidator : AbstractValidator<AddClientChildCommand>
{
    public AddClientChildCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        ClientChildRules.Apply(this, x => x.Gender, x => x.Age);
    }
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

        if (!client.IsActive)
        {
            return Result.Failure<Guid>(ClientErrors.Inactive);
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
        ClientChildRules.Apply(this, x => x.Gender, x => x.Age);
    }
}

/// <summary>C-4 (LC-L6): the domain's own child rule (<c>ClientChild.Validate</c>), checked up front so it
/// answers like every other field.</summary>
internal static class ClientChildRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Expression<Func<T, char>> gender,
        Expression<Func<T, short>> age)
    {
        validator.RuleFor(gender).Must(g => g is 'M' or 'F').WithMessage("Gender must be 'M' or 'F'.");
        validator.RuleFor(age).InclusiveBetween((short)0, (short)120);
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

        if (!client.IsActive)
        {
            return ClientErrors.Inactive;
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

        if (!client.IsActive)
        {
            return ClientErrors.Inactive;
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
