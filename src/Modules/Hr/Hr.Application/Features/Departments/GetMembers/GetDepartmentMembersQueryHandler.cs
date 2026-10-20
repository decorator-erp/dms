using BuildingBlocks.Messaging;
using Hr.Contracts;
using Hr.Contracts.Departments;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.GetMembers;

/// <summary>
/// Aug 3.10: <c>GET /api/departments/{id}/members</c>, moved out of the endpoint. An unknown or deleted
/// department (K3's filter) is a 404; it used to answer <c>200 []</c>, the same as a department with no
/// members. <see cref="IHrDirectory.DepartmentMembersAsync"/> gives HR's own ids, and
/// <see cref="IHrDirectory.GetProfilesAsync"/> turns the set into names and emails in one batched call.
/// </summary>
internal sealed class GetDepartmentMembersQueryHandler(HrDbContext db, IHrDirectory hrDirectory)
    : IQueryHandler<GetDepartmentMembersQuery, IReadOnlyList<EmployeeProfile>>
{
    public async Task<Result<IReadOnlyList<EmployeeProfile>>> HandleAsync(
        GetDepartmentMembersQuery query, CancellationToken ct = default)
    {
        if (!await db.Departments.AnyAsync(d => d.Id == query.DepartmentId, ct))
        {
            return DepartmentErrors.NotFound;
        }

        IReadOnlyList<Guid> memberIds = await hrDirectory.DepartmentMembersAsync(query.DepartmentId, ct);
        return Result.Success(await hrDirectory.GetProfilesAsync(memberIds, ct));
    }
}
