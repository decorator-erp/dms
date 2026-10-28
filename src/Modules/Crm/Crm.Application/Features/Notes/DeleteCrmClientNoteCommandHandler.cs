using BuildingBlocks;
using BuildingBlocks.Messaging;
using Crm.Application.Features.Clients;
using Crm.Contracts.Notes;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Notes;

internal sealed class DeleteCrmClientNoteCommandValidator : AbstractValidator<DeleteCrmClientNoteCommand>
{
    public DeleteCrmClientNoteCommandValidator()
    {
        RuleFor(x => x.NoteId).NotEmpty();
        RuleFor(x => x.ActingEmployeeId).NotEmpty();
    }
}

internal sealed class DeleteCrmClientNoteCommandHandler(CrmDbContext db, ICurrentUser currentUser) : ICommandHandler<DeleteCrmClientNoteCommand>
{
    public async Task<Result> HandleAsync(DeleteCrmClientNoteCommand command, CancellationToken ct = default)
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

        // D-16 / C-F4: a deactivated client's notes are frozen, as every other write on it is (C-5) — a
        // state check, so after authorship (403 before 409).
        Result active = await ActiveClient.EnsureAsync(db, note.ClientId, ct);
        if (active.IsFailure)
        {
            return active;
        }

        db.CrmClientNotes.Remove(note);
        return await ClientWrites.SaveAsync(db, ct);
    }
}
