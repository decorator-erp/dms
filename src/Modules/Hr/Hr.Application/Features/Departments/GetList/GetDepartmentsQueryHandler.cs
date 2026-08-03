using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.GetList;

internal sealed class GetDepartmentsQueryValidator : AbstractValidator<GetDepartmentsQuery>
{
    public GetDepartmentsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class GetDepartmentsQueryHandler(HrDbContext db)
    : IQueryHandler<GetDepartmentsQuery, PagedResult<DepartmentResponse>>
{
    public async Task<Result<PagedResult<DepartmentResponse>>> HandleAsync(
        GetDepartmentsQuery query, CancellationToken ct = default)
    {
        var baseQuery = db.Departments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.NameFilter))
            baseQuery = baseQuery.Where(d => EF.Functions.ILike(d.Name, $"%{query.NameFilter}%"));

        var totalCount = await baseQuery.CountAsync(ct);

        var items = await baseQuery
            .OrderBy(d => d.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(d => new DepartmentResponse(d.Id, d.Name, d.Description, d.DepartmentHeadId, d.IsActive))
            .ToListAsync(ct);

        return new PagedResult<DepartmentResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
