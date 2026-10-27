using Microsoft.EntityFrameworkCore;
using Npgsql;
using Projects.Domain;
using Projects.Infrastructure.Persistence.Configurations;
using SharedKernel;

namespace Projects.Infrastructure.Persistence;

/// <summary>
/// Saves a scope-of-work change and turns the races into 409s instead of 500s, modelled on CRM's
/// <c>LeadWrites</c>:
/// <list type="bullet">
/// <item>a stale <c>xmin</c> (<see cref="DbUpdateConcurrencyException"/>) → <see cref="ScopeOfWorkErrors.ConcurrentUpdate"/>;</item>
/// <item>two first saves of one project's scope (the unique <c>ProjectId</c> index) → <see cref="ScopeOfWorkErrors.ConcurrentUpdate"/>;</item>
/// <item>the same template stage picked twice at once (the unique source index) → <see cref="ScopeOfWorkErrors.DuplicateItem"/>.</item>
/// </list>
/// The scope row is always marked modified first, so an edit that only touches item rows still runs the
/// <c>xmin</c> check on the scope and can't silently interleave with another edit.
/// </summary>
internal static class ScopeWrites
{
    public static async Task<Result> SaveAsync(ProjectsDbContext db, ScopeOfWork scope, CancellationToken ct)
    {
        if (db.Entry(scope).State == EntityState.Unchanged)
        {
            db.Entry(scope).State = EntityState.Modified;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ScopeOfWorkErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex) is { } index)
        {
            return index == ScopeOfWorkItemConfiguration.SourceStageIndex
                ? ScopeOfWorkErrors.DuplicateItem
                : ScopeOfWorkErrors.ConcurrentUpdate;
        }
    }

    private static string? UniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && name is ScopeOfWorkConfiguration.ProjectIdIndex or ScopeOfWorkItemConfiguration.SourceStageIndex
            ? name
            : null;
}
