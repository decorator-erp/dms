using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class DeleteClientCommandValidator : AbstractValidator<DeleteClientCommand>
{
    public DeleteClientCommandValidator() => RuleFor(x => x.ClientId).NotEmpty();
}

/// <summary>Soft delete only, matching the module-wide convention (<c>Entity.IsActive = false</c>) —
/// deactivation, not a row removal, since other rows (contacts, notes, meetings) reference this
/// client.</summary>
internal sealed class DeleteClientCommandHandler(CrmDbContext db) : ICommandHandler<DeleteClientCommand>
{
    public async Task<Result> HandleAsync(DeleteClientCommand command, CancellationToken ct = default)
    {
        Client? client = await db.Clients.FindAsync([command.ClientId], ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        client.Deactivate();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
