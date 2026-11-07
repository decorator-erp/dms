using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;

namespace Projects.Infrastructure.Persistence;

/// <summary>
/// crm-complete E2/E3/E5: structural edits of a project's work (stage/task/sub-task create, reorder, delete)
/// take a row lock on the project (<c>FOR UPDATE</c>) and hold it to the end of the transaction, so two
/// concurrent edits can't both read the same orders or both pass a delete-if-unused check. The transaction is
/// the pipeline's (<c>DomainEventDispatchDecorator</c>); one is opened here only for an undecorated caller,
/// as <c>ConfirmScopeOfWorkCommandHandler</c> does, since a lock outside a transaction is released at once.
///
/// <para>Why a project lock and not <c>xmin</c> on the work rows: those rows are also written by the
/// progress recalculation and by gate resolution on every task transition, and a token there would turn
/// those existing, concurrent writes into failures. The lock serialises only the new structural edits.</para>
/// </summary>
internal static class ProjectWriteLock
{
    public static async Task<Result> RunAsync(ProjectsDbContext db, Guid projectId, Func<Task<Result>> work, CancellationToken ct)
    {
        Result<bool> result = await RunAsync(db, projectId, async () =>
        {
            Result inner = await work();
            return inner.IsSuccess ? Result.Success(true) : Result.Failure<bool>(inner.Error);
        }, ct);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    public static async Task<Result<T>> RunAsync<T>(ProjectsDbContext db, Guid projectId, Func<Task<Result<T>>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(db, projectId, ct);
            return await work();
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, projectId, ct);
        Result<T> result = await work();
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        return result;
    }

    /// <summary>FINDING-E-3: a writer that hangs work on a task without updating it (a comment) takes the task row
    /// <c>FOR SHARE</c> for its transaction, so it serialises against a delete holding the
    /// row <c>FOR UPDATE</c>. False when the task is not (or no longer) in the project.</summary>
    public static async Task<bool> ShareTaskAsync(ProjectsDbContext db, Guid projectId, Guid taskId, CancellationToken ct)
    {
        List<Guid> found = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM projects.project_tasks WHERE \"Id\" = {taskId} AND \"ProjectId\" = {projectId} FOR SHARE")
            .ToListAsync(ct);
        return found.Count == 1;
    }

    /// <summary>FINDING-E2-1/E2-2/E2-3: a writer that will UPDATE the task row (start, complete, assign, a sub-task
    /// completion's counters, an Unlock gate's lock) takes it <c>FOR NO KEY UPDATE</c> instead: it still waits for
    /// a delete holding <c>FOR UPDATE</c> (then finds the row gone, false), but two such writers queue rather than
    /// both holding a share lock and deadlocking on their updates. FK key-share checks are not blocked.</summary>
    public static async Task<bool> LockTaskForWriteAsync(ProjectsDbContext db, Guid projectId, Guid taskId, CancellationToken ct)
    {
        List<Guid> found = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM projects.project_tasks WHERE \"Id\" = {taskId} AND \"ProjectId\" = {projectId} FOR NO KEY UPDATE")
            .ToListAsync(ct);
        return found.Count == 1;
    }

    /// <summary>The stage form of <see cref="LockTaskForWriteAsync"/>, for a stage gate's creation.</summary>
    public static async Task<bool> LockStageForWriteAsync(ProjectsDbContext db, Guid projectId, Guid stageId, CancellationToken ct)
    {
        List<Guid> found = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM projects.project_stages WHERE \"Id\" = {stageId} AND \"ProjectId\" = {projectId} FOR NO KEY UPDATE")
            .ToListAsync(ct);
        return found.Count == 1;
    }

    /// <summary>FINDING-E-1 backstop: an FK violation on a hard delete (a row in another module that the
    /// contract checks could not see yet) is a 409, not a 500.</summary>
    public static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation };

    private static Task<int> LockAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM projects.projects WHERE \"Id\" = {projectId} FOR UPDATE", ct);
}
