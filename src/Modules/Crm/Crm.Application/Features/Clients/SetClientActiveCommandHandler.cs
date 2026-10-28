using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
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
        Client? client = await db.Clients.FindAsync([command.ClientId], ct);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        bool wasActive = client.IsActive;
        if (command.IsActive)
        {
            client.Activate();
        }
        else
        {
            client.Deactivate();
        }

        ClientStandingEvents.EnqueueOnChange(db, client, wasActive);
        return await ClientWrites.SaveAsync(db, ct);
    }
}

/// <summary>X-3: a client's portal login follows the client's own active flag. Enqueued in the same save as
/// the change, and only when the flag actually changed, so a repeated deactivate publishes nothing.</summary>
internal static class ClientStandingEvents
{
    public static void EnqueueOnChange(CrmDbContext db, Client client, bool wasActive)
    {
        if (client.IsActive == wasActive || string.IsNullOrWhiteSpace(client.AppUserId))
        {
            return;
        }

        if (client.IsActive)
        {
            db.Outbox().Enqueue(new ClientReactivatedIntegrationEvent(
                Guid.NewGuid(), client.Id, client.AppUserId, DateTimeOffset.UtcNow));
        }
        else
        {
            db.Outbox().Enqueue(new ClientDeactivatedIntegrationEvent(
                Guid.NewGuid(), client.Id, client.AppUserId, DateTimeOffset.UtcNow));
        }
    }
}
