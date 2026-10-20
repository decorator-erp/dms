using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Persistence;
using FluentValidation;
using Hr.Application.Features.Employees.Get;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.GetList;

internal sealed class GetEmployeesQueryValidator : AbstractValidator<GetEmployeesQuery>
{
    public GetEmployeesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class GetEmployeesQueryHandler(HrDbContext db)
    : IQueryHandler<GetEmployeesQuery, PagedResult<EmployeeResponse>>
{
    public async Task<Result<PagedResult<EmployeeResponse>>> HandleAsync(
        GetEmployeesQuery query, CancellationToken ct = default)
    {
        // K3: active employees unless the caller asks for the deactivated ones too.
        IQueryable<Employee> baseQuery = query.IncludeInactive
            ? db.Employees.IgnoreQueryFilters().AsNoTracking()
            : db.Employees.AsNoTracking();
        // Aug 3.4: the filter is matched literally ("50%" is not a wildcard).
        if (Like.Contains(query.JobTitleFilter) is { } pattern)
        {
            baseQuery = baseQuery.Where(e => EF.Functions.ILike(e.JobTitle, pattern, Like.EscapeCharacter));
        }

        // The count and the page are two statements, so a write between them can make the total
        // disagree with the rows by one. Accepted: the next read corrects it.
        int totalCount = await baseQuery.CountAsync(ct);
        if (!PageWindow.TrySkip(query.PageNumber, query.PageSize, out int skip))
        {
            return new PagedResult<EmployeeResponse>([], query.PageNumber, query.PageSize, totalCount);
        }

        // ThenBy(Id): job titles repeat, and without a unique tiebreak a row can appear on two pages
        // or none.
        List<Employee> employees = await baseQuery
            .OrderBy(e => e.JobTitle)
            .ThenBy(e => e.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = employees.Select(ResponseMapping.ToResponse).ToList();

        return new PagedResult<EmployeeResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
