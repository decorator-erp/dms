using Approvals.Contracts;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;
using Domain = Approvals.Domain;

namespace Approvals.Infrastructure.Services;

/// <summary>
/// The implementation of Approvals' own <see cref="IApprovalRequester"/> contract. Lives here, not in
/// <c>Approvals.Application</c>'s CQRS pipeline — <c>RequestAsync</c> is a synchronous cross-module WRITE
/// call another module's command handler makes directly (e.g. Projects' gate-creation handler, G5-08),
/// not something routed through the outbox: the caller needs the new approval's id back synchronously to
/// store on its own row (<c>GateCondition.ApprovalId</c>), which an outbox-queued event fundamentally
/// can't provide. This mirrors <c>Identity.Contracts.IUserAdministration</c> in spirit — a cross-module
/// write exposed through the OWNING module's own Contracts API, not a bypass of it — but differs in one
/// respect worth recording: <c>IUserAdministration.DeactivateAsync</c> is only ever called from inside an
/// integration-event handler (already decoupled and retriable via the outbox), while this is called
/// directly from a command handler's own transaction. If the caller's own <c>SaveChangesAsync</c> later
/// fails, the <see cref="Domain.Approval"/> row created here has already committed in this module's own
/// schema. That orphan is NOT harmless: it is still open, so every later request for the subject fails
/// with <c>OpenApprovalExists</c>. The caller-supplied <see cref="ApprovalRequest.ApprovalId"/> is the
/// fix short of a saga: a caller that derives the id deterministically from its own row (CRM's brief
/// rounds do) retries into the same approval, and this method returns it — with its decision, if one
/// was already made — instead of creating a second one.
/// </summary>
internal sealed class ApprovalRequester(ApprovalsDbContext db) : IApprovalRequester
{
    public async Task<Result<ApprovalRequestOutcome>> RequestAsync(ApprovalRequest request, CancellationToken ct)
    {
        Domain.ApprovalSubjectType subjectType = ToDomain(request.SubjectType);

        if (await ExistingAsync(request, subjectType, ct) is { } existing)
        {
            return existing;
        }

        bool alreadyOpen = await db.Approvals.AsNoTracking().AnyAsync(
            a => a.SubjectType == subjectType && a.SubjectId == request.SubjectId && a.Decision == null, ct);
        if (alreadyOpen)
        {
            // D-16 / A-F5: the open one may be this very id, committed by a parallel request since the
            // lookup above — that is the same approval, not a conflict.
            return await ExistingAsync(request, subjectType, ct)
                ?? Result.Failure<ApprovalRequestOutcome>(Domain.ApprovalErrors.OpenApprovalExists);
        }

        var approval = Domain.Approval.Create(
            request.ApprovalId,
            subjectType,
            request.SubjectId,
            ToDomain(request.Type),
            request.RequestedByUserId,
            [.. request.Approvers.Select(a => new Domain.ApproverEntry(ToDomain(a.Kind), a.ApproverUserId))],
            request.DueAtUtc);

        db.Approvals.Add(approval);
        db.Outbox().Enqueue(new ApprovalRequestedIntegrationEvent(
            Guid.NewGuid(), approval.Id, request.SubjectType, request.SubjectId, DateTimeOffset.UtcNow));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // D-16 / A-F5: a parallel request inserted the same id first. Drop this attempt (approval and
            // outbox row) and answer with the one that won, as a sequential retry would have been answered.
            db.ChangeTracker.Clear();
            if (await ExistingAsync(request, subjectType, ct) is { } winner)
            {
                return winner;
            }

            throw;
        }

        return new ApprovalRequestOutcome(approval.Id, null);
    }

    /// <summary>The approval already stored under the request's id: returned as-is (with its decision) for
    /// the same subject, <c>ApprovalIdInUse</c> for another; null when there is none.</summary>
    private async Task<Result<ApprovalRequestOutcome>?> ExistingAsync(
        ApprovalRequest request, Domain.ApprovalSubjectType subjectType, CancellationToken ct)
    {
        var existing = await db.Approvals.AsNoTracking()
            .Where(a => a.Id == request.ApprovalId)
            .Select(a => new { a.SubjectType, a.SubjectId, a.Decision })
            .FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return null;
        }

        if (existing.SubjectType != subjectType || existing.SubjectId != request.SubjectId)
        {
            return Result.Failure<ApprovalRequestOutcome>(Domain.ApprovalErrors.ApprovalIdInUse);
        }

        return new ApprovalRequestOutcome(
            request.ApprovalId, existing.Decision.HasValue ? ToContracts(existing.Decision.Value) : null);
    }

    public async Task<ApprovalSnapshot?> GetAsync(Guid approvalId, CancellationToken ct)
    {
        Domain.Approval? approval = await db.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == approvalId, ct);
        return approval is null ? null : ToSnapshot(approval);
    }

    public async Task<IReadOnlyList<ApprovalSnapshot>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        List<Domain.Approval> approvals = await db.Approvals.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        return [.. approvals.Select(ToSnapshot)];
    }

    public async Task<ApprovalSnapshot?> GetLatestForSubjectAsync(
        ApprovalSubjectType subjectType, Guid subjectId, CancellationToken ct) =>
        (await GetLatestForSubjectsAsync(subjectType, [subjectId], ct)).SingleOrDefault();

    public async Task<IReadOnlyList<ApprovalSnapshot>> GetLatestForSubjectsAsync(
        ApprovalSubjectType subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct)
    {
        Domain.ApprovalSubjectType type = ToDomain(subjectType);
        List<Domain.Approval> approvals = await db.Approvals.AsNoTracking()
            .Where(a => a.SubjectType == type && subjectIds.Contains(a.SubjectId))
            .ToListAsync(ct);
        return
        [
            .. approvals
                .GroupBy(a => a.SubjectId)
                .Select(g => g.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id).First())
                .Select(ToSnapshot),
        ];
    }

    private static ApprovalSnapshot ToSnapshot(Domain.Approval a) =>
        new(a.Id, ToContracts(a.SubjectType), a.SubjectId, ToContracts(a.State),
            a.Decision.HasValue ? ToContracts(a.Decision.Value) : null, a.DecidedAtUtc);

    private static Domain.ApprovalSubjectType ToDomain(ApprovalSubjectType t) => t switch
    {
        ApprovalSubjectType.GateCondition => Domain.ApprovalSubjectType.GateCondition,
        ApprovalSubjectType.DesignBrief => Domain.ApprovalSubjectType.DesignBrief,
        ApprovalSubjectType.RequirementItem => Domain.ApprovalSubjectType.RequirementItem,
        ApprovalSubjectType.BriefSpace => Domain.ApprovalSubjectType.BriefSpace,
        ApprovalSubjectType.ProjectDocument => Domain.ApprovalSubjectType.ProjectDocument,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    private static ApprovalSubjectType ToContracts(Domain.ApprovalSubjectType t) => t switch
    {
        Domain.ApprovalSubjectType.GateCondition => ApprovalSubjectType.GateCondition,
        Domain.ApprovalSubjectType.DesignBrief => ApprovalSubjectType.DesignBrief,
        Domain.ApprovalSubjectType.RequirementItem => ApprovalSubjectType.RequirementItem,
        Domain.ApprovalSubjectType.BriefSpace => ApprovalSubjectType.BriefSpace,
        Domain.ApprovalSubjectType.ProjectDocument => ApprovalSubjectType.ProjectDocument,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    private static Domain.ApprovalType ToDomain(ApprovalType t) => t switch
    {
        ApprovalType.ManagerApproval => Domain.ApprovalType.ManagerApproval,
        ApprovalType.ClientReview => Domain.ApprovalType.ClientReview,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    private static Domain.ApproverKind ToDomain(ApproverKind k) => k switch
    {
        ApproverKind.Employee => Domain.ApproverKind.Employee,
        ApproverKind.Client => Domain.ApproverKind.Client,
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, null),
    };

    private static ApprovalState ToContracts(Domain.ApprovalState s) => s switch
    {
        Domain.ApprovalState.Draft => ApprovalState.Draft,
        Domain.ApprovalState.PendingApproval => ApprovalState.PendingApproval,
        Domain.ApprovalState.Approved => ApprovalState.Approved,
        Domain.ApprovalState.Rejected => ApprovalState.Rejected,
        Domain.ApprovalState.NotRequired => ApprovalState.NotRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    private static ApprovalDecision ToContracts(Domain.ApprovalDecision d) => d switch
    {
        Domain.ApprovalDecision.Approved => ApprovalDecision.Approved,
        Domain.ApprovalDecision.Rejected => ApprovalDecision.Rejected,
        Domain.ApprovalDecision.ChangesRequested => ApprovalDecision.ChangesRequested,
        _ => throw new ArgumentOutOfRangeException(nameof(d), d, null),
    };
}
