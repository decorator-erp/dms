using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Periods;

/// <summary>
/// B3: the one close sequence, shared by <see cref="CloseCrmRequirementPeriodCommandHandler"/> (by hand)
/// and <c>CrmPeriodCloseJob</c> (on the clock), so a gate never behaves differently depending on WHO
/// closed the window.
///
/// <para>The sequence: <c>Close()</c>; <see cref="CrmPeriodClosedIntegrationEvent"/> when the period is
/// stage-scoped; capture the scopes of the Draft and Rejected items the close drops from the gate
/// (FINDING-P1-2 / D-11); save; then <see cref="CrmApprovalOutcomes.PublishIfResolvedAsync"/> per
/// captured stage and task, and save again. The second step reads the database (the gate counts are
/// AsNoTracking), which is why the first save comes before it.</para>
///
/// <para>It opens no transaction: the caller owns one. The command pipeline's ambient transaction
/// covers the handler; the job opens its own per period, so the close and its events commit together.</para>
/// </summary>
internal static class CrmPeriodCloser
{
    public static async Task<Result> CloseAsync(
        CrmDbContext db, CrmApprovalOutcomes outcomes, CrmRequirementPeriod period, DateTime nowUtc, CancellationToken ct)
    {
        Result result = period.Close(nowUtc);
        if (result.IsFailure)
        {
            return result;
        }

        if (period.ProjectStageId is { } stageId)
        {
            db.Outbox().Enqueue(new CrmPeriodClosedIntegrationEvent(
                Guid.NewGuid(), period.Id, stageId, DateTimeOffset.UtcNow));
        }

        // The close drops this period's Draft and Rejected items from the gate, so it may be what resolves
        // a stage or task. Those scopes are captured before the close is saved, and checked after, exactly
        // as a decision does.
        List<Guid?> dropped = await db.CrmRequirementItems.AsNoTracking()
            .Where(i => i.PeriodId == period.Id
                && (i.ApprovalStatus == ApprovalState.Draft || i.ApprovalStatus == ApprovalState.Rejected)
                && !db.CrmRequirementItems.Any(next => next.SupersedesItemId == i.Id))
            .Select(i => i.ProjectTaskId)
            .ToListAsync(ct);

        Result saved = await ItemWrites.SaveAsync(db, ct, CrmPeriodErrors.ConcurrentUpdate);
        if (saved.IsFailure || dropped.Count == 0)
        {
            return saved;
        }

        await outcomes.PublishIfResolvedAsync(period.ProjectStageId, null, ct);
        foreach (Guid taskId in dropped.OfType<Guid>().Distinct())
        {
            await outcomes.PublishIfResolvedAsync(null, taskId, ct);
        }

        return await ItemWrites.SaveAsync(db, ct, CrmPeriodErrors.ConcurrentUpdate);
    }
}
