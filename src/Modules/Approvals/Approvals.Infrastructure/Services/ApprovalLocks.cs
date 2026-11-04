using Approvals.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel;
using Domain = Approvals.Domain;

namespace Approvals.Infrastructure.Services;

/// <summary>
/// The one way an approval is decided under a lock, shared by both decide paths: the portal's
/// <see cref="ApprovalDecider"/> and the office <c>DecideApprovalCommandHandler</c> (FINDING-P1-2).
///
/// <para><see cref="Domain.Approval"/> has no concurrency token, so the row is locked
/// (<c>SELECT … FOR UPDATE</c>) in a transaction <b>before</b> the entity is loaded. A parallel decision —
/// from either path — waits on the lock, then loads the committed row, sees <c>Decision</c> set and answers
/// <c>AlreadyDecided</c> (409). No migration is needed for this.</para>
/// </summary>
internal static class ApprovalLocks
{
    /// <summary>Runs <paramref name="decide"/> in a transaction (the caller's, if one is open; else a new one
    /// committed only on success) after taking <see cref="LockForDecideAsync"/>. <paramref name="decide"/>
    /// loads the approval itself, after the lock, and saves.</summary>
    public static async Task<Result> DecideLockedAsync(
        ApprovalsDbContext db, Guid approvalId, Func<Task<Result>> decide, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await LockForDecideAsync(db, approvalId, ct);
            return await decide();
        }

        try
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            await LockForDecideAsync(db, approvalId, ct);
            Result result = await decide();
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
        catch (Exception ex) when (IsLostRace(ex))
        {
            // The lock makes this unreachable in the normal path; a serialization failure, deadlock or
            // concurrency exception here still means another decision got there first.
            db.ChangeTracker.Clear();
            return Domain.ApprovalErrors.AlreadyDecided;
        }
    }

    /// <summary>Lock first, load second: a load after this reads the row as the previous lock holder
    /// committed it. Must run inside a transaction.</summary>
    public static Task LockForDecideAsync(ApprovalsDbContext db, Guid approvalId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM approvals.approvals WHERE "Id" = {approvalId} FOR UPDATE""", ct);

    private static bool IsLostRace(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => true,
        DbUpdateException { InnerException: PostgresException pg } => IsLostRace(pg),
        PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure
                or PostgresErrorCodes.DeadlockDetected,
        } => true,
        _ => false,
    };
}
