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

    private static Task<int> LockAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM projects.projects WHERE \"Id\" = {projectId} FOR UPDATE", ct);
}
