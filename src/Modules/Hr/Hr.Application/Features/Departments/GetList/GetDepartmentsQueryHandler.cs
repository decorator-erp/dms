using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
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

internal sealed class GetDepartmentsQueryHandler(HrDbContext db, IUserDirectory userDirectory)
    : IQueryHandler<GetDepartmentsQuery, PagedResult<DepartmentResponse>>
{
    public async Task<Result<PagedResult<DepartmentResponse>>> HandleAsync(
        GetDepartmentsQuery query, CancellationToken ct = default)
    {
        // K3: the filter hides deleted departments (this used to be a hand-written Where(IsActive));
        // includeInactive is the admin view that shows them.
        IQueryable<Department> baseQuery = query.IncludeInactive
            ? db.Departments.IgnoreQueryFilters().AsNoTracking()
            : db.Departments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.NameFilter))
        {
            baseQuery = baseQuery.Where(d => EF.Functions.ILike(d.Name, $"%{query.NameFilter}%"));
        }

        int totalCount = await baseQuery.CountAsync(ct);

        List<Department> page = await baseQuery
            .OrderBy(d => d.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        List<DepartmentResponse> items = await DepartmentResponses.BuildAsync(db, userDirectory, page, ct);

        return new PagedResult<DepartmentResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
