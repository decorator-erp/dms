using BuildingBlocks.Messaging;
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
    }
}

/// <summary>A newly-created primary contact demotes the client's previous primary in the same
/// transaction — the invariant <c>CrmContactErrors</c> alone can't enforce, since it lives on two rows,
/// mirroring the backend's <c>CrmContactDomainService</c>.</summary>
internal sealed class CreateCrmContactCommandHandler(CrmDbContext db) : ICommandHandler<CreateCrmContactCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmContactCommand command, CancellationToken ct = default)
    {
        var clientExists = await db.Clients.AnyAsync(c => c.Id == command.ClientId, ct);
        if (!clientExists)
            return Result.Failure<Guid>(ClientErrors.NotFound);

        var contactResult = CrmContact.Create(
            command.ClientId, command.FirstName, command.LastName, CrmEnumMapping.ToDomain(command.Role),
            command.IsPrimary, command.Email, command.Phone, command.Mobile, command.Notes);
        if (contactResult.IsFailure)
            return Result.Failure<Guid>(contactResult.Error);

        var contact = contactResult.Value!;

        if (command.IsPrimary)
        {
            var previousPrimary = await db.CrmContacts
                .Where(c => c.ClientId == command.ClientId && c.IsPrimary)
                .ToListAsync(ct);
            foreach (var previous in previousPrimary)
                previous.ClearPrimary();
        }

        db.CrmContacts.Add(contact);
        await db.SaveChangesAsync(ct);

        return contact.Id;
    }
}
