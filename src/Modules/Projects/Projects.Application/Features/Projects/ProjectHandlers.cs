using System.Linq.Expressions;
using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Projects;

internal sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty();
    }
}

/// <summary>
/// <c>OwnerId</c> is resolved server-side from <see cref="ICurrentUser"/> via
/// <see cref="IHrDirectory.ResolveEmployeeIdAsync"/> — <see cref="CreateProjectCommand"/> carries no
/// user field at all, so there's nothing to spoof. Instantiation (per decision #7) materializes the
/// stage/task/subtask/team skeleton only — no gate auto-wiring, since there's no template-level
/// condition definition left to wire from once <c>StageConditionType</c> was dropped as dead code
/// (G5-03).
///
/// <para>G9-13: <c>command.ClientId</c> is validated against <c>crm.clients</c> via
/// <see cref="IClientDirectory.ExistAsync"/> before <c>Project.Create</c> is called — closing the
/// Phase 2 deferral that left this field an unvalidated Guid. Validation lives here, in the handler,
/// never in <c>Project.Create</c> itself (the plan's explicit note) — the aggregate has no way to
/// reach a cross-module contract.</para>
/// </summary>
internal sealed class CreateProjectCommandHandler(
    ProjectsDbContext db, IHrDirectory hrDirectory, IClientDirectory clientDirectory, ICurrentUser currentUser)
    : ICommandHandler<CreateProjectCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateProjectCommand command, CancellationToken ct = default)
    {
        var ownerId = currentUser.UserId is null ? null : await hrDirectory.ResolveEmployeeIdAsync(currentUser.UserId, ct);
        if (ownerId is null)
            return Result.Failure<Guid>(Domain.ProjectErrors.AccessDenied);

        var existingClientIds = await clientDirectory.ExistAsync([command.ClientId], ct);
        if (existingClientIds.Count == 0)
            return Result.Failure<Guid>(Domain.ProjectErrors.ClientNotFound);

        var project = Domain.Project.Create(
            command.Code, command.Name, command.Address, ProjectsEnumMapping.ToDomain(command.WorkflowType),
            command.ClientId, ownerId.Value, command.Description, command.Notes,
            command.EstimatedStartDate, command.EstimatedEndDate);
        db.Projects.Add(project);

        if (command.StageTemplateId is { } stageTemplateId)
        {
            var instantiated = await InstantiateAsync(project, stageTemplateId, ct);
            if (instantiated.IsFailure)
                return Result.Failure<Guid>(instantiated.Error);
        }

        await db.SaveChangesAsync(ct);
        return project.Id;
    }

    /// <summary>
    /// Copies a <c>StageTemplate</c>'s stages/tasks/subtasks/departments into runtime
    /// <c>ProjectStage</c>/<c>ProjectTask</c>/<c>ProjectSubTask</c>/<c>StageTeam</c> rows, all staged
    /// (not yet saved) onto <paramref name="project"/>'s own <c>SaveChangesAsync</c> call. Department
    /// heads for every active <c>StageDepartment</c> referenced are resolved BEFORE any row is staged —
    /// if any is missing, the whole operation fails and nothing from this method is added to the
    /// tracker (decision #7: hard-fail, no partial materialization).
    /// </summary>
    private async Task<Result> InstantiateAsync(Domain.Project project, Guid stageTemplateId, CancellationToken ct)
    {
        var templateExists = await db.StageTemplates.AsNoTracking().AnyAsync(t => t.Id == stageTemplateId, ct);
        if (!templateExists)
            return Domain.StageTemplateErrors.NotFound;

        var stages = await db.Stages.AsNoTracking()
            .Where(s => s.StageTemplateId == stageTemplateId)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);
        if (stages.Count == 0)
            return Domain.StageTemplateErrors.StagesNotFound;

        var stageIds = stages.Select(s => s.Id).ToList();
        var stageTasks = await db.StageTasks.AsNoTracking().Where(t => stageIds.Contains(t.StageId)).ToListAsync(ct);
        var taskIds = stageTasks.Select(t => t.Id).ToList();
        var stageSubTasks = taskIds.Count == 0
            ? []
            : await db.StageSubTasks.AsNoTracking().Where(st => taskIds.Contains(st.StageTaskId)).ToListAsync(ct);
        var stageDepartments = await db.StageDepartments.AsNoTracking()
            .Where(sd => stageIds.Contains(sd.StageId) && sd.IsActive)
            .ToListAsync(ct);

        var departmentIds = stageDepartments.Select(sd => sd.DepartmentId).Distinct().ToList();
        var heads = departmentIds.Count == 0 ? [] : await hrDirectory.GetDepartmentHeadsAsync(departmentIds, ct);
        var headsByDepartment = heads.ToDictionary(h => h.DepartmentId, h => h.HeadEmployeeId);
        foreach (var departmentId in departmentIds)
        {
            if (headsByDepartment.GetValueOrDefault(departmentId) is null)
                return Domain.TeamErrors.MissingHead;
        }

        foreach (var stage in stages)
        {
            var tasksForStage = stageTasks.Where(t => t.StageId == stage.Id).ToList();
            var projectStage = Domain.ProjectStage.Create(
                stage.Name, stage.Order, project.Id, tasksForStage.Count, stage.Id, stage.Description);
            db.ProjectStages.Add(projectStage);

            foreach (var task in tasksForStage)
            {
                var subTasksForTask = stageSubTasks.Where(st => st.StageTaskId == task.Id).ToList();
                var taskResult = Domain.ProjectTask.Create(
                    project.Id, task.Name, task.Order, project.WorkflowType, task.Weight,
                    projectStage.Id, task.Id, Domain.TaskPriority.Medium, Domain.ProjectTaskStatus.Open,
                    task.Description, subTasksForTask.Count, task.DepartmentId);
                if (taskResult.IsFailure)
                    return taskResult.Error;

                db.ProjectTasks.Add(taskResult.Value!);

                foreach (var subTask in subTasksForTask.OrderBy(st => st.Order))
                {
                    db.ProjectSubTasks.Add(Domain.ProjectSubTask.Create(
                        taskResult.Value!.Id, subTask.Title, subTask.Order, subTask.IsRequired, subTask.Description));
                }
            }

            foreach (var departmentId in stageDepartments.Where(sd => sd.StageId == stage.Id).Select(sd => sd.DepartmentId))
            {
                var headId = headsByDepartment[departmentId]!.Value;
                db.StageTeams.Add(Domain.StageTeam.CreateProjectDepartmentTeam(projectStage.Id, departmentId, headId));
            }
        }

        return Result.Success();
    }
}

internal sealed class UpdateProjectCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateProjectCommand>
{
    public async Task<Result> HandleAsync(UpdateProjectCommand command, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([command.ProjectId], ct);
        if (project is null)
            return Domain.ProjectErrors.NotFound;

        project.Update(
            command.Name, command.Description, command.Address, command.Notes,
            command.EstimatedStartDate, command.EstimatedEndDate);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class ChangeProjectStatusCommandHandler(ProjectsDbContext db) : ICommandHandler<ChangeProjectStatusCommand>
{
    public async Task<Result> HandleAsync(ChangeProjectStatusCommand command, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([command.ProjectId], ct);
        if (project is null)
            return Domain.ProjectErrors.NotFound;

        var result = project.ChangeStatus(ProjectsEnumMapping.ToDomain(command.Status));
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class SetProjectActiveCommandHandler(ProjectsDbContext db) : ICommandHandler<SetProjectActiveCommand>
{
    public async Task<Result> HandleAsync(SetProjectActiveCommand command, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([command.ProjectId], ct);
        if (project is null)
            return Domain.ProjectErrors.NotFound;

        if (command.IsActive)
            project.Activate();
        else
            project.Deactivate();

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeleteProjectCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteProjectCommand>
{
    public async Task<Result> HandleAsync(DeleteProjectCommand command, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([command.ProjectId], ct);
        if (project is null)
            return Domain.ProjectErrors.NotFound;

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetProjectQueryHandler(ProjectsDbContext db) : IQueryHandler<GetProjectQuery, ProjectResponse>
{
    public async Task<Result<ProjectResponse>> HandleAsync(GetProjectQuery query, CancellationToken ct = default)
    {
        var response = await db.Projects.AsNoTracking()
            .Where(p => p.Id == query.ProjectId)
            .Select(ToResponse())
            .FirstOrDefaultAsync(ct);

        return response is null ? Result.Failure<ProjectResponse>(Domain.ProjectErrors.NotFound) : response;
    }

    internal static Expression<Func<Domain.Project, ProjectResponse>> ToResponse() => p =>
        new ProjectResponse(
            p.Id, p.Code, p.Name, p.Description, ProjectsEnumMapping.ToContracts(p.Status),
            ProjectsEnumMapping.ToContracts(p.WorkflowType), p.Address, p.Notes, p.ProgressPercentage,
            p.EstimatedStartDate, p.EstimatedEndDate, p.ActualStartDate, p.ActualEndDate,
            p.ClientId, p.OwnerId, p.IsActive);
}

/// <summary>
/// Not <see cref="IProjectScoped"/> — it has no single target project, so <c>ProjectScopeDecorator</c>
/// cannot wrap it. Access is filtered inline instead: <see cref="ProjectsPermissions.Projects.ReadAll"/>
/// bypasses filtering entirely; otherwise the same stage-team-membership join
/// <c>ProjectAccessGuard</c> itself runs (G5-05) is inlined here rather than added to
/// <see cref="IProjectAccessGuard"/>'s public surface only for one list query — that guard's own
/// batch method requires a candidate id set up front, which a paged list doesn't have until after
/// filtering.
/// </summary>
internal sealed class GetProjectsQueryHandler(ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser)
    : IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>
{
    public async Task<Result<PagedResult<ProjectResponse>>> HandleAsync(GetProjectsQuery query, CancellationToken ct = default)
    {
        var projects = db.Projects.AsNoTracking().AsQueryable();

        if (!currentUser.HasPermission(ProjectsPermissions.Projects.ReadAll))
        {
            var employeeId = await hrDirectory.ResolveEmployeeIdAsync(query.UserId, ct);
            if (employeeId is null)
                return PagedResult<ProjectResponse>.Empty(query.PageNumber, query.PageSize);

            var accessibleProjectIds = db.StageTeamMembers.AsNoTracking()
                .Where(m => m.EmployeeId == employeeId.Value)
                .Join(db.StageTeams, m => m.StageTeamId, t => t.Id, (m, t) => t)
                .Join(db.ProjectStages, t => t.ProjectStageId, s => s.Id, (t, s) => s.ProjectId)
                .Distinct();

            projects = projects.Where(p => accessibleProjectIds.Contains(p.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.NameFilter))
            projects = projects.Where(p => p.Name.Contains(query.NameFilter));

        var totalCount = await projects.CountAsync(ct);
        var items = await projects
            .OrderBy(p => p.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(GetProjectQueryHandler.ToResponse())
            .ToListAsync(ct);

        return new PagedResult<ProjectResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
