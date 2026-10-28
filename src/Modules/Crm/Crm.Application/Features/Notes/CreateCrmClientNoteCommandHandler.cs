using BuildingBlocks.Messaging;
using Crm.Application.Features.Clients;
using Crm.Contracts.Notes;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Notes;

internal sealed class CreateCrmClientNoteCommandValidator : AbstractValidator<CreateCrmClientNoteCommand>
{
    public CreateCrmClientNoteCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.WrittenById).NotEmpty();
    }
}

internal sealed class CreateCrmClientNoteCommandHandler(CrmDbContext db) : ICommandHandler<CreateCrmClientNoteCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmClientNoteCommand command, CancellationToken ct = default)
    {
        Result active = await ActiveClient.EnsureAsync(db, command.ClientId, ct);
        if (active.IsFailure)
        {
            return Result.Failure<Guid>(active.Error);
        }

        var note = CrmClientNote.Create(command.ClientId, command.ProjectId, command.Content, command.WrittenById);
        db.CrmClientNotes.Add(note);
        await db.SaveChangesAsync(ct);

        return note.Id;
    }
}
