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
}
