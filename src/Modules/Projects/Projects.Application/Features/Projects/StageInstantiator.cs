using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Projects;

/// <summary>One picked template stage to materialize: <see cref="StageId"/> is the template <c>Stage</c>
/// whose tasks/subtasks/departments are copied; the name, description, order and cost are the caller's, so
/// a template instantiation passes the template's own values and a scope of work passes its edited
/// item.</summary>
internal sealed record StagePick(Guid StageId, string Name, string? Description, int Order, decimal? Cost);

/// <summary>
/// Copies picked template stages' tasks/subtasks/departments into runtime
/// <c>ProjectStage</c>/<c>ProjectTask</c>/<c>ProjectSubTask</c>/<c>StageTeam</c> rows, all staged (not yet
/// saved) onto the caller's own <c>SaveChangesAsync</c>. Department heads for every active
/// <c>StageDepartment</c> referenced are resolved BEFORE any row is staged — if any is missing, the whole
/// operation fails and nothing is added to the tracker (decision #7: hard-fail, no partial
/// materialization). A failure AFTER staging has begun — a task the project refuses, such as
/// <c>TaskErrors.StageNotAllowed</c> — returns with the earlier rows still staged in the tracker; they are
/// never written, because every caller returns that failure without reaching its save (D-4). Shared by <c>CreateProjectCommandHandler</c> (a whole template) and the scope-of-work
/// confirm (any subset of stages from any templates). Constructed by its callers rather than resolved from
/// DI: <c>Projects.Infrastructure</c> registers services but can't name this internal type.
/// </summary>
internal sealed class StageInstantiator(ProjectsDbContext db, IHrDirectory hrDirectory)
{
    /// <summary>Returns the created <c>ProjectStage</c> ids, in <paramref name="picks"/>' order.</summary>
    public async Task<Result<IReadOnlyList<Guid>>> InstantiateAsync(
        Domain.Project project, IReadOnlyList<StagePick> picks, CancellationToken ct)
    {
        var stageIds = picks.Select(p => p.StageId).ToList();
        List<StageTask> stageTasks = await db.StageTasks.AsNoTracking().Where(t => stageIds.Contains(t.StageId)).ToListAsync(ct);
        var taskIds = stageTasks.Select(t => t.Id).ToList();
        List<StageSubTask> stageSubTasks = taskIds.Count == 0
            ? []
            : await db.StageSubTasks.AsNoTracking().Where(st => taskIds.Contains(st.StageTaskId)).ToListAsync(ct);
        List<StageDepartment> stageDepartments = await db.StageDepartments.AsNoTracking()
            .Where(sd => stageIds.Contains(sd.StageId) && sd.IsActive)
            .ToListAsync(ct);

        var departmentIds = stageDepartments.Select(sd => sd.DepartmentId).Distinct().ToList();
        IReadOnlyList<DepartmentHeadSummary> heads = departmentIds.Count == 0 ? [] : await hrDirectory.GetDepartmentHeadsAsync(departmentIds, ct);
        var headsByDepartment = heads.ToDictionary(h => h.DepartmentId, h => h.HeadEmployeeId);
        foreach (Guid departmentId in departmentIds)
        {
            if (headsByDepartment.GetValueOrDefault(departmentId) is null)
            {
                return Result.Failure<IReadOnlyList<Guid>>(Domain.TeamErrors.MissingHead);
            }
        }

        var created = new List<Guid>(picks.Count);
        foreach (StagePick pick in picks)
        {
            var tasksForStage = stageTasks.Where(t => t.StageId == pick.StageId).ToList();
            var projectStage = Domain.ProjectStage.Create(
                pick.Name, pick.Order, project.Id, tasksForStage.Count, pick.StageId, pick.Description, cost: pick.Cost);
            db.ProjectStages.Add(projectStage);
            created.Add(projectStage.Id);

            foreach (StageTask? task in tasksForStage)
            {
                var subTasksForTask = stageSubTasks.Where(st => st.StageTaskId == task.Id).ToList();
                Result<ProjectTask> taskResult = Domain.ProjectTask.Create(
                    project.Id, task.Name, task.Order, project.WorkflowType, task.Weight,
                    projectStage.Id, task.Id, Domain.TaskPriority.Medium, Domain.ProjectTaskStatus.Open,
                    task.Description, subTasksForTask.Count(st => st.IsRequired), task.DepartmentId); // P14: required only
                if (taskResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<Guid>>(taskResult.Error);
                }

                db.ProjectTasks.Add(taskResult.Value!);

                foreach (StageSubTask? subTask in subTasksForTask.OrderBy(st => st.Order))
                {
                    db.ProjectSubTasks.Add(Domain.ProjectSubTask.Create(
                        taskResult.Value!.Id, subTask.Title, subTask.Order, subTask.IsRequired, subTask.Description));
                }
            }

            foreach (Guid departmentId in stageDepartments.Where(sd => sd.StageId == pick.StageId).Select(sd => sd.DepartmentId))
            {
                Guid headId = headsByDepartment[departmentId]!.Value;
                db.StageTeams.Add(Domain.StageTeam.CreateProjectDepartmentTeam(projectStage.Id, departmentId, headId));
            }
        }

        return created;
    }
}
