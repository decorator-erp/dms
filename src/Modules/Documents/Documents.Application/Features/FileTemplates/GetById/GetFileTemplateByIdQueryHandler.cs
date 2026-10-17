using BuildingBlocks.Messaging;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.FileTemplates.GetById;

internal sealed class GetFileTemplateByIdQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetFileTemplateByIdQuery, FileTemplateResponse>
{
    public async Task<Result<FileTemplateResponse>> HandleAsync(
        GetFileTemplateByIdQuery query, CancellationToken ct = default)
    {
        FileTemplateResponse? template = await db.FileTemplates.AsNoTracking()
            .Where(t => t.Id == query.FileTemplateId)
            .Select(ToResponse())
            .FirstOrDefaultAsync(ct);

        return template is null ? FileTemplateErrors.NotFound : template;
    }

    internal static System.Linq.Expressions.Expression<Func<FileTemplate, FileTemplateResponse>> ToResponse() => t =>
        new FileTemplateResponse(
            t.Id, t.Name, t.Description, t.Category.ToString(), t.FileName, t.ContentType, t.FileSizeBytes, t.IsActive);
}
