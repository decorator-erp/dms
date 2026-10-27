using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Application.Features.Items;
using Crm.Contracts;
using Crm.Contracts.Periods;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
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
internal sealed class CloseCrmRequirementPeriodCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory) : ICommandHandler<CloseCrmRequirementPeriodCommand>
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

        return await ItemWrites.SaveAsync(db, ct, CrmPeriodErrors.ConcurrentUpdate);
    }
}
