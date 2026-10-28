using BuildingBlocks.Messaging;
using Crm.Application.Features.Clients;
using Crm.Contracts.Contacts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Contacts;

internal sealed class SetPrimaryCrmContactCommandValidator : AbstractValidator<SetPrimaryCrmContactCommand>
{
    public SetPrimaryCrmContactCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
    }
}

/// <summary>Demotes the previous holder in the same transaction — see
/// <c>CreateCrmContactCommandHandler</c>'s own remarks on why this invariant lives at the handler
/// level, not the entity level.</summary>
internal sealed class SetPrimaryCrmContactCommandHandler(CrmDbContext db) : ICommandHandler<SetPrimaryCrmContactCommand>
{
    public async Task<Result> HandleAsync(SetPrimaryCrmContactCommand command, CancellationToken ct = default)
    {
        Result active = await ActiveClient.EnsureAsync(db, command.ClientId, ct);
        if (active.IsFailure)
        {
            return active;
        }

        List<CrmContact> contacts = await db.CrmContacts.Where(c => c.ClientId == command.ClientId).ToListAsync(ct);
        CrmContact? target = contacts.FirstOrDefault(c => c.Id == command.ContactId);
        if (target is null)
        {
            return CrmContactErrors.NotFound;
        }

        foreach (CrmContact? contact in contacts.Where(c => c.IsPrimary && c.Id != command.ContactId))
        {
            contact.ClearPrimary();
        }

        target.MarkPrimary();
        return await ClientWrites.SaveAsync(db, ct);
    }
}
