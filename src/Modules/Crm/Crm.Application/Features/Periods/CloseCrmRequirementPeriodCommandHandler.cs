using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Application.Features.Items;
using Crm.Contracts;
using Crm.Contracts.Periods;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Periods;

internal sealed class CloseCrmRequirementPeriodCommandValidator : AbstractValidator<CloseCrmRequirementPeriodCommand>
{
    public CloseCrmRequirementPeriodCommandValidator() => RuleFor(x => x.PeriodId).NotEmpty();
}

/// <summary>One of the three CRM gate-satisfaction triggers. Publishes
/// <see cref="CrmPeriodClosedIntegrationEvent"/> only when <see cref="CrmRequirementPeriod.ProjectStageId"/>
/// is set — a project-wide period (no stage) has no gate to satisfy. <c>Projects.Infrastructure</c>
/// subscribes at G9-13; publishing here regardless of whether that subscriber exists yet matches every
/// other module's own "publish with zero knowledge of who reacts" convention.</summary>
internal sealed class CloseCrmRequirementPeriodCommandHandler(
    CrmDbContext db, IProjectDirectory projectDirectory, CrmApprovalOutcomes outcomes) : ICommandHandler<CloseCrmRequirementPeriodCommand>
{
    public async Task<Result> HandleAsync(CloseCrmRequirementPeriodCommand command, CancellationToken ct = default)
    {
        CrmRequirementPeriod? period = await db.CrmRequirementPeriods.FindAsync([command.PeriodId], ct);
        if (period is null)
        {
            return CrmPeriodErrors.NotFound;
        }

        Result active = await ItemReferenceValidator.EnsureProjectActiveAsync(projectDirectory, period.ProjectId, ct);
        if (active.IsFailure)
        {
            return active;
        }

        Result result = period.Close(DateTime.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        if (period.ProjectStageId is { } stageId)
        {
            db.Outbox().Enqueue(new CrmPeriodClosedIntegrationEvent(
                Guid.NewGuid(), period.Id, stageId, DateTimeOffset.UtcNow));
        }

        // FINDING-P1-2 (D-11): the close drops this period's Draft and Rejected items from the gate, so it
        // may be what resolves a stage or task. Those scopes are captured before the close is saved, and
        // checked after (the gate counts read the database), exactly as a decision does.
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
