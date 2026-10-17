using BuildingBlocks.Messaging;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Documents.Application.Features.FileTemplates.Deactivate;

internal sealed class DeactivateFileTemplateCommandValidator : AbstractValidator<DeactivateFileTemplateCommand>
{
    public DeactivateFileTemplateCommandValidator() => RuleFor(x => x.FileTemplateId).NotEmpty();
}

internal sealed class DeactivateFileTemplateCommandHandler(DocumentsDbContext db) : ICommandHandler<DeactivateFileTemplateCommand>
{
    public async Task<Result> HandleAsync(DeactivateFileTemplateCommand command, CancellationToken ct = default)
    {
        FileTemplate? template = await db.FileTemplates.FindAsync([command.FileTemplateId], ct);
        if (template is null)
        {
            return FileTemplateErrors.NotFound;
        }

        template.Deactivate();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
