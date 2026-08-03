using BuildingBlocks.Messaging;
using Identity.Contracts.Permissions;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetUserPermissions;

/// <summary>
/// No <c>AbstractValidator</c> here — <c>AddModuleCqrs</c> only wraps command handlers in
/// <c>ValidationDecorator</c>; queries have no equivalent pipeline to plug a validator into. A
/// FluentValidation validator on a query type would register via the <c>IValidator&lt;&gt;</c> scan and
/// then silently never run — the same no-op failure mode already fixed once for commands in G1-05. An
/// empty <c>UserId</c> here is not worth a pipeline for: it resolves zero roles and returns an empty
/// list, a safe and correct answer to "what can this user do".
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
        var claims = await permissionService.GetUserPermissionsAsync(query.UserId, ct);
        return Result.Success<IReadOnlyList<PermissionResponse>>([.. claims.Select(c => c.ToResponse())]);
    }
}
