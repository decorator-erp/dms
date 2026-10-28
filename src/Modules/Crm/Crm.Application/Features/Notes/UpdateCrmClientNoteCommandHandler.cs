using BuildingBlocks;
using BuildingBlocks.Messaging;
using Crm.Contracts.Notes;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Notes;

internal sealed class UpdateCrmClientNoteCommandValidator : AbstractValidator<UpdateCrmClientNoteCommand>
{
    public UpdateCrmClientNoteCommandValidator()
    {
        RuleFor(x => x.NoteId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.ActingEmployeeId).NotEmpty();
    }
}

internal sealed class UpdateCrmClientNoteCommandHandler(CrmDbContext db, ICurrentUser currentUser) : ICommandHandler<UpdateCrmClientNoteCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmClientNoteCommand command, CancellationToken ct = default)
    {
        CrmClientNote? note = await db.CrmClientNotes.FindAsync([command.NoteId], ct);
        if (note is null)
        {
            return CrmClientNoteErrors.NotFound;
        }

        Result mayChange = NoteAuthorship.EnsureMayChange(note, command.ActingEmployeeId, currentUser);
        if (mayChange.IsFailure)
        {
            return mayChange;
        }

        note.Update(command.Content);
        return await ClientWrites.SaveAsync(db, ct);
    }
}
