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
/// portal client could not read the meeting notifications CRM sends them. v3 (B1.4) gives <c>client</c>
/// <c>crm.approvals.view_own</c> and <c>crm.approvals.decide_own</c>, so a portal client can list and decide
/// the approvals CRM sends them; it adds only those two, so a v1/v2 code an admin removed stays removed. v4 (crm-complete F) gives <c>client</c>
/// <c>requirements.view_own</c> for the portal requirement-items list, and only that. v5 (Sites/Projects phase 5) gives
/// <c>employee</c> project, task-execution and survey-capture codes; see <see cref="BaselineV5"/>. v6 (BF-1) gives
/// <c>employee</c> the survey lookups — spaces, catalog items and project documents; see <see cref="BaselineV6"/>.</para>
/// </summary>
internal static class BaselineClaimSeeder
{
    public const string MarkerKey = "baseline-claims-v1";

    public const string V2MarkerKey = "baseline-claims-v2";

    public const string V3MarkerKey = "baseline-claims-v3";

    public const string V4MarkerKey = "baseline-claims-v4";

    public const string V5MarkerKey = "baseline-claims-v5";

    public const string V6MarkerKey = "baseline-claims-v6";

    public const string V7MarkerKey = "baseline-claims-v7";

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

    /// <summary>B1.4: the client decides its own approvals in the portal.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BaselineV3 = new Dictionary<string, string[]>
    {
        [DefaultRoles.Client] = ["crm.approvals.view_own", "crm.approvals.decide_own"],
    };

    /// <summary>crm-complete part F (C5): the client reads its own requirement items in the portal (F1). Only this
    /// one code; nothing a v1–v3 step granted is re-granted.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BaselineV4 = new Dictionary<string, string[]>
    {
        [DefaultRoles.Client] = ["requirements.view_own"],
    };

    /// <summary>Sites/Projects phase 5 (S6, P10): the employee works the projects they are on. Read the project, its
    /// stages and scope; read and execute tasks (start, complete, sub-tasks, comments — <c>stage_tasks.complete</c>
    /// opens the start and comment routes too); capture site surveys (read, create, update, submit — never
    /// approve or delete). The project level (Editor and up) still decides each write on each project. Literal
    /// codes, as in v1–v4: Identity does not reference the modules that own them.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BaselineV5 = new Dictionary<string, string[]>
    {
        [DefaultRoles.Employee] =
        [
            "project.read", "stages.read", "scope.read", "stage_tasks.read", "stage_tasks.complete",
            "sites.read", "sites.create", "sites.update", "sites.submit",
        ],
    };

    /// <summary>BF-1: what a surveyor's lookups read, which v5's capture codes assumed. Without these an employee
    /// (Editor) capturing a survey could not pick a space or a catalog item, nor list or attach the project's files.
    /// <list type="bullet">
    /// <item><c>crm.spaces.read</c>: already project-scoped (<c>ICrmProjectScoped</c> → <c>CrmProjectScopeDecorator</c>).</item>
    /// <item><c>crm.catalog.read</c>: the global item catalog — reference data, no project rows.</item>
    /// <item><c>documents.read</c>, <c>documents.upload</c>: project-scoped since <c>DocumentsScopeDecorator</c>
    /// (a project the caller is not on is a 404; a write needs project level Editor or higher).</item>
    /// </list>
    /// Deliberately not <c>employees.read</c> (the full HR record; the survey page only wants names and falls back
    /// to the id), nor <c>documents.update</c>/<c>delete</c> or any manage code.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BaselineV6 = new Dictionary<string, string[]>
    {
        [DefaultRoles.Employee] = ["crm.spaces.read", "crm.catalog.read", "documents.read", "documents.upload"],
    };

    /// <summary>P2-A: the client reads what the office shares with it in the portal.
    /// <list type="bullet">
    /// <item><c>crm.portal.files.view_own</c>: the shared documents, the gallery (images and site photos) and the
    /// file stream.</item>
    /// <item><c>crm.portal.progress.view_own</c>: the overview (progress %, stage timeline, counts), the shared
    /// tasks and the published updates.</item>
    /// </list>
    /// Both are reads of the caller's OWN projects only (the handlers check <c>project.ClientId</c>, else 404), and
    /// both open nothing by themselves: every section starts switched off and nothing is shared until the office
    /// shares it. Never the office's <c>crm.portal.manage</c>. Literal codes, as in v1–v6.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BaselineV7 = new Dictionary<string, string[]>
    {
        [DefaultRoles.Client] = ["crm.portal.files.view_own", "crm.portal.progress.view_own"],
    };

    /// <summary>Applied in order, each behind its own marker.</summary>
    private static readonly (string MarkerKey, IReadOnlyDictionary<string, string[]> Claims)[] Steps =
    [
        (MarkerKey, Baseline), (V2MarkerKey, BaselineV2), (V3MarkerKey, BaselineV3), (V4MarkerKey, BaselineV4),
        (V5MarkerKey, BaselineV5), (V6MarkerKey, BaselineV6), (V7MarkerKey, BaselineV7),
    ];

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
