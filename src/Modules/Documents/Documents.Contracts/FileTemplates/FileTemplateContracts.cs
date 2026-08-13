using BuildingBlocks.Messaging;

namespace Documents.Contracts.FileTemplates;

/// <summary>See the remarks on <c>Documents.Contracts.Documents.DocumentContracts</c> — same reason
/// these command/query wrapper types and DTOs live here rather than in <c>Documents.Application</c>.
/// <paramref name="Category"/> is the enum name as a string (e.g. <c>"Cad"</c>), not
/// <c>Documents.Domain.FileTemplateCategory</c> itself — <c>Documents.Contracts</c> may only reference
/// <c>SharedKernel</c>/<c>BuildingBlocks</c>, and <c>Documents.Domain</c> may only reference
/// <c>SharedKernel</c>, so neither side can share one enum type; the Application handler converts, same
/// pattern as <c>Hr.Contracts.Employees.EmployeeContracts</c>'s own <c>ContractType</c>.</summary>
public sealed record FileTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    string Category,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    bool IsActive);

/// <summary><paramref name="Content"/>/<paramref name="FileName"/>/<paramref name="ContentType"/>/
/// <paramref name="FileSizeBytes"/> come from the multipart form, same as the document upload
/// commands. <paramref name="Name"/> must be globally unique across all templates.</summary>
public sealed record CreateFileTemplateCommand(
    string Name,
    string Category,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string? Description = null) : ICommand<Guid>;

/// <summary>Cannot change the stored file's bytes — matches the backend's own
/// <c>FileTemplate.Update</c>, which only ever touches name/category/description. Replacing the file
/// itself means deleting the template and creating a new one.</summary>
public sealed record UpdateFileTemplateCommand(
    Guid FileTemplateId, string Name, string Category, string? Description = null) : ICommand;

public sealed record DeleteFileTemplateCommand(Guid FileTemplateId) : ICommand;

public sealed record ActivateFileTemplateCommand(Guid FileTemplateId) : ICommand;

public sealed record DeactivateFileTemplateCommand(Guid FileTemplateId) : ICommand;

public sealed record GetFileTemplatesQuery(string? CategoryFilter = null) : IQuery<IReadOnlyList<FileTemplateResponse>>;

public sealed record GetFileTemplateByIdQuery(Guid FileTemplateId) : IQuery<FileTemplateResponse>;
