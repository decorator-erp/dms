using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

/// <summary>
/// Every id a brief or brief-space entry carries must belong to the brief's own project — and a task to
/// the brief's own stage. None of these columns has a foreign key that could say so (stage and task live
/// in <c>projects</c>, which <c>crm</c> precedes in SchemaOrder; space and meeting FKs prove existence,
/// not ownership), so without this an approval on one project's brief could resolve another project's
/// gates. Each mismatch is a 400 with its own code; an unknown id counts as a mismatch.
/// </summary>
internal static class BriefReferenceValidator
{
    public static async Task<Result> ValidateStageAsync(
        IProjectDirectory projects, Guid projectId, Guid stageId, CancellationToken ct)
    {
        StageSummary? stage = await projects.GetStageAsync(stageId, ct);
        return stage?.ProjectId == projectId ? Result.Success() : CrmBriefErrors.StageProjectMismatch;
    }

    public static async Task<Result> ValidateEntryReferencesAsync(
        CrmDbContext db, IProjectDirectory projects, CrmDesignBrief brief,
        Guid? spaceId, Guid? projectTaskId, Guid? meetingId, CancellationToken ct)
    {
        if (spaceId is { } space
            && !await db.CrmSpaces.AsNoTracking().AnyAsync(s => s.Id == space && s.ProjectId == brief.ProjectId, ct))
        {
            return CrmBriefErrors.SpaceProjectMismatch;
        }

        if (projectTaskId is { } taskId)
        {
            TaskSummary? task = await projects.GetTaskAsync(taskId, ct);
            if (task is null || task.ProjectId != brief.ProjectId)
            {
                return CrmBriefErrors.TaskProjectMismatch;
            }

            if (task.ProjectStageId is { } taskStageId && taskStageId != brief.ProjectStageId)
            {
                return CrmBriefErrors.TaskStageMismatch;
            }
        }

        if (meetingId is { } meeting
            && !await db.CrmMeetings.AsNoTracking().AnyAsync(m => m.Id == meeting && m.ProjectId == brief.ProjectId, ct))
        {
            return CrmBriefErrors.MeetingProjectMismatch;
        }

        return Result.Success();
    }
}
