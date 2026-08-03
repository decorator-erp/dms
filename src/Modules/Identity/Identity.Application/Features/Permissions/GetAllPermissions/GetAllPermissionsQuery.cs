using BuildingBlocks.Messaging;
using Identity.Contracts;
using SharedKernel;

namespace Identity.Application.Features.Permissions.GetAllPermissions;

internal sealed record GetAllPermissionsQuery : IQuery<IReadOnlyList<ModulePermissionSet>>;

/// <summary>Reads <see cref="IPermissionCatalog"/> — the union of every module's registered
/// permission strings — never a hard-coded list. This is deliberately NOT the same thing as
/// <c>IPermissionService.GetAllPermissionsAsync</c>, which only returns permissions currently assigned
/// to a role; this returns every permission that COULD be assigned, including ones no role has yet.</summary>
internal sealed class GetAllPermissionsQueryHandler(IPermissionCatalog catalog)
    : IQueryHandler<GetAllPermissionsQuery, IReadOnlyList<ModulePermissionSet>>
{
    public Task<Result<IReadOnlyList<ModulePermissionSet>>> HandleAsync(
        GetAllPermissionsQuery query, CancellationToken ct = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<ModulePermissionSet>>(catalog.All()));
}
