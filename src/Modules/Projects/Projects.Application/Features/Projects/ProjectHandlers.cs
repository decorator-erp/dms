using System.Linq.Expressions;
using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Persistence;
using Crm.Contracts;
using Documents.Contracts;
using Finance.Contracts;
using FluentValidation;
using Hr.Contracts;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Projects.Application.Features.Stages;
using Projects.Contracts;
using Projects.Contracts.Grants;
using Projects.Contracts.Projects;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using Projects.Infrastructure.Services;
using SharedKernel;
using Sites.Contracts;
using Domain = Projects.Domain;
using ProjectMemberAccess = Projects.Contracts.ProjectMemberAccess;

namespace Projects.Application.Features.Projects;

internal sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);

        // P7: the column lengths and the enum, so an oversized or unknown value is a 400, not a 500.
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.WorkflowType).IsInEnum();
    }
}

/// <summary>P7: a null field keeps its value; a given one has the same limits as create.</summary>
internal sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500).When(x => x.Address is not null);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

/// <summary>P7: an unknown status is a 400.</summary>
internal sealed class ChangeProjectStatusCommandValidator : AbstractValidator<ChangeProjectStatusCommand>
{
    public ChangeProjectStatusCommandValidator() => RuleFor(x => x.Status).IsInEnum();
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
        Guid? ownerId = currentUser.UserId is null ? null : await hrDirectory.ResolveEmployeeIdAsync(currentUser.UserId, ct);
        if (ownerId is null)
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.AccessDenied);
        }

        // C-5 (LC-M5): a deactivated client gets no new project — it reads as not found, like an unknown one.
        IReadOnlyList<Guid> existingClientIds = await clientDirectory.ExistActiveAsync([command.ClientId], ct);
        if (existingClientIds.Count == 0)
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.ClientNotFound);
        }

        // P8: the sequential case; a create racing past this trips IX_projects_Code, mapped by ProjectWrites.
        if (await db.Projects.AsNoTracking().AnyAsync(p => p.Code == command.Code, ct))
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.CodeTaken);
        }

        var project = Domain.Project.Create(
            command.Code, command.Name, command.Address, ProjectsEnumMapping.ToDomain(command.WorkflowType),
            command.ClientId, ownerId.Value, command.Description, command.Notes,
            ProjectDates.ToUtc(command.EstimatedStartDate), ProjectDates.ToUtc(command.EstimatedEndDate));
        db.Projects.Add(project);

        if (command.StageTemplateId is { } stageTemplateId)
        {
            Result instantiated = await InstantiateAsync(project, stageTemplateId, ct);
            if (instantiated.IsFailure)
            {
                return Result.Failure<Guid>(instantiated.Error);
            }
        }

        Result saved = await ProjectWrites.SaveAsync(db, ct);
        return saved.IsSuccess ? project.Id : Result.Failure<Guid>(saved.Error);
    }

    /// <summary>
    /// Materializes every stage of the template, with the template's own names, order and descriptions and
    /// no cost, through <see cref="StageInstantiator"/> — which stages nothing if a department head is
    /// missing (decision #7: hard-fail, no partial materialization).
    /// </summary>
    private async Task<Result> InstantiateAsync(Domain.Project project, Guid stageTemplateId, CancellationToken ct)
    {
        bool templateExists = await db.StageTemplates.AsNoTracking().AnyAsync(t => t.Id == stageTemplateId, ct);
        if (!templateExists)
        {
            return Domain.StageTemplateErrors.NotFound;
        }

        List<StagePick> picks = await db.Stages.AsNoTracking()
            .Where(s => s.StageTemplateId == stageTemplateId)
            .OrderBy(s => s.Order)
            .Select(s => new StagePick(s.Id, s.Name, s.Description, s.Order, null))
            .ToListAsync(ct);
        if (picks.Count == 0)
        {
            return Domain.StageTemplateErrors.StagesNotFound;
        }

        Result<IReadOnlyList<Guid>> instantiated = await new StageInstantiator(db, hrDirectory).InstantiateAsync(project, picks, ct);
        return instantiated.IsFailure ? instantiated.Error : Result.Success();
    }
}

/// <summary>E8: the paging caps every list in this work uses.</summary>
internal sealed class GetProjectsQueryValidator : AbstractValidator<GetProjectsQuery>
{
    public GetProjectsQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.NameFilter).MaximumLength(200); // P7
    }
}

internal sealed class UpdateProjectCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateProjectCommand>
{
    public async Task<Result> HandleAsync(UpdateProjectCommand command, CancellationToken ct = default)
    {
        Project? project = await db.Projects.FindAsync([command.ProjectId], ct);
        if (project is null)
        {
            return Domain.ProjectErrors.NotFound;
        }

        project.Update(
            command.Name, command.Description, command.Address, command.Notes,
            ProjectDates.ToUtc(command.EstimatedStartDate), ProjectDates.ToUtc(command.EstimatedEndDate));
        return await ProjectWrites.SaveAsync(db, ct);
    }
}

internal sealed class ChangeProjectStatusCommandHandler(ProjectsDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ChangeProjectStatusCommand>
{
    public Task<Result> HandleAsync(ChangeProjectStatusCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => ChangeAsync(command, ct), ct);

    /// <summary>Under the project lock, so a stage added by hand can't slip in beside the open-stage check.</summary>
    private async Task<Result> ChangeAsync(ChangeProjectStatusCommand command, CancellationToken ct)
    {
        Project? project = await db.Projects.FindAsync([command.ProjectId], ct);
        if (project is null)
        {
            return Domain.ProjectErrors.NotFound;
        }

        // NP3: a project is Completed only once none of its stages is still Active or Locked, and (R1-11) none of its
        // stage-less (FreeStyle) tasks is still open.
        if (ProjectsEnumMapping.ToDomain(command.Status) == Domain.ProjectStatus.Completed && project.Status != Domain.ProjectStatus.Completed)
        {
            if (await db.ProjectStages.AnyAsync(
                    s => s.ProjectId == project.Id && (s.Status == Domain.StageStatus.Active || s.Status == Domain.StageStatus.Locked), ct))
            {
                return Domain.ProjectErrors.StagesOpen;
            }

            if (await db.ProjectTasks.AnyAsync(
                    t => t.ProjectId == project.Id && t.ProjectStageId == null
                        && t.Status != Domain.ProjectTaskStatus.Completed && t.Status != Domain.ProjectTaskStatus.Cancelled, ct))
            {
                return Domain.ProjectErrors.TasksOpen;
            }
        }

        Result result = project.ChangeStatus(ProjectsEnumMapping.ToDomain(command.Status), timeProvider.GetUtcNow().UtcDateTime);
        if (result.IsFailure)
        {
            return result;
        }

        return await ProjectWrites.SaveAsync(db, ct);
    }
}

internal sealed class SetProjectActiveCommandHandler(ProjectsDbContext db) : ICommandHandler<SetProjectActiveCommand>
{
    public async Task<Result> HandleAsync(SetProjectActiveCommand command, CancellationToken ct = default)
    {
        Project? project = await db.Projects.FindAsync([command.ProjectId], ct);
        if (project is null)
        {
            return Domain.ProjectErrors.NotFound;
        }

        if (command.IsActive)
        {
            project.Activate();
        }
        else
        {
            project.Deactivate();
        }

        return await ProjectWrites.SaveAsync(db, ct);
    }
}

/// <summary>
/// P1: a guarded hard delete, under the project lock (<see cref="ProjectWriteLock.RunAsync(ProjectsDbContext, Guid, Func{Task{Result}}, CancellationToken)"/>).
/// Allowed only while nothing has happened on the project and nothing points at it
/// (<see cref="WorkDeletionGuard.CanDeleteProjectAsync"/>: every stage and task passes the stage/task delete rules,
/// no pending approval gate, no document, no site — soft-deleted ones included — and no CRM or Finance row);
/// otherwise 409 <c>projects.NotDeletable</c>. The gates, sub-tasks, tasks, stage teams and stages go in the same
/// transaction (members and the scope of work by their cascading foreign keys); an FK violation from a row a
/// contract could not see yet is the same 409.
/// </summary>
internal sealed class DeleteProjectCommandHandler(
    ProjectsDbContext db, IDocumentQueries documents, ICrmProjectWorkUsage crm, IFinanceProjectWorkUsage finance,
    ISiteProjectUsage sites)
    : ICommandHandler<DeleteProjectCommand>
{
    public Task<Result> HandleAsync(DeleteProjectCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => DeleteAsync(command, ct), ct);

    private async Task<Result> DeleteAsync(DeleteProjectCommand command, CancellationToken ct)
    {
        Project? project = await db.Projects.FirstOrDefaultAsync(p => p.Id == command.ProjectId, ct);
        if (project is null)
        {
            return Domain.ProjectErrors.NotFound;
        }

        var guard = new WorkDeletionGuard(db, documents, crm, finance);
        await guard.LockProjectWorkAsync(project.Id, ct);
        List<Guid> stageIds = await db.ProjectStages.AsNoTracking()
            .Where(s => s.ProjectId == project.Id).Select(s => s.Id).ToListAsync(ct);
        List<Guid> taskIds = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.ProjectId == project.Id).Select(t => t.Id).ToListAsync(ct);
        if (!await guard.CanDeleteProjectAsync(project.Id, stageIds, taskIds, sites, ct))
        {
            return Domain.ProjectErrors.NotDeletable;
        }

        await guard.RemoveTasksAsync(taskIds, ct);
        db.GateConditions.RemoveRange(await db.GateConditions
            .Where(g => g.ProjectStageId != null && stageIds.Contains(g.ProjectStageId.Value)).ToListAsync(ct));
        db.StageTeams.RemoveRange(await db.StageTeams.Where(t => stageIds.Contains(t.ProjectStageId)).ToListAsync(ct));
        db.ProjectStages.RemoveRange(await db.ProjectStages.Where(s => s.ProjectId == project.Id).ToListAsync(ct));
        db.Projects.Remove(project);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Domain.ProjectErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (ProjectWriteLock.IsForeignKeyViolation(ex))
        {
            return Domain.ProjectErrors.NotDeletable;
        }

        return Result.Success();
    }
}

/// <summary>L1: <see cref="ProjectResponse.MyLevel"/> comes from <see cref="IProjectAccessLevelReader.GetLevelAsync"/> for the
/// query's own <see cref="GetProjectQuery.UserId"/>, the reader the scope decorator enforces with. No level is 404,
/// never a default level. P1-A: the level and <see cref="ProjectResponse.MyAccess"/> come from
/// <see cref="ProjectAccessContext.GetAccessAsync"/>, which also counts an active grant (membership wins over it).</summary>
internal sealed class GetProjectQueryHandler(
    ProjectsDbContext db, ProjectAccessContext access, IClientDirectory clientDirectory, IUserDirectory userDirectory,
    IHrDirectory hrDirectory)
    : IQueryHandler<GetProjectQuery, ProjectResponse>
{
    public async Task<Result<ProjectResponse>> HandleAsync(GetProjectQuery query, CancellationToken ct = default)
    {
        IReadOnlyDictionary<Guid, ProjectAccessResponse> accesses = await access.GetAccessAsync([query.ProjectId], query.UserId, ct);
        if (!accesses.TryGetValue(query.ProjectId, out ProjectAccessResponse? myAccess))
        {
            return Result.Failure<ProjectResponse>(Domain.ProjectErrors.NotFound);
        }

        ProjectRow? row = await db.Projects.AsNoTracking()
            .Where(p => p.Id == query.ProjectId)
            .Select(ToRow(db.ProjectStages, db.ProjectTasks))
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return Result.Failure<ProjectResponse>(Domain.ProjectErrors.NotFound);
        }

        ProjectRowNames names = await ProjectRowNames.ResolveAsync([row], clientDirectory, userDirectory, hrDirectory, ct);
        return row.ToResponse(myAccess, names);
    }

    /// <summary>The row projection. <paramref name="stages"/> and <paramref name="tasks"/> are the context's
    /// <c>ProjectStages</c> and <c>ProjectTasks</c>: the stage count and the progress sums are correlated subqueries
    /// of the same SQL statement (Project has no navigations), so a page of projects is still one query, never one
    /// count per project. P2/NP5: progress is computed here from the stages' counters plus the unstaged tasks
    /// (<see cref="ProjectProgress"/>), never read from a stored column.</summary>
    internal static Expression<Func<Domain.Project, ProjectRow>> ToRow(
        IQueryable<Domain.ProjectStage> stages, IQueryable<Domain.ProjectTask> tasks) => p =>
        new ProjectRow(
            p.Id, p.Code, p.Name, p.Description, ProjectsEnumMapping.ToContracts(p.Status),
            ProjectsEnumMapping.ToContracts(p.WorkflowType), p.Address, p.Notes,
            ProjectProgress.Percent(
                stages.Where(s => s.ProjectId == p.Id).Sum(s => s.CompletedTasks)
                    + tasks.Count(t => t.ProjectId == p.Id && t.ProjectStageId == null && t.Status == Domain.ProjectTaskStatus.Completed),
                stages.Where(s => s.ProjectId == p.Id).Sum(s => s.TotalTasks)
                    + tasks.Count(t => t.ProjectId == p.Id && t.ProjectStageId == null && t.Status != Domain.ProjectTaskStatus.Cancelled)),
            p.EstimatedStartDate, p.EstimatedEndDate, p.ActualStartDate, p.ActualEndDate,
            p.ClientId, p.OwnerId, p.IsActive, stages.Count(s => s.ProjectId == p.Id));
}

/// <summary>
/// Not <see cref="IProjectScoped"/> — it has no single target project, so <c>ProjectScopeDecorator</c>
/// cannot wrap it. Access is filtered inline instead: <see cref="ProjectsPermissions.Projects.ReadAll"/>
/// bypasses filtering entirely; otherwise the guard's own accessible-ids query (owned projects plus
/// stage-team membership, D-14) is reused here (FINDING-P1-1) rather than added to
/// <see cref="IProjectAccessGuard"/>'s public surface only for one list query — that guard's own
/// batch method requires a candidate id set up front, which a paged list doesn't have until after
/// filtering.
///
/// <para>L1/L2: each row's <see cref="ProjectResponse.MyLevel"/> comes from one
/// <see cref="IProjectAccessLevelReader.GetLevelsAsync"/> call for the page's ids, never one call per row. C3: a row
/// the reader gives no level is dropped, never shown with a default level, and logged as a warning — the filter
/// above and the reader share their sources, so it should never happen.</para>
///
/// <para>P1-A: the filter and the levels come from <see cref="ProjectAccessContext"/>, so a project the caller has an
/// active grant on (any area) is listed, with <c>MyAccess.Source</c> "Grant" and the grant's level. A grant never
/// makes a project "mine".</para>
/// </summary>
internal sealed class GetProjectsQueryHandler(
    ProjectsDbContext db, IHrDirectory hrDirectory, ProjectAccessContext access, IClientDirectory clientDirectory,
    IUserDirectory userDirectory, ILogger<GetProjectsQueryHandler> logger)
    : IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>
{
    public async Task<Result<PagedResult<ProjectResponse>>> HandleAsync(GetProjectsQuery query, CancellationToken ct = default)
    {
        IQueryable<Project> projects = db.Projects.AsNoTracking().AsQueryable();

        if (!access.ReadAll)
        {
            Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(query.UserId, ct);
            if (employeeId is null)
            {
                return PagedResult<ProjectResponse>.Empty(query.PageNumber, query.PageSize);
            }

            IQueryable<Guid> accessibleProjectIds = ProjectAccessGuard.AccessibleProjectIds(access, employeeId.Value);

            projects = projects.Where(p => accessibleProjectIds.Contains(p.Id));
        }

        if (query.Mine)
        {
            Guid? me = await hrDirectory.ResolveEmployeeIdAsync(query.UserId, ct);
            if (me is null)
            {
                return PagedResult<ProjectResponse>.Empty(query.PageNumber, query.PageSize);
            }

            IQueryable<Guid> myProjectIds = ProjectAccessGuard.MyProjectIds(db, me.Value);
            projects = projects.Where(p => myProjectIds.Contains(p.Id));
        }

        if (query.ClientId is { } clientId)
        {
            projects = projects.Where(p => p.ClientId == clientId);
        }

        if (Like.Contains(query.Search) is { } pattern)
        {
            projects = projects.Where(p =>
                EF.Functions.ILike(p.Name, pattern, Like.EscapeCharacter) || EF.Functions.ILike(p.Code, pattern, Like.EscapeCharacter));
        }

        if (!string.IsNullOrWhiteSpace(query.NameFilter))
        {
            projects = projects.Where(p => p.Name.Contains(query.NameFilter));
        }

        int totalCount = await projects.CountAsync(ct);
        List<ProjectRow> rows = await projects
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id) // P13: a total order, so pages neither repeat nor skip a project.
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(GetProjectQueryHandler.ToRow(db.ProjectStages, db.ProjectTasks))
            .ToListAsync(ct);

        IReadOnlyDictionary<Guid, ProjectAccessResponse> accesses =
            await access.GetAccessAsync([.. rows.Select(r => r.Id)], query.UserId, ct);
        ProjectRowNames names = await ProjectRowNames.ResolveAsync(rows, clientDirectory, userDirectory, hrDirectory, ct);
        var items = new List<ProjectResponse>(rows.Count);
        foreach (ProjectRow row in rows)
        {
            if (accesses.TryGetValue(row.Id, out ProjectAccessResponse? myAccess))
            {
                items.Add(row.ToResponse(myAccess, names));
            }
            else
            {
                logger.LogWarning("Project {ProjectId} was in the list but the level reader gave no level; the row was dropped.", row.Id);
            }
        }

        return new PagedResult<ProjectResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
