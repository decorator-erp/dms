using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.MyWork;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
using Domain = Crm.Domain;

namespace Crm.Application.Features.MyWork;

/// <summary>
/// crm-complete F3 (D5). The caller must be an employee (403 otherwise). Their open assigned tasks come from
/// Projects (<see cref="IProjectDirectory.GetOpenTasksAssignedToAsync"/>, capped at 200) and are kept only on
/// projects they can reach (<see cref="IProjectAccessGuard.FilterAccessibleProjectsAsync"/>). Then one query
/// per kind over the whole batch — briefs by stage, brief-space entries and requirement items by task, open
/// periods and upcoming meetings by project — so the statement count does not grow with the task count. A task
/// is listed when it has its own gathering work: its stage's brief in Draft or ChangesRequested (Rejected), or a
/// pending or unresolved entry or item linked to it; the open period and upcoming meetings only fill in the dates.
/// </summary>
internal sealed class GetMyGatheringWorkQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projects, IProjectAccessGuard projectAccess)
    : IQueryHandler<GetMyGatheringWorkQuery, IReadOnlyList<MyGatheringWorkItemResponse>>
{
    /// <summary>FINDING-F-3: the cap applies to the rows returned, after the reachable and gathering-work filters;
    /// up to <see cref="MaxCandidates"/> open tasks are considered.</summary>
    private const int MaxTasks = 200;

    private const int MaxCandidates = 2000;

    public async Task<Result<IReadOnlyList<MyGatheringWorkItemResponse>>> HandleAsync(GetMyGatheringWorkQuery query, CancellationToken ct = default)
    {
        Result<CrmActor> actor = await guard.ResolveActorAsync(query.AppUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<IReadOnlyList<MyGatheringWorkItemResponse>>(actor.Error);
        }

        if (actor.Value!.Kind != CrmActorKind.Employee || actor.Value.EmployeeId is not { } me)
        {
            return Result.Failure<IReadOnlyList<MyGatheringWorkItemResponse>>(Domain.CrmMyWorkErrors.NotAnEmployee);
        }

        IReadOnlyList<TaskSummary> assigned = await projects.GetOpenTasksAssignedToAsync([me], MaxCandidates, ct);
        if (assigned.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyGatheringWorkItemResponse>>([]);
        }

        HashSet<Guid> reachable = [.. await projectAccess.FilterAccessibleProjectsAsync([.. assigned.Select(t => t.ProjectId).Distinct()], query.AppUserId, ct)];
        List<TaskSummary> tasks = [.. assigned.Where(t => reachable.Contains(t.ProjectId))];
        if (tasks.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyGatheringWorkItemResponse>>([]);
        }

        List<Guid> taskIds = [.. tasks.Select(t => t.Id)];
        List<Guid> projectIds = [.. tasks.Select(t => t.ProjectId).Distinct()];
        List<Guid> stageIds = [.. tasks.Where(t => t.ProjectStageId is not null).Select(t => t.ProjectStageId!.Value).Distinct()];

        var projectNames = (await projects.GetProjectsAsync(projectIds, ct)).ToDictionary(p => p.Id, p => p.Name);
        var stageNames = (await projects.GetStagesAsync(stageIds, ct)).ToDictionary(s => s.Id, s => s.Name);

        var briefs = (await db.CrmDesignBriefs.AsNoTracking()
                .Where(b => stageIds.Contains(b.ProjectStageId) && b.IsActive)
                .Select(b => new { b.ProjectStageId, b.Id, b.Status, b.CreatedAtUtc })
                .ToListAsync(ct))
            .GroupBy(b => b.ProjectStageId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.CreatedAtUtc).First());

        var entries = (await db.CrmBriefSpaces.AsNoTracking()
                .Where(e => e.ProjectTaskId != null && taskIds.Contains(e.ProjectTaskId.Value) && e.IsActive)
                .Select(e => new { TaskId = e.ProjectTaskId!.Value, e.ApprovalStatus })
                .ToListAsync(ct))
            .ToLookup(e => e.TaskId);

        var items = (await db.CrmRequirementItems.AsNoTracking()
                .Where(i => i.ProjectTaskId != null && taskIds.Contains(i.ProjectTaskId.Value) && i.IsActive)
                .Select(i => new { TaskId = i.ProjectTaskId!.Value, i.ApprovalStatus })
                .ToListAsync(ct))
            .ToLookup(i => i.TaskId);

        var periods = (await db.CrmRequirementPeriods.AsNoTracking()
                .Where(p => projectIds.Contains(p.ProjectId) && p.Status == Domain.RequirementPeriodStatus.Open && p.IsActive)
                .Select(p => new { p.ProjectId, p.ClosesAtUtc })
                .ToListAsync(ct))
            .GroupBy(p => p.ProjectId)
            .ToDictionary(g => g.Key, g => g.Min(p => p.ClosesAtUtc));

        DateTime now = DateTime.UtcNow;
        var meetings = (await db.CrmMeetings.AsNoTracking()
                .Where(m => projectIds.Contains(m.ProjectId) && m.IsActive && m.ScheduledAtUtc != null && m.ScheduledAtUtc >= now
                    && (m.Status == Domain.MeetingStatus.Scheduled || m.Status == Domain.MeetingStatus.Rescheduled))
                .Select(m => new { m.ProjectId, At = m.ScheduledAtUtc!.Value })
                .ToListAsync(ct))
            .ToLookup(m => m.ProjectId);

        var rows = new List<MyGatheringWorkItemResponse>(tasks.Count);
        foreach (TaskSummary task in tasks)
        {
            var brief = task.ProjectStageId is { } sid ? briefs.GetValueOrDefault(sid) : null;
            var taskEntries = entries[task.Id].ToList();
            var taskItems = items[task.Id].ToList();
            int entryPending = taskEntries.Count(e => e.ApprovalStatus == Domain.ApprovalState.Draft);
            int itemPending = taskItems.Count(i => i.ApprovalStatus == Domain.ApprovalState.PendingApproval);
            int unresolved = taskEntries.Count(e => !IsResolved(e.ApprovalStatus)) + taskItems.Count(i => !IsResolved(i.ApprovalStatus));
            var upcoming = meetings[task.ProjectId].ToList();
            DateTime? closes = periods.TryGetValue(task.ProjectId, out DateTime c) ? c : null;

            // FINDING-F-4: listed only for the task's own gathering work (its stage's brief needing work, its entries
            // and items); the project's period and meetings only fill in the dates.
            bool briefNeedsWork = brief?.Status is Domain.ApprovalState.Draft or Domain.ApprovalState.Rejected;
            if (!briefNeedsWork && entryPending == 0 && itemPending == 0 && unresolved == 0)
            {
                continue;
            }

            if (rows.Count == MaxTasks)
            {
                break;
            }

            rows.Add(new MyGatheringWorkItemResponse(
                task.Id, task.Name, task.ProjectId, projectNames.GetValueOrDefault(task.ProjectId) ?? string.Empty,
                task.ProjectStageId, task.ProjectStageId is { } s ? stageNames.GetValueOrDefault(s) : null,
                new CrmTaskBlockResponse(
                    brief?.Id, brief is null ? null : CrmEnumMapping.ToContracts(brief.Status), taskEntries.Count, entryPending,
                    taskItems.Count, itemPending, unresolved, upcoming.Count),
                closes, upcoming.Count == 0 ? null : upcoming.Min(m => m.At)));
        }

        return Result.Success<IReadOnlyList<MyGatheringWorkItemResponse>>(rows);
    }

    private static bool IsResolved(Domain.ApprovalState state) =>
        state is Domain.ApprovalState.Approved or Domain.ApprovalState.NotRequired;
}
