using BuildingBlocks.Messaging;
using Crm.Application.Features.Clients;
using Crm.Contracts.Notes;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
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

internal sealed class CreateCrmClientNoteCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CreateCrmClientNoteCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmClientNoteCommand command, CancellationToken ct = default)
    {
        Result active = await ActiveClient.EnsureAsync(db, command.ClientId, ct);
        if (active.IsFailure)
        {
            return Result.Failure<Guid>(active.Error);
        }

        // C-7 (LC-L3): a note may point only at one of its client's own projects.
        if (command.ProjectId is { } projectId
            && (await projectDirectory.GetProjectAsync(projectId, ct))?.ClientId != command.ClientId)
        {
            return Result.Failure<Guid>(CrmClientNoteErrors.ProjectMismatch);
        }

        var note = CrmClientNote.Create(command.ClientId, command.ProjectId, command.Content, command.WrittenById);
        db.CrmClientNotes.Add(note);
        Result saved = await ClientWrites.SaveAsync(db, ct);
        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : note.Id;
    }
}
