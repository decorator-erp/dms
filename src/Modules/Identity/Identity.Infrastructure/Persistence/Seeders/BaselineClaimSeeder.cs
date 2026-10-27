using BuildingBlocks;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Identity.Infrastructure.Persistence.Seeders;

/// <summary>
/// N5: the permissions a fresh <c>employee</c> or <c>client</c> needs to use their own screens, given
/// <b>once</b>. Unlike <see cref="RoleClaimSeeder"/>, which re-adds whatever a full-access role lacks on
/// every boot, these roles belong to the admin after the first run. A claim an admin removes stays
/// removed, so the step runs behind a <see cref="SeedMarker"/>: no marker, add the missing codes and
/// the marker in one save; marker present, do nothing.
///
/// <para>Only codes the catalog knows are added, so a module that isn't registered contributes nothing.
/// The claims go through the change tracker, so the seed is audited like any other claim write.</para>
///
/// <para><b>Two instances starting at once</b> both see no marker. The second save then hits the
/// marker's primary key; that means the other instance has applied the step, so the loser clears what
/// it tracked and returns. Chosen over <c>pg_advisory_xact_lock</c> because the marker row is already
/// the lock.</para>
/// <para><b>Steps (B-16).</b> A later baseline is a new step with its own marker, never an edit to an
/// applied one: v1's marker is already on every running database, so a code added to v1 would never
/// reach them. v2 gives <c>client</c> <c>notifications.view_own</c>, which v1 missed — without it a
/// portal client could not read the meeting notifications CRM sends them.</para>
/// </summary>
internal static class BaselineClaimSeeder
{
    public const string MarkerKey = "baseline-claims-v1";

    public const string V2MarkerKey = "baseline-claims-v2";

    public static readonly IReadOnlyDictionary<string, string[]> Baseline = new Dictionary<string, string[]>
    {
        [DefaultRoles.Employee] = ["employees.view_own", "notifications.view_own"],
        [DefaultRoles.Client] =
        [
            "crm.portal", "crm.briefs.view_own", "crm.meetings.view_own", "crm.meetings.request_own",
            "crm.meetings.respond_own",
        ],
    };

    /// <summary>B-16 (I1): the client's own notifications.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BaselineV2 = new Dictionary<string, string[]>
    {
        [DefaultRoles.Client] = ["notifications.view_own"],
    };

    /// <summary>Applied in order, each behind its own marker.</summary>
    private static readonly (string MarkerKey, IReadOnlyDictionary<string, string[]> Claims)[] Steps =
        [(MarkerKey, Baseline), (V2MarkerKey, BaselineV2)];

    public static async Task SeedAsync(
        RoleManager<ApplicationRole> roleManager,
        IdentityModuleDbContext db,
        IPermissionCatalog catalog,
        CancellationToken ct = default)
    {
        var known = catalog.All().SelectMany(m => m.Permissions).ToHashSet(StringComparer.Ordinal);
        foreach ((string markerKey, IReadOnlyDictionary<string, string[]> claims) in Steps)
        {
            await ApplyStepAsync(roleManager, db, known, markerKey, claims, ct);
        }
    }

    private static async Task ApplyStepAsync(
        RoleManager<ApplicationRole> roleManager,
        IdentityModuleDbContext db,
        HashSet<string> known,
        string markerKey,
        IReadOnlyDictionary<string, string[]> claims,
        CancellationToken ct)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == markerKey, ct))
        {
            return;
        }

        foreach ((string roleName, string[] codes) in claims)
        {
            ApplicationRole? role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            List<string> existing = await db.RoleClaims
                .Where(rc => rc.RoleId == role.Id && rc.ClaimType == PermissionClaim.Type)
                .Select(rc => rc.ClaimValue!)
                .ToListAsync(ct);

            foreach (string code in codes.Where(known.Contains).Except(existing))
            {
                db.RoleClaims.Add(ApplicationRoleClaim.Create(role.Id, PermissionClaim.Type, code));
            }
        }

        db.SeedMarkers.Add(SeedMarker.Create(markerKey, DateTime.UtcNow));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: SeedMarkerConfiguration.PrimaryKey,
        })
        {
            // Another instance applied the step first (see the summary). Its claims are in; ours were
            // not saved. Forget them so the rest of the seed scope doesn't try again.
            db.ChangeTracker.Clear();
        }
    }
}
