using Microsoft.EntityFrameworkCore;
using Npgsql;
using Projects.Domain;
using Projects.Infrastructure.Persistence.Configurations;
using SharedKernel;

namespace Projects.Infrastructure.Persistence;

/// <summary>
/// Saves a project-row write and turns the races into 409s instead of 500s, modelled on
/// <see cref="ProjectMemberWrites"/> and CRM's <c>CatalogWrites</c>:
/// <list type="bullet">
/// <item>P9: a stale <c>xmin</c> (<see cref="DbUpdateConcurrencyException"/>) → <see cref="ProjectErrors.ConcurrentUpdate"/>;</item>
/// <item>P8: a code that another create took between the handler's pre-check and this save
/// (<c>IX_projects_Code</c>) → <see cref="ProjectErrors.CodeTaken"/>.</item>
/// </list>
/// Anything else still throws.
/// </summary>
internal static class ProjectWrites
{
    public static async Task<Result> SaveAsync(ProjectsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProjectErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProjectConfiguration.CodeIndex,
        })
        {
            return ProjectErrors.CodeTaken;
        }
    }
}
