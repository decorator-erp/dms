using BuildingBlocks.Messaging;
using Crm.Application.Features.Clients;
using Crm.Contracts.Contacts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Contacts;

internal sealed class UpdateCrmContactCommandValidator : AbstractValidator<UpdateCrmContactCommand>
{
    public UpdateCrmContactCommandValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty();
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

internal sealed class UpdateCrmContactCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmContactCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmContactCommand command, CancellationToken ct = default)
    {
        CrmContact? contact = await db.CrmContacts.FindAsync([command.ContactId], ct);
        if (contact is null)
        {
            return CrmContactErrors.NotFound;
        }

        Result active = await ActiveClient.EnsureAsync(db, contact.ClientId, ct);
        if (active.IsFailure)
        {
            return active;
        }

        Result result = contact.Update(
            command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Role),
            command.Email, command.Phone, command.Mobile, command.Notes);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
