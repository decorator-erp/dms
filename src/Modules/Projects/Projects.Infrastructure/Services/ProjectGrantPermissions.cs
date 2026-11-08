using BuildingBlocks;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Services;

/// <summary>
/// P1-A: <see cref="IProjectGrantPermissions"/> — each active grant's areas × level through
/// <see cref="GrantAreaPermissions"/>. One statement (the user's active grants' level and areas), read on the
/// request's own clock; <see cref="ImpliedPermissions"/> caches the result for the rest of the request.
///
/// <para>Deliberately not built on <see cref="ProjectAccessContext"/>: that context reads
/// <see cref="ImpliedPermissions"/>, which is built from this source, so depending on it would be a DI cycle.</para>
/// </summary>
internal sealed class ProjectGrantPermissions(ProjectsDbContext db, IHrDirectory hrDirectory, TimeProvider timeProvider)
    : IProjectGrantPermissions, IImpliedPermissionSource
{
    public async Task<bool> HasImpliedPermissionAsync(string userId, string permission, CancellationToken ct) =>
        (await GetImpliedPermissionsAsync(userId, ct)).Contains(permission);

    public async Task<IReadOnlySet<string>> GetImpliedPermissionsAsync(string userId, CancellationToken ct)
    {
        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return new HashSet<string>();
        }

        var grants = await db.ProjectAccessGrants.AsNoTracking()
            .Where(g => g.EmployeeId == employeeId.Value)
            .Where(Domain.ProjectAccessGrant.ActiveAt(timeProvider.GetUtcNow().UtcDateTime))
            .Select(g => new { g.Level, g.Areas })
            .ToListAsync(ct);

        HashSet<string> codes = new(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            codes.UnionWith(GrantAreaPermissions.Implied((ProjectAccessArea)grant.Areas, (ProjectMemberAccess)grant.Level));
        }

        return codes;
    }
}
