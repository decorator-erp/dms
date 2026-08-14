using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class SetClientActiveCommandValidator : AbstractValidator<SetClientActiveCommand>
{
    public SetClientActiveCommandValidator() => RuleFor(x => x.ClientId).NotEmpty();
}

internal sealed class SetClientActiveCommandHandler(CrmDbContext db) : ICommandHandler<SetClientActiveCommand>
{
    public async Task<Result> HandleAsync(SetClientActiveCommand command, CancellationToken ct = default)
    {
        var client = await db.Clients.FindAsync([command.ClientId], ct);
        if (client is null)
            return ClientErrors.NotFound;

        if (command.IsActive) client.Activate(); else client.Deactivate();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
