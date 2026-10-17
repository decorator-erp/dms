using Approvals.Contracts;
using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Gates;

internal sealed class CreateStageGateConditionCommandValidator : AbstractValidator<CreateStageGateConditionCommand>
{
    public CreateStageGateConditionCommandValidator() => RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
}

/// <summary>
/// Resolves the matching <see cref="IGateConditionTypeHandler"/> for create-time validation, then per
/// the facts table: an <see cref="GateTrigger.Unlock"/>-trigger gate locks its owner immediately, and an
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
        Domain.ProjectStage? stage = await db.ProjectStages.FirstOrDefaultAsync(
            s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct);
        if (stage is null)
        {
            return Result.Failure<Guid>(Domain.StageErrors.NotFound);
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
    public CreateTaskGateConditionCommandValidator() => RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
}

internal sealed class CreateTaskGateConditionCommandHandler(
    ProjectsDbContext db, IEnumerable<IGateConditionTypeHandler> typeHandlers,
    IApprovalRequester approvalRequester, IHrDirectory hrDirectory, BuildingBlocks.ICurrentUser currentUser)
    : ICommandHandler<CreateTaskGateConditionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateTaskGateConditionCommand command, CancellationToken ct = default)
    {
        Domain.ProjectTask? task = await db.ProjectTasks.FirstOrDefaultAsync(
            t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct);
        if (task is null)
        {
            return Result.Failure<Guid>(Domain.TaskErrors.NotFound);
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
            Guid? headId = await db.StageTeams.AsNoTracking()
                .Where(t => t.ProjectStageId == stageId)
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

        Result<Guid> requestResult = await approvalRequester.RequestAsync(
            new ApprovalRequest(
                ApprovalSubjectType.GateCondition, gate.Id, (global::Approvals.Contracts.ApprovalType)approvalType,
                requestedByUserId, approvers, DueAtUtc: null),
            ct);
        if (requestResult.IsFailure)
        {
            return requestResult.Error;
        }

        gate.LinkApproval(requestResult.Value);
        return Result.Success();
    }
}

internal sealed class DeleteGateConditionCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteGateConditionCommand>
{
    public async Task<Result> HandleAsync(DeleteGateConditionCommand command, CancellationToken ct = default)
    {
        Domain.GateCondition? gate = await db.GateConditions.FindAsync([command.GateConditionId], ct);
        if (gate is null || !await BelongsToProjectAsync(db, gate, command.ProjectId, ct))
        {
            return Domain.GateConditionErrors.NotFound;
        }

        db.GateConditions.Remove(gate);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    internal static Task<bool> BelongsToProjectAsync(ProjectsDbContext db, Domain.GateCondition gate, Guid projectId, CancellationToken ct) =>
        gate.ProjectStageId is { } stageId
            ? db.ProjectStages.AsNoTracking().AnyAsync(s => s.Id == stageId && s.ProjectId == projectId, ct)
            : db.ProjectTasks.AsNoTracking().AnyAsync(t => t.Id == gate.ProjectTaskId && t.ProjectId == projectId, ct);
}

/// <summary>Manual satisfaction (the <c>ManualCheckHandler</c> path) — approval-type gates are satisfied
/// reactively by <c>ApprovalDecidedIntegrationEventHandler</c>, never through this command.</summary>
internal sealed class SatisfyGateConditionCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<SatisfyGateConditionCommand>
{
    public async Task<Result> HandleAsync(SatisfyGateConditionCommand command, CancellationToken ct = default)
    {
        Domain.GateCondition? gate = await db.GateConditions.FindAsync([command.GateConditionId], ct);
        if (gate is null || !await DeleteGateConditionCommandHandler.BelongsToProjectAsync(db, gate, command.ProjectId, ct))
        {
            return Domain.GateConditionErrors.NotFound;
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
            ProjectsEnumMapping.ToContracts(g.Status), g.ApprovalId);
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
