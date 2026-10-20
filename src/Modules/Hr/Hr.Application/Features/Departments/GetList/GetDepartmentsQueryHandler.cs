using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Persistence;
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
        // Aug 3.4: the filter is matched literally ("50%" is not a wildcard).
        if (Like.Contains(query.NameFilter) is { } pattern)
        {
            baseQuery = baseQuery.Where(d => EF.Functions.ILike(d.Name, pattern, Like.EscapeCharacter));
        }

        // The count and the page are two statements, so a write between them can make the total
        // disagree with the rows by one. Accepted: the next read corrects it.
        int totalCount = await baseQuery.CountAsync(ct);
        if (!PageWindow.TrySkip(query.PageNumber, query.PageSize, out int skip))
        {
            return new PagedResult<DepartmentResponse>([], query.PageNumber, query.PageSize, totalCount);
        }

        // ThenBy(Id): with includeInactive a deleted department can share an active one's name.
        List<Department> page = await baseQuery
            .OrderBy(d => d.Name)
            .ThenBy(d => d.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        List<DepartmentResponse> items = await DepartmentResponses.BuildAsync(db, userDirectory, page, ct);

        return new PagedResult<DepartmentResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
