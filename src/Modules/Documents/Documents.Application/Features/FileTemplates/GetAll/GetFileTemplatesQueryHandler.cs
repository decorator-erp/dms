using BuildingBlocks.Messaging;
using Documents.Application.Features.FileTemplates.GetById;
using Documents.Contracts.FileTemplates;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.FileTemplates.GetAll;

internal sealed class GetFileTemplatesQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetFileTemplatesQuery, IReadOnlyList<FileTemplateResponse>>
{
    public async Task<Result<IReadOnlyList<FileTemplateResponse>>> HandleAsync(
        GetFileTemplatesQuery query, CancellationToken ct = default)
    {
        IQueryable<FileTemplate> baseQuery = db.FileTemplates.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.CategoryFilter)
            && Enum.TryParse<FileTemplateCategory>(query.CategoryFilter, out FileTemplateCategory category))
        {
            baseQuery = baseQuery.Where(t => t.Category == category);
        }

        List<FileTemplateResponse> templates = await baseQuery
            .OrderBy(t => t.Name)
            .Select(GetFileTemplateByIdQueryHandler.ToResponse())
            .ToListAsync(ct);

        return templates;
    }
}
