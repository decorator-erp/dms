using BuildingBlocks.Messaging;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetUserPermissions;

/// <summary>
/// No <c>AbstractValidator</c> here, by choice. When this was written <c>AddModuleCqrs</c> only wrapped
/// command handlers in <c>ValidationDecorator</c>, so a query validator would have registered via the
/// <c>IValidator&lt;&gt;</c> scan and then silently never run. Queries now get
/// <c>ValidationDecorator.QueryHandler&lt;,&gt;</c> too (HR/Identity Phase 0, Aug 0.1), and Rule 11
/// checks every validator is reachable, so one could be added here and would run. It still isn't worth
/// one: an empty <c>UserId</c> resolves zero roles and returns an empty list, a safe and correct answer
/// to "what can this user do".
///
/// Role-derived permissions only (the admin/listing view) — NOT the same as a user's effective
/// permission set, which also applies per-user overrides; see
/// <see cref="Identity.Application.Features.Permissions.GetUserEffectivePermissions.GetUserEffectivePermissionsQueryHandler"/>
/// for that.
/// </summary>
internal sealed class GetUserPermissionsQueryHandler(IPermissionService permissionService)
    : IQueryHandler<GetUserPermissionsQuery, IReadOnlyList<PermissionResponse>>
{
    public async Task<Result<IReadOnlyList<PermissionResponse>>> HandleAsync(
        GetUserPermissionsQuery query, CancellationToken ct = default)
    {
        IReadOnlyList<ApplicationRoleClaim> claims = await permissionService.GetUserPermissionsAsync(query.UserId, ct);
        return Result.Success<IReadOnlyList<PermissionResponse>>([.. claims.Select(c => c.ToResponse())]);
    }
}
