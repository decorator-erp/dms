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
    /// <summary>Runs <paramref name="decide"/> in a transaction after taking <see cref="LockForDecideAsync"/>.
    /// <paramref name="decide"/> loads the approval itself, after the lock, and saves.
    ///
    /// <para>Two branches. <b>Ambient:</b> the office <c>DecideApprovalCommandHandler</c> always runs inside the
    /// transaction <c>DomainEventDispatchDecorator</c> opens around every command, so the lock joins it and the
    /// decorator commits or rolls back. <b>Owned:</b> the portal's <see cref="ApprovalDecider"/> is not an
    /// Approvals command, so <c>ApprovalsDbContext</c> has no ambient transaction; one is opened here and
    /// committed only on success.</para>
    ///
    /// <para>The owned branch <i>is</i> reached from inside another module's transaction: CRM's
    /// <c>DecideMyPortalApprovalCommandHandler</c> calls the decider within CRM's own
    /// <c>DomainEventDispatchDecorator</c> transaction, which is on <c>CrmDbContext</c>'s connection, not this
    /// one (FINDING-P1-18). So the decision commits here, independently, before CRM's transaction ends, and a
    /// CRM rollback cannot undo it. That is safe only while the invariant holds that <b>CRM writes nothing after
    /// the decide call</b>; the subject reacts later from the outbox (<c>ApprovalDecidedIntegrationEvent</c>).
    /// <c>PortalApprovalTests.PortalDecide_MakesNoCrmWrite_AfterApprovalsCommitsTheDecision</c> pins it.</para>
    ///
    /// <para>Both map a lost race (<see cref="IsLostRace"/>) to <c>AlreadyDecided</c> (409) (FINDING-P1-14). In
    /// the ambient branch the caller's transaction is neither disposed nor rolled back here: returning the failure
    /// is enough, because the decorator rolls back on a failure <see cref="Result"/> — which is also all a
    /// Postgres-aborted transaction is still good for.</para></summary>
    public static async Task<Result> DecideLockedAsync(
        ApprovalsDbContext db, Guid approvalId, Func<Task<Result>> decide, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            try
            {
                await LockForDecideAsync(db, approvalId, ct);
                return await decide();
            }
            catch (Exception ex) when (IsLostRace(ex))
            {
                // The lock makes this unreachable in the normal path; see the owned branch below.
                db.ChangeTracker.Clear();
                return Domain.ApprovalErrors.AlreadyDecided;
            }
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
