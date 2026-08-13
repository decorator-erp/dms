using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.FileTemplates.Create;

internal sealed class CreateFileTemplateCommandValidator : AbstractValidator<CreateFileTemplateCommand>
{
    public CreateFileTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Category).NotEmpty().Must(BeAKnownCategory);
        RuleFor(x => x.FileName).NotEmpty();
    }

    private static bool BeAKnownCategory(string category) => Enum.TryParse<FileTemplateCategory>(category, out _);
}

/// <summary>Same upload shape every document-upload command uses (G7-07): validate, save the blob, THEN
/// insert the row, with a compensating delete on commit failure — the blob is written before the row
/// exists, matching the backend's own ordering.</summary>
internal sealed class CreateFileTemplateCommandHandler(DocumentsDbContext db, IFileStorageService fileStorage)
    : ICommandHandler<CreateFileTemplateCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateFileTemplateCommand command, CancellationToken ct = default)
    {
        var nameExists = await db.FileTemplates.AsNoTracking().AnyAsync(t => t.Name == command.Name, ct);
        if (nameExists)
            return FileTemplateErrors.NameAlreadyExists;

        if (command.FileSizeBytes <= 0)
            return FileTemplateErrors.InvalidFileSize;

        var category = Enum.Parse<FileTemplateCategory>(command.Category);
        var storagePath = await fileStorage.SaveAsync(command.Content, command.FileName, "file-templates", ct);

        var templateResult = FileTemplate.Create(
            command.Name, category, command.FileName, storagePath, command.ContentType,
            command.FileSizeBytes, command.Description);
        if (templateResult.IsFailure)
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            return Result.Failure<Guid>(templateResult.Error);
        }

        var template = templateResult.Value!;
        db.FileTemplates.Add(template);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            throw;
        }

        return template.Id;
    }
}
