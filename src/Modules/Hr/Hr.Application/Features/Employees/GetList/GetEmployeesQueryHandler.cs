using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Hr.Application.Features.Employees.Get;
using Hr.Contracts.Employees;
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
        var baseQuery = db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.JobTitleFilter))
            baseQuery = baseQuery.Where(e => EF.Functions.ILike(e.JobTitle, $"%{query.JobTitleFilter}%"));

        var totalCount = await baseQuery.CountAsync(ct);

        var employees = await baseQuery
            .OrderBy(e => e.JobTitle)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = employees.Select(ResponseMapping.ToResponse).ToList();

        return new PagedResult<EmployeeResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
