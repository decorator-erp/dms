using BuildingBlocks.Messaging;
using Crm.Application.Features.Clients;
using Crm.Contracts.Contacts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Contacts;

internal sealed class CreateCrmContactCommandValidator : AbstractValidator<CreateCrmContactCommand>
{
    public CreateCrmContactCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.LastName).NotEmpty();
        // C-4 (LC-M4): the column lengths, a real email address and a defined role.
        RuleFor(x => x.FirstName).MaximumLength(128);
        RuleFor(x => x.LastName).MaximumLength(128);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.Mobile).MaximumLength(32);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Role).IsInEnum();
    }
}

/// <summary>A newly-created primary contact demotes the client's previous primary in the same
/// transaction — the invariant <c>CrmContactErrors</c> alone can't enforce, since it lives on two rows,
/// mirroring the backend's <c>CrmContactDomainService</c>.</summary>
internal sealed class CreateCrmContactCommandHandler(CrmDbContext db) : ICommandHandler<CreateCrmContactCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmContactCommand command, CancellationToken ct = default)
    {
        Result active = await ActiveClient.EnsureAsync(db, command.ClientId, ct);
        if (active.IsFailure)
        {
            return Result.Failure<Guid>(active.Error);
        }

        Result<CrmContact> contactResult = CrmContact.Create(
            command.ClientId, command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Role),
            command.IsPrimary, command.Email, command.Phone, command.Mobile, command.Notes);
        if (contactResult.IsFailure)
        {
            return Result.Failure<Guid>(contactResult.Error);
        }

        CrmContact contact = contactResult.Value!;

        if (command.IsPrimary)
        {
            List<CrmContact> previousPrimary = await db.CrmContacts
                .Where(c => c.ClientId == command.ClientId && c.IsPrimary)
                .ToListAsync(ct);
            foreach (CrmContact? previous in previousPrimary)
            {
                previous.ClearPrimary();
            }
        }

        db.CrmContacts.Add(contact);
        await db.SaveChangesAsync(ct);

        return contact.Id;
    }
}
