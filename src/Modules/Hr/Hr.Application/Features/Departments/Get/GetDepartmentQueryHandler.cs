using BuildingBlocks.Messaging;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.Get;

internal sealed class GetDepartmentQueryHandler(HrDbContext db) : IQueryHandler<GetDepartmentQuery, DepartmentResponse>
{
    public async Task<Result<DepartmentResponse>> HandleAsync(GetDepartmentQuery query, CancellationToken ct = default)
    {
        var department = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == query.DepartmentId, ct);

        return department is null
            ? DepartmentErrors.NotFound
            : new DepartmentResponse(department.Id, department.Name, department.Description, department.DepartmentHeadId, department.IsActive);
    }
}
