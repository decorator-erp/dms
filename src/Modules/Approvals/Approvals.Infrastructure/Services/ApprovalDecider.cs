using Approvals.Contracts;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel;
using Domain = Approvals.Domain;

namespace Approvals.Infrastructure.Services;

/// <summary>
/// B1.1: the implementation of <see cref="IApprovalDecider"/>, beside <see cref="ApprovalRequester"/> for
/// the same reason — a synchronous cross-module write exposed through the owning module's Contracts.
///
/// <para><b>Serialization.</b> <see cref="Domain.Approval"/> has no concurrency token, so the row is locked
/// (<c>SELECT … FOR UPDATE</c>) in an explicit transaction <b>before</b> the entity is loaded. A parallel
/// decision waits on the lock, then loads the committed row, sees <c>Decision</c> set and answers
/// <c>AlreadyDecided</c> (409). No migration is needed for this.</para>
///
/// <para><b>Who may decide.</b> Only an approver entry of kind <see cref="ApproverKind.Client"/> naming the
/// caller. An unknown approval and one the caller is not a client approver on are the same 404, so the
/// portal learns nothing about approvals that aren't theirs. Never an override.</para>
///
/// <para>The decision and its <see cref="ApprovalDecidedIntegrationEvent"/> are saved together, exactly as
/// the office <c>DecideApprovalCommandHandler</c> does.</para>
/// </summary>
internal sealed class ApprovalDecider(ApprovalsDbContext db) : IApprovalDecider
{
    private const int MaxCommentLength = 2000;

    public async Task<Result> DecideAsClientAsync(
        Guid approvalId, string appUserId, ApprovalDecision decision, string? comment, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await DecideAsync(approvalId, appUserId, decision, comment, ct);
        }

        try
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            Result result = await DecideAsync(approvalId, appUserId, decision, comment, ct);
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

    private async Task<Result> DecideAsync(
        Guid approvalId, string appUserId, ApprovalDecision decision, string? comment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appUserId))
        {
            return Domain.ApprovalErrors.NotFound;
        }

        // Lock first, load second: the load then reads the row as the previous lock holder committed it.
        await db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM approvals.approvals WHERE "Id" = {approvalId} FOR UPDATE""", ct);

        Domain.Approval? approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);
        if (approval is null
            || !approval.Approvers.Any(a => a.Kind == Domain.ApproverKind.Client && a.ApproverUserId == appUserId))
        {
            return Domain.ApprovalErrors.NotFound;
        }

        if (comment is { Length: > MaxCommentLength })
        {
            return Domain.ApprovalErrors.CommentTooLong;
        }

        Result decided = approval.Decide(appUserId, ApprovalRequester.ToDomain(decision), comment, isOverride: false);
        if (decided.IsFailure)
        {
            return decided;
        }

        db.Outbox().Enqueue(new ApprovalDecidedIntegrationEvent(
            Guid.NewGuid(), approval.Id, ApprovalRequester.ToContracts(approval.SubjectType), approval.SubjectId,
            decision, appUserId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

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
