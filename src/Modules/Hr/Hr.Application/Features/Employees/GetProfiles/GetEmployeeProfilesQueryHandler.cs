using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Persistence;
using FluentValidation;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Services;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Hr.Application.Features.Employees.GetProfiles;

internal sealed class GetEmployeeProfilesQueryValidator : AbstractValidator<GetEmployeeProfilesQuery>
{
    public GetEmployeeProfilesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>
/// Aug 4.3: <c>GET /api/employees</c>. It used to run <c>GetEmployeesQuery</c> (whole rows) and then
/// <c>IHrDirectory.GetProfilesAsync</c>, which read the same rows again for four columns and the
/// department. Now the page is projected straight to <see cref="EmployeeSummary"/> through
/// <see cref="EmployeeProfiles.ToSummary"/>, the same projection the directory uses, and zipped with one
/// <see cref="IUserDirectory.GetManyAsync"/>: three statements in all (count, page, Identity).
///
/// <para>Filters, literal ILIKE, the long-safe skip and the <c>JobTitle, Id</c> order are Phase 3's (the
/// old list handler had them too; Phase 5 deleted it). The count and the page are two statements, so a
/// write between them can make the total disagree with the rows by one; the next read corrects it.</para>
/// </summary>
internal sealed class GetEmployeeProfilesQueryHandler(
    HrDbContext db, IUserDirectory userDirectory, ILogger<GetEmployeeProfilesQueryHandler> logger)
    : IQueryHandler<GetEmployeeProfilesQuery, PagedResult<EmployeeProfile>>
{
    public async Task<Result<PagedResult<EmployeeProfile>>> HandleAsync(
        GetEmployeeProfilesQuery query, CancellationToken ct = default)
    {
        // K3: active employees unless the caller asks for the deactivated ones too.
        IQueryable<Employee> baseQuery = query.IncludeInactive
            ? db.Employees.IgnoreQueryFilters().AsNoTracking()
            : db.Employees.AsNoTracking();
        if (Like.Contains(query.JobTitleFilter) is { } pattern)
        {
            baseQuery = baseQuery.Where(e => EF.Functions.ILike(e.JobTitle, pattern, Like.EscapeCharacter));
        }

        int totalCount = await baseQuery.CountAsync(ct);
        if (!PageWindow.TrySkip(query.PageNumber, query.PageSize, out int skip))
        {
            return new PagedResult<EmployeeProfile>([], query.PageNumber, query.PageSize, totalCount);
        }

        List<EmployeeSummary> page = await baseQuery
            .OrderBy(e => e.JobTitle)
            .ThenBy(e => e.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .Select(EmployeeProfiles.ToSummary(db))
            .ToListAsync(ct);
        if (page.Count == 0)
        {
            return new PagedResult<EmployeeProfile>([], query.PageNumber, query.PageSize, totalCount);
        }

        IReadOnlyList<UserSummary> users = await userDirectory.GetManyAsync([.. page.Select(s => s.AppUserId)], ct);
        return new PagedResult<EmployeeProfile>(
            EmployeeProfiles.Zip(page, users, logger), query.PageNumber, query.PageSize, totalCount);
    }
}
