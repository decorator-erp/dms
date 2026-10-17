using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class UpdateClientProfileCommandValidator : AbstractValidator<UpdateClientProfileCommand>
{
    public UpdateClientProfileCommandValidator() => RuleFor(x => x.ClientId).NotEmpty();
}

internal sealed class UpdateClientProfileCommandHandler(CrmDbContext db) : ICommandHandler<UpdateClientProfileCommand>
{
    public async Task<Result> HandleAsync(UpdateClientProfileCommand command, CancellationToken ct = default)
    {
        Client? client = await db.Clients.FindAsync([command.ClientId], ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        client.UpdateProfile(command.JobTitle, command.MaritalStatus, command.RepresentativeName);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
