using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Items;

/// <summary>
/// A-3 (X-6): every id an office requirement item carries must belong to the item's own project — the
/// item counterpart of <c>BriefReferenceValidator</c>. None of these columns has a foreign key that could
/// say so (the task lives in <c>projects</c>; meeting, brief and note FKs, where they exist, prove
/// existence, not ownership), so without this an item on one project could resolve another project's
/// task gate. Each mismatch is a 400 with its own code; an unknown id counts as a mismatch.
/// </summary>
internal static class ItemReferenceValidator
{
    public static async Task<Result> ValidateAsync(
        CrmDbContext db, IProjectDirectory projects, ProjectSummary project,
        Guid? projectTaskId, Guid? meetingId, Guid? briefId, Guid? noteId, CancellationToken ct)
    {
        if (projectTaskId is { } taskId && (await projects.GetTaskAsync(taskId, ct))?.ProjectId != project.Id)
        {
            return CrmItemErrors.TaskProjectMismatch;
        }

        if (meetingId is { } meeting
            && !await db.CrmMeetings.AsNoTracking().AnyAsync(m => m.Id == meeting && m.ProjectId == project.Id, ct))
        {
            return CrmItemErrors.MeetingProjectMismatch;
        }

        if (briefId is { } brief
            && !await db.CrmDesignBriefs.AsNoTracking().AnyAsync(b => b.Id == brief && b.ProjectId == project.Id, ct))
        {
            return CrmItemErrors.BriefProjectMismatch;
        }

        // A note is the client's; one tied to a project must be tied to THIS project.
        if (noteId is { } note
            && !await db.CrmClientNotes.AsNoTracking().AnyAsync(
                n => n.Id == note && n.ClientId == project.ClientId && (n.ProjectId == null || n.ProjectId == project.Id), ct))
        {
            return CrmItemErrors.NoteProjectMismatch;
        }

        return Result.Success();
    }

    /// <summary>A-15: office item and period writes need an active project.</summary>
    public static Result EnsureProjectActive(ProjectSummary project) =>
        project.IsActive ? Result.Success() : CrmProjectErrors.Inactive;

    /// <summary>A-15: the same rule where only the id is at hand (period extend and close).</summary>
    public static async Task<Result> EnsureProjectActiveAsync(IProjectDirectory projects, Guid projectId, CancellationToken ct) =>
        await projects.GetProjectAsync(projectId, ct) switch
        {
            null => CrmPeriodErrors.NotFound,
            { IsActive: false } => CrmProjectErrors.Inactive,
            _ => Result.Success(),
        };

    /// <summary>A-8 (B-M5): a named space type must exist and be active — a 400, not the FK's 500.</summary>
    public static async Task<Result> ValidateSpaceTypeAsync(CrmDbContext db, Guid? spaceTypeId, CancellationToken ct) =>
        spaceTypeId is { } id && !await db.CrmSpaceTypes.AsNoTracking().AnyAsync(t => t.Id == id && t.IsActive, ct)
            ? CrmItemErrors.UnknownSpaceType
            : Result.Success();

    /// <summary>A-5 (B-M2): a Closed period's gate may already have fired, so its items are frozen — no
    /// create, update or supersede. An unknown period is 404.</summary>
    public static async Task<Result> EnsurePeriodOpenAsync(CrmDbContext db, Guid periodId, CancellationToken ct)
    {
        RequirementPeriodStatus? status = await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => p.Id == periodId)
            .Select(p => (RequirementPeriodStatus?)p.Status)
            .FirstOrDefaultAsync(ct);

        return status switch
        {
            null => CrmPeriodErrors.NotFound,
            RequirementPeriodStatus.Open => Result.Success(),
            _ => CrmPeriodErrors.NotOpen,
        };
    }
}
