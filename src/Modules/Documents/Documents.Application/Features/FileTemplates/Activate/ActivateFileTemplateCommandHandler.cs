using BuildingBlocks.Messaging;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Documents.Application.Features.FileTemplates.Activate;

internal sealed class ActivateFileTemplateCommandValidator : AbstractValidator<ActivateFileTemplateCommand>
{
    public ActivateFileTemplateCommandValidator() => RuleFor(x => x.FileTemplateId).NotEmpty();
}

internal sealed class ActivateFileTemplateCommandHandler(DocumentsDbContext db) : ICommandHandler<ActivateFileTemplateCommand>
{
    public async Task<Result> HandleAsync(ActivateFileTemplateCommand command, CancellationToken ct = default)
    {
        var template = await db.FileTemplates.FindAsync([command.FileTemplateId], ct);
        if (template is null)
            return FileTemplateErrors.NotFound;

        template.Activate();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
