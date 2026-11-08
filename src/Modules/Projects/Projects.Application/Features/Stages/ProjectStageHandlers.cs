using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Application.Features.Projects;
using Projects.Contracts;
using Projects.Contracts.Stages;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Stages;

/// <summary>
/// Per <c>Projects.Domain.ProjectEvents</c>'s own remarks: the domain event
/// (<c>ProjectStageCompletedDomainEvent</c>) drives intra-module gate re-evaluation (G5-08); the
/// integration event is enqueued directly here, in the same <c>SaveChangesAsync</c> — a domain-event
/// handler staging an outbox write would land in a second, separate save.
/// </summary>
internal sealed class CompleteProjectStageCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<CompleteProjectStageCommand>
{
    public async Task<Result> HandleAsync(CompleteProjectStageCommand command, CancellationToken ct = default)
    {
        // P11: hold this stage and every stage its completion unlocks, in id order (GateOwnerLocks), before reading
        // it, so a racing writer is waited for and seen instead of overwritten.
        await GateOwnerLocks.LockCompletionCascadeAsync(db, command.StageId, null, command.StageId, ct);
        ProjectStage? stage = await ProjectWriteLock.LockStageForWriteAsync(db, command.ProjectId, command.StageId, ct)
            ? await db.ProjectStages.FirstOrDefaultAsync(s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct)
            : null;
        if (stage is null)
        {
            return Domain.StageErrors.NotFound;
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(command.UserId, ct);
        if (employeeId is null)
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        // The stage's own state first: a stage that is no longer Active is a transition error whatever its gates
        // and tasks say.
        if (stage.Status != Domain.StageStatus.Active)
        {
            return Domain.StageErrors.InvalidStatusTransition;
        }

        bool hasUnsatisfiedGates = await db.GateConditions.AsNoTracking().AnyAsync(
            g => g.ProjectStageId == stage.Id && g.Trigger == Domain.GateTrigger.Complete && g.Status != Domain.GateStatus.Satisfied, ct);
        if (hasUnsatisfiedGates)
        {
            return Domain.StageErrors.GatesNotSatisfied;
        }

        // NP7: no unfinished task left behind a completed stage.
        bool hasOpenTasks = await db.ProjectTasks.AsNoTracking().AnyAsync(
            t => t.ProjectStageId == stage.Id
                && t.Status != Domain.ProjectTaskStatus.Completed && t.Status != Domain.ProjectTaskStatus.Cancelled, ct);
        if (hasOpenTasks)
        {
            return Domain.StageErrors.TasksOpen;
        }

        Result result = stage.Complete(employeeId.Value);
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectStageCompletedIntegrationEvent(
            Guid.NewGuid(), stage.ProjectId, stage.Id, stage.Name, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>P7: the reason is free text with a ceiling.</summary>
internal sealed class RejectProjectStageCommandValidator : AbstractValidator<RejectProjectStageCommand>
{
    public RejectProjectStageCommandValidator() => RuleFor(x => x.Reason).MaximumLength(2000);
}

internal sealed class RejectProjectStageCommandHandler(ProjectsDbContext db) : ICommandHandler<RejectProjectStageCommand>
{
    public async Task<Result> HandleAsync(RejectProjectStageCommand command, CancellationToken ct = default)
    {
        // P11: hold the stage row first, so a racing writer is waited for and seen instead of overwritten.
        ProjectStage? stage = await ProjectWriteLock.LockStageForWriteAsync(db, command.ProjectId, command.StageId, ct)
            ? await db.ProjectStages.FirstOrDefaultAsync(s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct)
            : null;
        if (stage is null)
        {
            return Domain.StageErrors.NotFound;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        Result result = stage.Reject(command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectStageRejectedIntegrationEvent(
            Guid.NewGuid(), stage.ProjectId, stage.Id, stage.Name, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetProjectStageQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectStageQuery, ProjectStageResponse>
{
    public async Task<Result<ProjectStageResponse>> HandleAsync(GetProjectStageQuery query, CancellationToken ct = default)
    {
        ProjectStageResponse? response = await db.ProjectStages.AsNoTracking()
            .Where(s => s.Id == query.StageId && s.ProjectId == query.ProjectId)
            .Select(ToResponse())
            .FirstOrDefaultAsync(ct);

        return response is null ? Result.Failure<ProjectStageResponse>(Domain.StageErrors.NotFound) : response;
    }

    internal static System.Linq.Expressions.Expression<Func<Domain.ProjectStage, ProjectStageResponse>> ToResponse() => s =>
        new ProjectStageResponse(
            s.Id, s.ProjectId, s.Name, s.Description, s.Order, ProjectsEnumMapping.ToContracts(s.Status),
            s.ProgressPercent, s.CompletedTasks, s.TotalTasks, s.Cost,
            s.StageId, s.Notes, s.EstimatedStartDate, s.EstimatedEndDate, s.ActualStartDate, s.ActualEndDate);
}

/// <summary>P1-A: a caller whose access is only a stage-limited grant sees only the granted stages
/// (<see cref="IProjectAccessGuard.GetStageFilterAsync"/>).</summary>
internal sealed class GetProjectStagesByProjectQueryHandler(ProjectsDbContext db, IProjectAccessGuard guard)
    : IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectStageResponse>>> HandleAsync(
        GetProjectStagesByProjectQuery query, CancellationToken ct = default)
    {
        Guid[]? visible = (await guard.GetStageFilterAsync(
            query.ProjectId, query.UserId, Contracts.ProjectAccessArea.StagesAndTasks, ct))?.ToArray();
        List<ProjectStageResponse> stages = await db.ProjectStages.AsNoTracking()
            .Where(s => s.ProjectId == query.ProjectId)
            .Where(s => visible == null || visible.Contains(s.Id))
            .OrderBy(s => s.Order)
            .Select(GetProjectStageQueryHandler.ToResponse())
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ProjectStageResponse>>(stages);
    }
}
