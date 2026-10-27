using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Infrastructure.Services;

/// <summary>
/// <see cref="ICrmAccessGuard"/>'s implementation, ported from the backend's <c>CrmAccessService</c>'s
/// actor-resolution half. Two actor kinds:
///
/// <list type="bullet">
/// <item><description><b>Employee</b> — resolved via <see cref="IHrDirectory.ResolveEmployeeIdAsync"/>.
/// Depth-1: CRM asks HR one question, HR asks nobody. <see cref="CrmActor.HasReadAll"/> is set for
/// every resolved employee actor here; the backend's read-all override is a permission check
/// (<c>crm.*.read_all</c>-shaped), which in this port is the caller's own
/// <c>CrmPermissions.*.Read</c> claim, checked by the endpoint/handler, not by this guard — this
/// guard only resolves WHO the caller is, never what they may do with that identity.</description></item>
/// <item><description><b>Client</b> — CRM-owned data, resolved locally against
/// <c>crm.clients.AppUserId</c>. No cross-module call.</description></item>
/// </list>
///
/// A caller who is neither (no employee record, no active client record for that app user id) is not an
/// authentication failure — it is a caller with no standing in CRM, surfaced as a 403-shaped error per
/// <see cref="ICrmAccessGuard.ResolveActorAsync"/>'s own doc comment.
/// </summary>
internal sealed class CrmAccessGuard(CrmDbContext db, IHrDirectory hrDirectory) : ICrmAccessGuard
{
    public async Task<Result<CrmActor>> ResolveActorAsync(string appUserId, CancellationToken ct)
    {
        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(appUserId, ct);
        if (employeeId is { } resolvedEmployeeId)
        {
            return new CrmActor(CrmActorKind.Employee, resolvedEmployeeId, null, null, HasReadAll: true);
        }

        var client = await db.Clients.AsNoTracking()
            .Where(c => c.AppUserId == appUserId)
            .Select(c => new { c.Id, c.IsActive })
            .FirstOrDefaultAsync(ct);

        // X-3: a deactivated (or soft-deleted) client has no standing. Same error as "no client at all",
        // so the caller learns nothing about the row.
        if (client is { IsActive: true })
        {
            // LC-L1: the primary contact acts for the client; the oldest contact only when none is primary.
            Guid? contactId = await db.CrmContacts.AsNoTracking()
                .Where(c => c.ClientId == client.Id)
                .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);

            return new CrmActor(CrmActorKind.Client, null, contactId, client.Id, HasReadAll: false);
        }

        return CrmAccessErrors.NoStanding;
    }

    public async Task<IReadOnlyDictionary<string, CrmActor>> ResolveActorsAsync(
        IReadOnlyCollection<string> appUserIds, CancellationToken ct)
    {
        if (appUserIds.Count == 0)
        {
            return new Dictionary<string, CrmActor>();
        }

        var result = new Dictionary<string, CrmActor>();

        // IHrDirectory has no "resolve many app-user-ids to employee ids" batch surface - only
        // GetManyAsync(employee ids) and the single-id ResolveEmployeeIdAsync. This falls back to one
        // ResolveEmployeeIdAsync call per id for the employee branch. Acceptable here: batch
        // resolution is a display-list convenience (the Portal roster), not the hot per-request
        // authorization path, which always resolves exactly one actor via ResolveActorAsync.
        foreach (string appUserId in appUserIds)
        {
            Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(appUserId, ct);
            if (employeeId is { } resolvedEmployeeId)
            {
                result[appUserId] = new CrmActor(CrmActorKind.Employee, resolvedEmployeeId, null, null, HasReadAll: true);
            }
        }

        var remaining = appUserIds.Where(id => !result.ContainsKey(id)).ToList();
        if (remaining.Count > 0)
        {
            var clients = await db.Clients.AsNoTracking()
                .Where(c => remaining.Contains(c.AppUserId) && c.IsActive)
                .Select(c => new { c.Id, c.AppUserId })
                .ToListAsync(ct);

            var clientIds = clients.Select(c => c.Id).ToList();
            var primaryContactsByClient = await db.CrmContacts.AsNoTracking()
                .Where(c => clientIds.Contains(c.ClientId))
                .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                .Select(c => new { c.ClientId, c.Id })
                .ToListAsync(ct);

            foreach (var client in clients)
            {
                Guid? contactId = primaryContactsByClient.FirstOrDefault(c => c.ClientId == client.Id)?.Id;
                result[client.AppUserId] = new CrmActor(CrmActorKind.Client, null, contactId, client.Id, HasReadAll: false);
            }
        }

        return result;
    }
}

internal static class CrmAccessErrors
{
    /// <summary>The caller has no user id at all — the endpoint layer's 401, before any CRM lookup.</summary>
    public static readonly Error Unauthenticated = Error.Unauthorized(
        "crm.access.Unauthenticated", "The current user could not be identified.");

    public static readonly Error NoStanding = Error.Forbidden(
        "crm.access.NoStanding", "This user has no active CRM standing.");

    /// <summary>G9-14: surfaced by the endpoint layer (<c>CrmEndpointActors</c>), not this guard itself —
    /// several CRM commands (<c>CreatedById</c>/<c>WrittenById</c>/<c>OwnerId</c>-shaped) take a
    /// pre-resolved employee <c>Guid</c> rather than an <c>AppUserId</c> + internal resolution (unlike
    /// Projects' own handlers), so the endpoint resolves the acting employee via
    /// <see cref="ICrmAccessGuard"/> before constructing the command. A caller who resolves to a client
    /// actor on an employee-only write is unreachable in practice (permission claims differ per role),
    /// but this error exists so that path fails closed rather than passing <c>Guid.Empty</c> through.</summary>
    public static readonly Error NotAnEmployee = Error.Forbidden(
        "crm.access.NotAnEmployee", "This action requires an employee actor.");

    /// <summary>The Portal-side mirror of <see cref="NotAnEmployee"/> — a caller who resolves to an
    /// employee actor reaching a client-only write (e.g. the Portal's own requirement-item submission,
    /// which resolves <c>ContactId</c> the same pre-resolved way).</summary>
    public static readonly Error NotAClient = Error.Forbidden(
        "crm.access.NotAClient", "This action requires a client actor.");
}
