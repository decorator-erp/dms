using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Gates;

/// <summary>Always valid — the manual-satisfaction path (<c>SatisfyGateConditionCommand</c>) has nothing
/// to check at creation time.</summary>
internal sealed class ManualCheckHandler : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.ManualCheck;

    public Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());
}

/// <summary><see cref="GateConditionContext.ReferencedStageId"/> required; must not self-reference the
/// gate's own owner (only possible when the owner is itself a stage); the referenced stage must exist
/// and belong to the same project.</summary>
internal sealed class StageReferenceHandler(ProjectsDbContext db) : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.Stage;

    public async Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default)
    {
        if (context.ReferencedStageId is not { } referencedStageId)
        {
            return Domain.GateConditionErrors.InvalidReference;
        }

        if (context.OwnerKind == GateOwnerKind.Stage && context.OwnerId == referencedStageId)
        {
            return Domain.GateConditionErrors.SelfReference;
        }

        Guid? projectId = await db.ProjectStages.AsNoTracking()
            .Where(s => s.Id == referencedStageId)
            .Select(s => (Guid?)s.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (projectId is null)
        {
            return Domain.GateConditionErrors.ReferenceNotFound;
        }

        if (projectId != context.OwnerProjectId)
        {
            return Domain.GateConditionErrors.CrossProjectReference;
        }

        return Result.Success();
    }
}

/// <summary>Identical shape to <see cref="StageReferenceHandler"/>, for <c>ReferencedTaskId</c>.</summary>
internal sealed class TaskReferenceHandler(ProjectsDbContext db) : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.Task;

    public async Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default)
    {
        if (context.ReferencedTaskId is not { } referencedTaskId)
        {
            return Domain.GateConditionErrors.InvalidReference;
        }

        if (context.OwnerKind == GateOwnerKind.Task && context.OwnerId == referencedTaskId)
        {
            return Domain.GateConditionErrors.SelfReference;
        }

        Guid? projectId = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.Id == referencedTaskId)
            .Select(t => (Guid?)t.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (projectId is null)
        {
            return Domain.GateConditionErrors.ReferenceNotFound;
        }

        if (projectId != context.OwnerProjectId)
        {
            return Domain.GateConditionErrors.CrossProjectReference;
        }

        return Result.Success();
    }
}

/// <summary>Only checks <see cref="GateConditionContext.ApprovalType"/> is present — the actual
/// request/decide flow is orchestrated by <c>CreateStageGateConditionCommandHandler</c>/
/// <c>CreateTaskGateConditionCommandHandler</c> at creation time (calling <c>IApprovalRequester</c>) and
/// by <c>ApprovalDecidedIntegrationEventHandler</c> at decision time, not by this handler.</summary>
internal sealed class ApprovalHandler : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.Approval;

    public Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default) =>
        Task.FromResult(context.ApprovalType is null ? Domain.GateConditionErrors.InvalidReference : Result.Success());
}
