using BuildingBlocks.Messaging;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.Get;

internal sealed class GetDepartmentQueryHandler(HrDbContext db, IUserDirectory userDirectory)
    : IQueryHandler<GetDepartmentQuery, DepartmentResponse>
{
    public async Task<Result<DepartmentResponse>> HandleAsync(GetDepartmentQuery query, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: the detail page shows a deleted department too (IsActive says so).
        Department? department = await db.Departments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DepartmentId, ct);

        return department is null
            ? DepartmentErrors.NotFound
            : (await DepartmentResponses.BuildAsync(db, userDirectory, [department], ct))[0];
    }
}
