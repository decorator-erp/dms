using BuildingBlocks.Messaging;
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
    }
}

internal sealed class UpdateCrmContactCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmContactCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmContactCommand command, CancellationToken ct = default)
    {
        var contact = await db.CrmContacts.FindAsync([command.ContactId], ct);
        if (contact is null)
            return CrmContactErrors.NotFound;

        var result = contact.Update(
            command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Role),
            command.Email, command.Phone, command.Mobile, command.Notes);
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
