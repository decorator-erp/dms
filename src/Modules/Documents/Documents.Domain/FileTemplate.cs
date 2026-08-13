using SharedKernel;

namespace Documents.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/FileTemplate.cs</c>. No navigations — a standalone,
/// project-independent library entry. <see cref="Name"/> is globally unique across all templates
/// (checked in Application via a DB query, same "entity holds the pure state machine" split as
/// <see cref="DocumentFolder"/>'s own remarks — this entity has no way to see other rows).
/// <see cref="Update"/> cannot change the stored file's bytes, matching the backend exactly — replacing
/// the file itself means deleting the template and creating a new one. Active/inactive is
/// <see cref="SharedKernel.Entity.IsActive"/> via <see cref="Activate"/>/<see cref="Deactivate"/>, not a
/// property of its own.
/// </summary>
internal sealed class FileTemplate : Entity, IAuditable
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public FileTemplateCategory Category { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StoragePath { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }

    private FileTemplate()
    {
        // EF materialization.
    }

    /// <summary><paramref name="nameAlreadyExists"/> is the caller's own uniqueness pre-check, passed in
    /// rather than queried here so this factory stays pure and unit-testable — same pattern
    /// <see cref="DocumentFolder.Create"/> uses for its own default-folder check.</summary>
    public static Result<FileTemplate> Create(
        string name, FileTemplateCategory category, string fileName, string storagePath,
        string contentType, long fileSizeBytes, string? description = null, bool nameAlreadyExists = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FileTemplateErrors.NameRequired;

        if (nameAlreadyExists)
            return FileTemplateErrors.NameAlreadyExists;

        if (fileSizeBytes <= 0)
            return FileTemplateErrors.InvalidFileSize;

        return new FileTemplate
        {
            Name = name,
            Description = description,
            Category = category,
            FileName = fileName,
            StoragePath = storagePath,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes = fileSizeBytes,
        };
    }

    public Result Update(string name, FileTemplateCategory category, string? description, bool nameAlreadyExists = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FileTemplateErrors.NameRequired;

        if (nameAlreadyExists && !string.Equals(name, Name, StringComparison.Ordinal))
            return FileTemplateErrors.NameAlreadyExists;

        Name = name;
        Category = category;
        Description = description;
        return Result.Success();
    }

    public void Activate() => SetActive(true);

    public void Deactivate() => SetActive(false);
}
