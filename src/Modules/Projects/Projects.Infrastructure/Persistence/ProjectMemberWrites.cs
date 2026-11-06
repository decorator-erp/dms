using Microsoft.EntityFrameworkCore;
using Npgsql;
using Projects.Domain;
using Projects.Infrastructure.Persistence.Configurations;
using SharedKernel;

namespace Projects.Infrastructure.Persistence;

/// <summary>Turns the member races into 409s: a stale <c>xmin</c> → ConcurrentUpdate, a second insert of
/// the same (project, employee) → AlreadyMember.</summary>
internal static class ProjectMemberWrites
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
            return ProjectMemberErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProjectMemberConfiguration.ProjectEmployeeIndex,
        })
        {
            return ProjectMemberErrors.AlreadyMember;
        }
    }
}
