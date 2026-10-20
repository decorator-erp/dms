using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetAllPermissions;

/// <summary>Reads <see cref="IPermissionCatalog"/> — the union of every module's registered
/// permission strings — never a hard-coded list. This is deliberately NOT the permissions currently
/// assigned to some role (the service method that listed those had no caller and was deleted in Phase
/// 5); this returns every permission that COULD be assigned, including ones no role has yet.</summary>
internal sealed class GetAllPermissionsQueryHandler(IPermissionCatalog catalog)
    : IQueryHandler<GetAllPermissionsQuery, IReadOnlyList<ModulePermissionSet>>
{
    public Task<Result<IReadOnlyList<ModulePermissionSet>>> HandleAsync(
        GetAllPermissionsQuery query, CancellationToken ct = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<ModulePermissionSet>>(catalog.All()));
}
