using BuildingBlocks.Messaging;
using Identity.Contracts.Roles;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Roles.GetRole;

/// <summary>Aug 4.4: reads untracked, straight from the context. <c>IRoleService.GetByIdAsync</c> stays
/// tracked because the role commands that use it may go on to change the role.</summary>
internal sealed class GetRoleQueryHandler(IdentityModuleDbContext db) : IQueryHandler<GetRoleQuery, RoleResponse>
{
    public async Task<Result<RoleResponse>> HandleAsync(GetRoleQuery query, CancellationToken ct = default)
    {
        ApplicationRole? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == query.RoleId, ct);
        return role is null ? RoleErrors.NotFound : role.ToResponse();
    }
}
