using BuildingBlocks.Messaging;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.FileTemplates.Update;

internal sealed class UpdateFileTemplateCommandValidator : AbstractValidator<UpdateFileTemplateCommand>
{
    public UpdateFileTemplateCommandValidator()
    {
        RuleFor(x => x.FileTemplateId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Category).NotEmpty().Must(c => Enum.TryParse<FileTemplateCategory>(c, out _));
    }
}

/// <summary>Cannot change the stored file's bytes — matches <c>FileTemplate.Update</c> exactly.</summary>
internal sealed class UpdateFileTemplateCommandHandler(DocumentsDbContext db) : ICommandHandler<UpdateFileTemplateCommand>
{
    public async Task<Result> HandleAsync(UpdateFileTemplateCommand command, CancellationToken ct = default)
    {
        FileTemplate? template = await db.FileTemplates.FindAsync([command.FileTemplateId], ct);
        if (template is null)
        {
            return FileTemplateErrors.NotFound;
        }

        bool nameExists = await db.FileTemplates.AsNoTracking()
            .AnyAsync(t => t.Id != command.FileTemplateId && t.Name == command.Name, ct);

        FileTemplateCategory category = Enum.Parse<FileTemplateCategory>(command.Category);
        Result updateResult = template.Update(command.Name, category, command.Description, nameExists);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
