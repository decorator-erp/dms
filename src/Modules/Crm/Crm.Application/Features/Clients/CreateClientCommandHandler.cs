using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Identity.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Clients;

internal sealed class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.Address).NotEmpty();
    }
}

/// <summary>Direct creation path — the other is lead conversion (<c>ConvertCrmLeadCommandHandler</c>),
/// which creates the row internally. Validates <see cref="CreateClientCommand.AppUserId"/> against
/// <see cref="IUserDirectory"/> before creating the row — the application-code check that stands where
/// <c>crm.clients.AppUserId</c>'s own hard FK (G9-08) would otherwise surface as a raw Postgres
/// exception on <c>SaveChangesAsync</c> instead of a graceful <see cref="Result"/> failure.</summary>
internal sealed class CreateClientCommandHandler(CrmDbContext db, IUserDirectory userDirectory)
    : ICommandHandler<CreateClientCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateClientCommand command, CancellationToken ct = default)
    {
        var user = await userDirectory.GetAsync(command.AppUserId, ct);
        if (user is null)
            return ClientErrors.AppUserIdRequired;

        var clientResult = Client.Create(command.AppUserId, command.Address, command.Notes);
        if (clientResult.IsFailure)
            return Result.Failure<Guid>(clientResult.Error);

        var client = clientResult.Value!;
        db.Clients.Add(client);
        db.Outbox().Enqueue(new ClientCreatedIntegrationEvent(
            Guid.NewGuid(), client.Id, client.AppUserId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        return client.Id;
    }
}
