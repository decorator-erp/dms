using BuildingBlocks.Messaging;
using Crm.Contracts;
using Documents.Contracts;
using Finance.Contracts;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Application.Features.Projects;
using Projects.Contracts.Stages;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Stages;

internal sealed class CreateProjectStageCommandValidator : AbstractValidator<CreateProjectStageCommand>
{
    public CreateProjectStageCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Order).GreaterThan(0);
        RuleFor(x => x.EstimatedEndDate).GreaterThanOrEqualTo(x => x.EstimatedStartDate)
            .When(x => x.EstimatedStartDate is not null && x.EstimatedEndDate is not null);
    }
}

internal sealed class UpdateProjectStageCommandValidator : AbstractValidator<UpdateProjectStageCommand>
{
    public UpdateProjectStageCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Order).GreaterThan(0).When(x => x.Order is not null);
        RuleFor(x => x.EstimatedEndDate).GreaterThanOrEqualTo(x => x.EstimatedStartDate)
            .When(x => x.EstimatedStartDate is not null && x.EstimatedEndDate is not null);
    }
}

/// <summary>Shared rules for E2's manual stage writes.</summary>
internal static class ManualStages
{
    /// <summary>D2: a Stage-workflow project whose stage structure is settled — its scope of work is
    /// confirmed or, with no scope of work at all (or only a Draft, P5), it was created from a template (it already
    /// has stages).
    /// A missing project is 404; an inactive, on-hold, completed or cancelled one is 409 (NP3, <see cref="ProjectLiveness"/>).</summary>
    public static async Task<Result<Domain.Project>> EnsureEditableAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct)
    {
        // NP3: an inactive, on-hold, completed or cancelled project takes no work writes (409).
        Result live = await ProjectLiveness.EnsureWritableAsync(db, projectId, ct);
        if (live.IsFailure)
        {
            return Result.Failure<Domain.Project>(live.Error);
        }

        Domain.Project project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
        if (project.WorkflowType != Domain.WorkflowType.Stage)
        {
            return Result.Failure<Domain.Project>(Domain.StageErrors.WorkflowNotSupported);
        }

        Domain.ScopeOfWorkStatus? scope = await db.ScopesOfWork.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .Select(s => (Domain.ScopeOfWorkStatus?)s.Status)
            .FirstOrDefaultAsync(ct);
        // P5: a Draft scope beside existing stages (a template-created project whose scope was touched before
        // FindOrCreateAsync refused that) can never be confirmed, so it counts as "no scope", not as unsettled.
        bool settled = scope == Domain.ScopeOfWorkStatus.Confirmed
            || await db.ProjectStages.AnyAsync(s => s.ProjectId == projectId, ct);

        return settled ? project : Result.Failure<Domain.Project>(Domain.StageErrors.ScopeNotConfirmed);
    }

    /// <summary>Puts <paramref name="moved"/> at <paramref name="requested"/> (clamped into 1..n) and renumbers
    /// every other stage 1..n around it, so the order stays contiguous.</summary>
    public static void Reorder(List<Domain.ProjectStage> stages, Domain.ProjectStage moved, int requested)
    {
        stages.Remove(moved);
        int index = Math.Clamp(requested, 1, stages.Count + 1) - 1;
        stages.Insert(index, moved);
        Renumber(stages);
    }

    public static void Renumber(List<Domain.ProjectStage> stages)
    {
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i].Order != i + 1)
            {
                stages[i].SetOrder(i + 1);
            }
        }
    }

    public static Task<List<Domain.ProjectStage>> LoadOrderedAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct) =>
        db.ProjectStages.Where(s => s.ProjectId == projectId).OrderBy(s => s.Order).ThenBy(s => s.CreatedAtUtc).ToListAsync(ct);
}

/// <summary>E2 (D2). Route claim → scope (404) → <see cref="ManualStages.EnsureEditableAsync"/> (409), under
/// the project lock. With a template stage, <see cref="StageInstantiator"/> materialises it (tasks,
/// sub-tasks and the department teams), so a manual stage gets its team the same way a confirmed stage
/// does (E7).</summary>
internal sealed class CreateProjectStageCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<CreateProjectStageCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(CreateProjectStageCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => CreateAsync(command, ct), ct);

    private async Task<Result<Guid>> CreateAsync(CreateProjectStageCommand command, CancellationToken ct)
    {
        // R1-3: the template stage's 404 before the project's 409s.
        if (command.TemplateStageId is { } pickedStageId && !await db.Stages.AsNoTracking().AnyAsync(s => s.Id == pickedStageId, ct))
        {
            return Result.Failure<Guid>(Domain.TemplateEntityErrors.StageNotFound);
        }

        Result<Domain.Project> editable = await ManualStages.EnsureEditableAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return Result.Failure<Guid>(editable.Error);
        }

        List<Domain.ProjectStage> stages = await ManualStages.LoadOrderedAsync(db, command.ProjectId, ct);

        Domain.ProjectStage stage;
        if (command.TemplateStageId is { } templateStageId)
        {
            Result<IReadOnlyList<Guid>> created = await new StageInstantiator(db, hrDirectory).InstantiateAsync(
                editable.Value!, [new StagePick(templateStageId, command.Name.Trim(), command.Description, stages.Count + 1, null)], ct);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            stage = db.ProjectStages.Local.Single(s => s.Id == created.Value![0]);
            stage.Update(stage.Name, stage.Description, command.Notes,
                ProjectDates.ToUtc(command.EstimatedStartDate), ProjectDates.ToUtc(command.EstimatedEndDate));
        }
        else
        {
            stage = Domain.ProjectStage.Create(
                command.Name.Trim(), stages.Count + 1, command.ProjectId, description: command.Description, notes: command.Notes,
                estimatedStartDate: ProjectDates.ToUtc(command.EstimatedStartDate),
                estimatedEndDate: ProjectDates.ToUtc(command.EstimatedEndDate));
            db.ProjectStages.Add(stage);
        }

        stages.Add(stage);
        ManualStages.Reorder(stages, stage, command.Order);
        await db.SaveChangesAsync(ct);
        return stage.Id;
    }
}

internal sealed class UpdateProjectStageCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateProjectStageCommand>
{
    public Task<Result> HandleAsync(UpdateProjectStageCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => UpdateAsync(command, ct), ct);

    private async Task<Result> UpdateAsync(UpdateProjectStageCommand command, CancellationToken ct)
    {
        // R1-3: the stage's 404 before the project's 409s.
        if (!await db.ProjectStages.AsNoTracking().AnyAsync(s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct))
        {
            return Domain.StageErrors.NotFound;
        }

        Result<Domain.Project> editable = await ManualStages.EnsureEditableAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        List<Domain.ProjectStage> stages = await ManualStages.LoadOrderedAsync(db, command.ProjectId, ct);
        Domain.ProjectStage? stage = stages.FirstOrDefault(s => s.Id == command.StageId);
        if (stage is null)
        {
            return Domain.StageErrors.NotFound;
        }

        stage.Update(command.Name.Trim(), command.Description, command.Notes,
            ProjectDates.ToUtc(command.EstimatedStartDate), ProjectDates.ToUtc(command.EstimatedEndDate));
        if (command.Order is { } order)
        {
            ManualStages.Reorder(stages, stage, order);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>E2: hard delete, only before the stage has started and only when nothing points at it
/// (<see cref="WorkDeletionGuard"/>); otherwise 409 <c>projects.stages.NotDeletable</c>. The stage goes with
/// its never-started tasks, their sub-tasks, its pending gates and its teams; the rest close up.</summary>
internal sealed class DeleteProjectStageCommandHandler(
    ProjectsDbContext db, IDocumentQueries documents, ICrmProjectWorkUsage crm, IFinanceProjectWorkUsage finance)
    : ICommandHandler<DeleteProjectStageCommand>
{
    public Task<Result> HandleAsync(DeleteProjectStageCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => DeleteAsync(command, ct), ct);

    private async Task<Result> DeleteAsync(DeleteProjectStageCommand command, CancellationToken ct)
    {
        // R1-3: the stage's 404 before the project's 409s.
        if (!await db.ProjectStages.AsNoTracking().AnyAsync(s => s.Id == command.StageId && s.ProjectId == command.ProjectId, ct))
        {
            return Domain.StageErrors.NotFound;
        }

        Result<Domain.Project> editable = await ManualStages.EnsureEditableAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        List<Domain.ProjectStage> stages = await ManualStages.LoadOrderedAsync(db, command.ProjectId, ct);
        Domain.ProjectStage? stage = stages.FirstOrDefault(s => s.Id == command.StageId);
        if (stage is null)
        {
            return Domain.StageErrors.NotFound;
        }

        // FINDING-E-4: a completed or rejected stage has run its course and stays.
        if (stage.Status is not (Domain.StageStatus.Active or Domain.StageStatus.Locked))
        {
            return Domain.StageErrors.NotDeletable;
        }

        List<Guid> taskIds = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.ProjectStageId == stage.Id).Select(t => t.Id).ToListAsync(ct);
        var guard = new WorkDeletionGuard(db, documents, crm, finance);
        await guard.LockAsync(stage.Id, taskIds, ct);
        if (!await guard.CanDeleteStageAsync(stage.Id, taskIds, ct))
        {
            return Domain.StageErrors.NotDeletable;
        }

        await guard.RemoveTasksAsync(taskIds, ct);
        db.GateConditions.RemoveRange(await db.GateConditions.Where(g => g.ProjectStageId == stage.Id).ToListAsync(ct));
        db.StageTeams.RemoveRange(await db.StageTeams.Where(t => t.ProjectStageId == stage.Id).ToListAsync(ct));

        // P12: the scope item that created this stage forgets it in the same save.
        Domain.ScopeOfWork? scope = await db.ScopesOfWork.Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.ProjectId == command.ProjectId, ct);
        scope?.ForgetCreatedStage(stage.Id);

        db.ProjectStages.Remove(stage);
        stages.Remove(stage);
        ManualStages.Renumber(stages);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProjectWriteLock.IsForeignKeyViolation(ex))
        {
            return Domain.StageErrors.NotDeletable;
        }

        return Result.Success();
    }
}
