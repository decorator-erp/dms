using Approvals.Contracts;
using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Application.Features.Projects;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Gates;

internal sealed class CreateStageGateConditionCommandValidator : AbstractValidator<CreateStageGateConditionCommand>
{
    public CreateStageGateConditionCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);

        // P7: unknown enum values are a 400.
        RuleFor(x => x.ConditionType).IsInEnum();
        RuleFor(x => x.Trigger).IsInEnum();
        RuleFor(x => x.ApprovalType).IsInEnum().When(x => x.ApprovalType is not null);
    }
}

/// <summary>
/// Resolves the matching <see cref="IGateConditionTypeHandler"/> for create-time validation, then per
/// the facts table: an <see cref="GateTrigger.Unlock"/>-trigger gate locks its owner immediately (NP2: only an owner
/// that is open or already Locked, else 409 <c>gate_conditions.OwnerNotOpen</c>), and an
/// <see cref="ConditionType.Approval"/> gate requests the approval end-to-end (decision #1: eligible
/// approvers are the project owner and the owning stage's team head, resolved to their
/// <c>Employee.AppUserId</c> via <see cref="IHrDirectory"/> — Approvals never sees an Employee id).
/// </summary>
internal sealed class CreateStageGateConditionCommandHandler(
    ProjectsDbContext db, IEnumerable<IGateConditionTypeHandler> typeHandlers,
    IApprovalRequester approvalRequester, IHrDirectory hrDirectory, BuildingBlocks.ICurrentUser currentUser)
    : ICommandHandler<CreateStageGateConditionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateStageGateConditionCommand command, CancellationToken ct = default)
    {
        // FINDING-E2-2: hold the stage row against a racing stage delete (no orphan gate or approval request).
        Domain.ProjectStage? stage = await ProjectWriteLock.LockStageForWriteAsync(db, command.ProjectId, command.StageId, ct)
            ? await db.ProjectStages.FirstOrDefaultAsync(s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct)
            : null;
        if (stage is null)
        {
            return Result.Failure<Guid>(Domain.StageErrors.NotFound);
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return Result.Failure<Guid>(live.Error);
        }

        if (command.Trigger == GateTrigger.Unlock && !stage.IsLockable)
        {
            return Result.Failure<Guid>(Domain.GateConditionErrors.OwnerNotOpen);
        }

        IGateConditionTypeHandler? typeHandler = typeHandlers.SingleOrDefault(h => h.Type == command.ConditionType);
        if (typeHandler is null)
        {
            return Result.Failure<Guid>(Domain.GateConditionErrors.UnsupportedConditionType);
        }

        var context = new GateConditionContext(
            stage.Id, command.ProjectId, GateOwnerKind.Stage, command.ReferencedTaskId, command.ReferencedStageId,
            command.ApprovalType, command.ReferencedDocumentId);
        Result validation = await typeHandler.ValidateAsync(context, ct);
        if (validation.IsFailure)
        {
            return Result.Failure<Guid>(validation.Error);
        }

        Result<Domain.GateCondition> gateResult = Domain.GateCondition.Create(
            stage.Id, null, ProjectsEnumMapping.ToDomain(command.ConditionType), ProjectsEnumMapping.ToDomain(command.Trigger),
            command.Label, command.ReferencedTaskId, command.ReferencedStageId,
            command.ApprovalType is { } at ? ProjectsEnumMapping.ToDomain(at) : null, command.ReferencedDocumentId);
        if (gateResult.IsFailure)
        {
            return Result.Failure<Guid>(gateResult.Error);
        }

        Domain.GateCondition gate = gateResult.Value!;
        db.GateConditions.Add(gate);

        if (command.Trigger == GateTrigger.Unlock)
        {
            stage.Lock();
        }

        if (command.ConditionType == ConditionType.Approval)
        {
            Result approvalResult = await GateApprovalRequests.RequestAsync(
                db, approvalRequester, hrDirectory, currentUser, gate, command.ProjectId, stage.Id, command.ApprovalType!.Value, ct);
            if (approvalResult.IsFailure)
            {
                return Result.Failure<Guid>(approvalResult.Error);
            }
        }

        await db.SaveChangesAsync(ct);
        return gate.Id;
    }
}

internal sealed class CreateTaskGateConditionCommandValidator : AbstractValidator<CreateTaskGateConditionCommand>
{
    public CreateTaskGateConditionCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);

        // P7: unknown enum values are a 400.
        RuleFor(x => x.ConditionType).IsInEnum();
        RuleFor(x => x.Trigger).IsInEnum();
        RuleFor(x => x.ApprovalType).IsInEnum().When(x => x.ApprovalType is not null);
    }
}

internal sealed class CreateTaskGateConditionCommandHandler(
    ProjectsDbContext db, IEnumerable<IGateConditionTypeHandler> typeHandlers,
    IApprovalRequester approvalRequester, IHrDirectory hrDirectory, BuildingBlocks.ICurrentUser currentUser)
    : ICommandHandler<CreateTaskGateConditionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateTaskGateConditionCommand command, CancellationToken ct = default)
    {
        // FINDING-E2-2: hold the task row against a racing task delete (no orphan gate or approval request).
        Domain.ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Result.Failure<Guid>(Domain.TaskErrors.NotFound);
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return Result.Failure<Guid>(live.Error);
        }

        if (command.Trigger == GateTrigger.Unlock && !task.IsLockable)
        {
            return Result.Failure<Guid>(Domain.GateConditionErrors.OwnerNotOpen);
        }

        IGateConditionTypeHandler? typeHandler = typeHandlers.SingleOrDefault(h => h.Type == command.ConditionType);
        if (typeHandler is null)
        {
            return Result.Failure<Guid>(Domain.GateConditionErrors.UnsupportedConditionType);
        }

        var context = new GateConditionContext(
            task.Id, command.ProjectId, GateOwnerKind.Task, command.ReferencedTaskId, command.ReferencedStageId,
            command.ApprovalType, command.ReferencedDocumentId);
        Result validation = await typeHandler.ValidateAsync(context, ct);
        if (validation.IsFailure)
        {
            return Result.Failure<Guid>(validation.Error);
        }

        Result<Domain.GateCondition> gateResult = Domain.GateCondition.Create(
            null, task.Id, ProjectsEnumMapping.ToDomain(command.ConditionType), ProjectsEnumMapping.ToDomain(command.Trigger),
            command.Label, command.ReferencedTaskId, command.ReferencedStageId,
            command.ApprovalType is { } at ? ProjectsEnumMapping.ToDomain(at) : null, command.ReferencedDocumentId);
        if (gateResult.IsFailure)
        {
            return Result.Failure<Guid>(gateResult.Error);
        }

        Domain.GateCondition gate = gateResult.Value!;
        db.GateConditions.Add(gate);

        if (command.Trigger == GateTrigger.Unlock)
        {
            task.Lock();
        }

        if (command.ConditionType == ConditionType.Approval)
        {
            Result approvalResult = await GateApprovalRequests.RequestAsync(
                db, approvalRequester, hrDirectory, currentUser, gate, command.ProjectId, task.ProjectStageId, command.ApprovalType!.Value, ct);
            if (approvalResult.IsFailure)
            {
                return Result.Failure<Guid>(approvalResult.Error);
            }
        }

        await db.SaveChangesAsync(ct);
        return gate.Id;
    }
}

/// <summary>Shared by both create handlers above — identical regardless of whether the gate's owner is a
/// stage or a task, since the approver set (project owner + owning stage's team head, decision #1) only
/// ever depends on the PROJECT and, when one exists, the STAGE (a task's own <c>ProjectStageId</c> is
/// null for free-style tasks, in which case only the project owner is eligible).</summary>
internal static class GateApprovalRequests
{
    public static async Task<Result> RequestAsync(
        ProjectsDbContext db, IApprovalRequester approvalRequester, IHrDirectory hrDirectory,
        BuildingBlocks.ICurrentUser currentUser, Domain.GateCondition gate, Guid projectId, Guid? projectStageId,
        global::Projects.Contracts.ApprovalType approvalType, CancellationToken ct)
    {
        var employeeIds = new List<Guid>();

        Guid? ownerId = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerId)
            .FirstOrDefaultAsync(ct);
        if (ownerId is not null)
        {
            employeeIds.Add(ownerId.Value);
        }

        if (projectStageId is { } stageId)
        {
            // The stage's first team (created first, then by id): a stage has one team per department, and without the
            // order the approver was whichever row Postgres returned first.
            Guid? headId = await db.StageTeams.AsNoTracking()
                .Where(t => t.ProjectStageId == stageId)
                .OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
                .Select(t => (Guid?)t.HeadId)
                .FirstOrDefaultAsync(ct);
            if (headId is not null)
            {
                employeeIds.Add(headId.Value);
            }
        }

        IReadOnlyList<EmployeeSummary> summaries = await hrDirectory.GetManyAsync(employeeIds.Distinct().ToList(), ct);
        if (summaries.Count == 0)
        {
            return Domain.GateConditionErrors.InvalidReference;
        }

        var approvers = summaries.Select(s => new ApproverSpec(ApproverKind.Employee, s.AppUserId)).ToList();
        string requestedByUserId = currentUser.UserId ?? approvers[0].ApproverUserId;

        // B-P-M7: the approval id is derived from the gate id (GateApprovalIds), so a repeated request for this gate
        // returns the approval already made instead of opening a second one.
        Result<ApprovalRequestOutcome> requestResult = await approvalRequester.RequestAsync(
            new ApprovalRequest(
                GateApprovalIds.For(gate.Id), ApprovalSubjectType.GateCondition, gate.Id,
                (global::Approvals.Contracts.ApprovalType)approvalType, requestedByUserId, approvers, DueAtUtc: null,
                ProjectId: projectId),
            ct);
        if (requestResult.IsFailure)
        {
            return requestResult.Error;
        }

        ApprovalRequestOutcome outcome = requestResult.Value!;
        gate.LinkApproval(outcome.ApprovalId);

        // A decision made before this gate committed found no gate to bind to and was dropped; apply it here, as
        // ApprovalDecidedIntegrationEventHandler would have (CRM's CrmApprovalOutcomes pattern).
        switch (outcome.Decision)
        {
            case ApprovalDecision.Approved:
                gate.MarkSatisfied(null);
                break;
            case ApprovalDecision.Rejected:
                gate.MarkRejected(null);
                await GateOwnerLocks.ReleaseOnRejectionAsync(db, gate, ct);
                break;
        }

        return Result.Success();
    }
}

/// <summary>P4: route claim (either gate permission) → scope (404) → the owner's own gate permission
/// (<c>stages.manage_gates</c> for a stage gate, <c>stage_tasks.manage_gates</c> for a task gate; 403) → state (409):
/// a satisfied gate, or an approval gate still waiting for its decision (its request is open in the Approvals inbox),
/// is <see cref="Domain.GateConditionErrors.NotDeletable"/>. Deleting the owner's last pending Unlock gate releases
/// the owner (<see cref="GateOwnerLocks"/>) instead of leaving it Locked for good.</summary>
internal sealed class DeleteGateConditionCommandHandler(ProjectsDbContext db, BuildingBlocks.ICurrentUser currentUser)
    : ICommandHandler<DeleteGateConditionCommand>
{
    public async Task<Result> HandleAsync(DeleteGateConditionCommand command, CancellationToken ct = default)
    {
        Domain.GateCondition? gate = await db.GateConditions.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == command.GateConditionId, ct);
        if (gate is null || !await GateOwners.LockForProjectAsync(db, gate, command.ProjectId, ct))
        {
            return Domain.GateConditionErrors.NotFound;
        }

        if (!GateOwners.MayManage(currentUser, gate))
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        gate = await db.GateConditions.FirstOrDefaultAsync(g => g.Id == command.GateConditionId, ct);
        if (gate is null)
        {
            return Domain.GateConditionErrors.NotFound;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        if (gate.IsSatisfied || gate.ConditionType == Domain.ConditionType.Approval && gate.IsPending)
        {
            return Domain.GateConditionErrors.NotDeletable;
        }

        db.GateConditions.Remove(gate);
        await GateOwnerLocks.ReleaseIfLastAsync(db, gate, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class SatisfyGateConditionCommandValidator : AbstractValidator<SatisfyGateConditionCommand>
{
    public SatisfyGateConditionCommandValidator() => RuleFor(x => x.Note).MaximumLength(2000);
}

/// <summary>Manual satisfaction (the <c>ManualCheckHandler</c> path) — approval-type gates are satisfied
/// reactively by <c>ApprovalDecidedIntegrationEventHandler</c>, never through this command.
///
/// <para>P3: only a <see cref="Domain.ConditionType.ManualCheck"/> gate that is still Pending (409
/// <see cref="Domain.GateConditionErrors.NotManuallySatisfiable"/> otherwise), so a rejected approval, document or
/// budget gate can't be forced through by hand. The owner row is taken first (<c>FOR NO KEY UPDATE</c>), so the
/// unlock the satisfaction cascades into (<c>GateOwnerLocks</c>) and a racing gate change on the same owner
/// queue instead of interleaving.</para></summary>
internal sealed class SatisfyGateConditionCommandHandler(
    ProjectsDbContext db, IHrDirectory hrDirectory, BuildingBlocks.ICurrentUser currentUser)
    : ICommandHandler<SatisfyGateConditionCommand>
{
    public async Task<Result> HandleAsync(SatisfyGateConditionCommand command, CancellationToken ct = default)
    {
        Domain.GateCondition? gate = await db.GateConditions.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == command.GateConditionId, ct);
        if (gate is null || !await GateOwners.LockForProjectAsync(db, gate, command.ProjectId, ct))
        {
            return Domain.GateConditionErrors.NotFound;
        }

        if (!GateOwners.MayManage(currentUser, gate))
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        // Re-read under the owner lock: a racing satisfy or delete committed while this one waited.
        gate = await db.GateConditions.FirstOrDefaultAsync(g => g.Id == command.GateConditionId, ct);
        if (gate is null)
        {
            return Domain.GateConditionErrors.NotFound;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        if (gate.ConditionType != Domain.ConditionType.ManualCheck || !gate.IsPending)
        {
            return Domain.GateConditionErrors.NotManuallySatisfiable;
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(command.UserId, ct);
        if (employeeId is null)
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        Result result = gate.MarkSatisfied(employeeId.Value, command.Note);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>The gate's owner (a stage or a task), checked against the project and locked.</summary>
internal static class GateOwners
{
    /// <summary>Takes the owner row <c>FOR NO KEY UPDATE</c>; false when the owner is not (or no longer) in the
    /// project.</summary>
    public static Task<bool> LockForProjectAsync(ProjectsDbContext db, Domain.GateCondition gate, Guid projectId, CancellationToken ct) =>
        gate.ProjectStageId is { } stageId
            ? ProjectWriteLock.LockStageForWriteAsync(db, projectId, stageId, ct)
            : ProjectWriteLock.LockTaskForWriteAsync(db, projectId, gate.ProjectTaskId!.Value, ct);

    /// <summary>P4: a stage gate is managed under <c>stages.manage_gates</c>, a task gate under
    /// <c>stage_tasks.manage_gates</c> (decision #9), whichever of the two let the caller onto the shared route.</summary>
    public static bool MayManage(BuildingBlocks.ICurrentUser currentUser, Domain.GateCondition gate) =>
        currentUser.HasPermission(gate.ProjectStageId is not null
            ? ProjectsPermissions.Stages.ManageGates
            : ProjectsPermissions.StageTasks.ManageGates);
}

internal sealed class GetStageGateConditionsQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>>
{
    public async Task<Result<IReadOnlyList<GateConditionResponse>>> HandleAsync(
        GetStageGateConditionsQuery query, CancellationToken ct = default)
    {
        bool stageExists = await db.ProjectStages.AsNoTracking()
            .AnyAsync(s => s.Id == query.StageId && s.ProjectId == query.ProjectId, ct);
        if (!stageExists)
        {
            return Result.Failure<IReadOnlyList<GateConditionResponse>>(Domain.StageErrors.NotFound);
        }

        List<GateConditionResponse> gates = await db.GateConditions.AsNoTracking()
            .Where(g => g.ProjectStageId == query.StageId)
            .Select(ToResponse())
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<GateConditionResponse>>(gates);
    }

    internal static System.Linq.Expressions.Expression<Func<Domain.GateCondition, GateConditionResponse>> ToResponse() => g =>
        new GateConditionResponse(
            g.Id, g.ProjectStageId, g.ProjectTaskId, ProjectsEnumMapping.ToContracts(g.ConditionType),
            ProjectsEnumMapping.ToContracts(g.Trigger), g.Label, g.ReferencedTaskId, g.ReferencedStageId,
            g.ApprovalType == null ? null : ProjectsEnumMapping.ToContracts(g.ApprovalType.Value),
            ProjectsEnumMapping.ToContracts(g.Status), g.ApprovalId,
            g.ReferencedDocumentId, g.ResolvedById, g.ResolvedAt, g.ResolutionNote);
}

internal sealed class GetTaskGateConditionsQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>
{
    public async Task<Result<IReadOnlyList<GateConditionResponse>>> HandleAsync(
        GetTaskGateConditionsQuery query, CancellationToken ct = default)
    {
        bool taskExists = await db.ProjectTasks.AsNoTracking()
            .AnyAsync(t => t.Id == query.TaskId && t.ProjectId == query.ProjectId, ct);
        if (!taskExists)
        {
            return Result.Failure<IReadOnlyList<GateConditionResponse>>(Domain.TaskErrors.NotFound);
        }

        List<GateConditionResponse> gates = await db.GateConditions.AsNoTracking()
            .Where(g => g.ProjectTaskId == query.TaskId)
            .Select(GetStageGateConditionsQueryHandler.ToResponse())
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<GateConditionResponse>>(gates);
    }
}
