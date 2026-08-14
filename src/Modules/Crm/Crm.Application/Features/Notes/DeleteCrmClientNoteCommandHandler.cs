using BuildingBlocks.Messaging;
using Crm.Contracts.Notes;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Notes;

internal sealed class DeleteCrmClientNoteCommandValidator : AbstractValidator<DeleteCrmClientNoteCommand>
{
    public DeleteCrmClientNoteCommandValidator() => RuleFor(x => x.NoteId).NotEmpty();
}

internal sealed class DeleteCrmClientNoteCommandHandler(CrmDbContext db) : ICommandHandler<DeleteCrmClientNoteCommand>
{
    public async Task<Result> HandleAsync(DeleteCrmClientNoteCommand command, CancellationToken ct = default)
    {
        var note = await db.CrmClientNotes.FindAsync([command.NoteId], ct);
        if (note is null)
            return CrmClientNoteErrors.NotFound;

        db.CrmClientNotes.Remove(note);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
