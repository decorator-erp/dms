using BuildingBlocks.Messaging;
using Crm.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Application.Features.Tasks;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Tasks;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Projects;

/// <summary>
/// B2-be: the project page's one-shot read. <see cref="GetProjectDetailsQuery"/> is <see cref="IProjectScoped"/>,
/// so <c>ProjectScopeDecorator</c> has already turned an outsider into 404 before this runs. Like
/// <see cref="GetProjectQueryHandler"/> it does not filter on <c>IsActive</c>: an inactive project is returned.
///
/// <para><b>A fixed query set, never one per stage:</b> the project (its stage count a subquery of the same
/// statement), its stages, its tasks, its gates' owner and status, then the client's name through the existing
/// <see cref="IClientDirectory"/> (one CRM read plus one batched login read on CRM's side). Tasks, counts and
/// gate summaries are grouped per stage in memory.</para>
/// </summary>
internal sealed class GetProjectDetailsQueryHandler(ProjectsDbContext db, IClientDirectory clientDirectory)
    : IQueryHandler<GetProjectDetailsQuery, ProjectDetailsResponse>
{
    public async Task<Result<ProjectDetailsResponse>> HandleAsync(GetProjectDetailsQuery query, CancellationToken ct = default)
    {
        ProjectResponse? project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == query.ProjectId)
            .Select(GetProjectQueryHandler.ToResponse(db.ProjectStages))
            .FirstOrDefaultAsync(ct);
        if (project is null)
        {
            return Result.Failure<ProjectDetailsResponse>(Domain.ProjectErrors.NotFound);
        }

        var stages = await db.ProjectStages.AsNoTracking()
            .Where(s => s.ProjectId == query.ProjectId)
            .OrderBy(s => s.Order)
            .Select(s => new
            {
                s.Id,
                s.ProjectId,
                s.StageId,
                s.Name,
                s.Description,
                s.Order,
                s.Status,
                s.ProgressPercent,
                s.CompletedTasks,
                s.TotalTasks,
                s.Cost,
                s.EstimatedStartDate,
                s.EstimatedEndDate,
                s.ActualStartDate,
                s.ActualEndDate,
                s.Notes,
            })
            .ToListAsync(ct);

        List<ProjectTaskResponse> tasks = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.ProjectId == query.ProjectId)
            .OrderBy(t => t.Order)
            .Select(GetProjectTaskQueryHandler.ToResponse())
            .ToListAsync(ct);

        IQueryable<Guid> stageIds = db.ProjectStages.Where(s => s.ProjectId == query.ProjectId).Select(s => s.Id);
        IQueryable<Guid> taskIds = db.ProjectTasks.Where(t => t.ProjectId == query.ProjectId).Select(t => t.Id);
        var gates = await db.GateConditions.AsNoTracking()
            .Where(g => g.ProjectStageId != null && stageIds.Contains(g.ProjectStageId.Value)
                || g.ProjectTaskId != null && taskIds.Contains(g.ProjectTaskId.Value))
            .Select(g => new { g.ProjectStageId, g.ProjectTaskId, g.Status })
            .ToListAsync(ct);

        ClientDisplayName? client = await clientDirectory.GetDisplayNameAsync(project.ClientId, ct);

        var stageOfTask = tasks.ToDictionary(t => t.Id, t => t.ProjectStageId);
        ILookup<Guid, Domain.GateStatus> gateStatusesByStage = gates
            .Select(g => (StageId: g.ProjectStageId ?? (g.ProjectTaskId is { } tid ? stageOfTask.GetValueOrDefault(tid) : null), g.Status))
            .Where(g => g.StageId is not null)
            .ToLookup(g => g.StageId!.Value, g => g.Status);
        ILookup<Guid, ProjectTaskResponse> tasksByStage = tasks.Where(t => t.ProjectStageId is not null).ToLookup(t => t.ProjectStageId!.Value);

        List<ProjectStageDetailsResponse> stageResponses = [.. stages.Select(s =>
        {
            List<ProjectTaskResponse> stageTasks = [.. tasksByStage[s.Id]];
            return new ProjectStageDetailsResponse(
                s.Id, s.ProjectId, s.StageId, s.Name, s.Description, s.Order, ProjectsEnumMapping.ToContracts(s.Status),
                s.ProgressPercent, s.CompletedTasks, s.TotalTasks, s.Cost,
                s.EstimatedStartDate, s.EstimatedEndDate, s.ActualStartDate, s.ActualEndDate, s.Notes,
                stageTasks, CountByStatus(stageTasks), Summarize(gateStatusesByStage[s.Id]));
        })];

        var header = new ProjectDetailsProjectResponse(
            project.Id, project.Code, project.Name, project.Description, project.Status, project.WorkflowType,
            project.Address, project.Notes, project.ProgressPercentage,
            project.EstimatedStartDate, project.EstimatedEndDate, project.ActualStartDate, project.ActualEndDate,
            project.ClientId, client?.DisplayName, project.OwnerId, project.IsActive, project.StageCount);

        return new ProjectDetailsResponse(
            header,
            stageResponses,
            [.. tasks.Where(t => t.ProjectStageId is null)],
            CountByStatus(tasks),
            Summarize(gates.Select(g => g.Status)));
    }

    private static List<TaskStatusCount> CountByStatus(IEnumerable<ProjectTaskResponse> tasks) =>
        [.. tasks.GroupBy(t => t.Status).OrderBy(g => g.Key).Select(g => new TaskStatusCount(g.Key, g.Count()))];

    private static GateSummary Summarize(IEnumerable<Domain.GateStatus> statuses)
    {
        List<Domain.GateStatus> all = [.. statuses];
        return new GateSummary(
            all.Count,
            all.Count(s => s == Domain.GateStatus.Pending),
            all.Count(s => s == Domain.GateStatus.Satisfied),
            all.Count(s => s == Domain.GateStatus.Rejected));
    }
}
