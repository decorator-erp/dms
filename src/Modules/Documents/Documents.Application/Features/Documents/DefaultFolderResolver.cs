using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Documents.Application.Features.Documents;

/// <summary>
/// Ported from the backend's <c>DefaultFolderResolver</c>: if a stage/task already has a default folder,
/// return its id; otherwise create one and <c>Add</c> it — deliberately WITHOUT calling
/// <c>SaveChangesAsync</c>. The caller (an upload command, G7-07) commits the new folder in the SAME
/// <c>SaveChangesAsync</c> as the document it's about to create; this works only because
/// <c>DocumentFolder.Id</c> is client-generated (<c>Guid.CreateVersion7()</c> in the base
/// <c>Entity</c> ctor), so the folder's id is valid before it's ever inserted.
///
/// <para>
/// <b>G7-02's correction 5, closed here:</b> the backend names every default folder
/// <c>$"Task: {task.Name}"</c>/<c>$"Stage: {stage.Name}"</c> at the project root
/// (<c>ParentFolderId == null</c>) — against the unique index on
/// <c>(ProjectId, ParentFolderId, Name)</c>, two DIFFERENT tasks sharing a name (a real, common case —
/// "Review" appearing under two stages) collide on the second upload. This port disambiguates by
/// appending the scope's own id (hex, no dashes) to the name, guaranteeing uniqueness without changing
/// the unique index or the shared-root layout.
/// </para>
/// </summary>
internal static class DefaultFolderResolver
{
    public static async Task<Guid> EnsureForStageAsync(
        DocumentsDbContext db, Guid projectId, Guid projectStageId, string stageName, CancellationToken ct)
    {
        Guid? existingId = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.ProjectStageId == projectStageId && f.IsDefault)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId is { } id)
        {
            return id;
        }

        string name = Truncate($"Stage: {stageName} ({projectStageId:N})");
        DocumentFolder folder = DocumentFolder.Create(
            projectId, name, projectStageId: projectStageId, isDefault: true).Value!;
        db.DocumentFolders.Add(folder);

        return folder.Id;
    }

    public static async Task<Guid> EnsureForTaskAsync(
        DocumentsDbContext db, Guid projectId, Guid projectTaskId, string taskName, CancellationToken ct)
    {
        Guid? existingId = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.ProjectTaskId == projectTaskId && f.IsDefault)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId is { } id)
        {
            return id;
        }

        string name = Truncate($"Task: {taskName} ({projectTaskId:N})");
        DocumentFolder folder = DocumentFolder.Create(
            projectId, name, projectTaskId: projectTaskId, isDefault: true).Value!;
        db.DocumentFolders.Add(folder);

        return folder.Id;
    }

    /// <summary><c>DocumentFolder.Name</c> is capped at 256 chars (G7-03's configuration). The id
    /// suffix is fixed-length (32 hex chars + " ()" = 35), so truncating from the LEFT edge of the
    /// display name (never the suffix) always keeps the disambiguator intact.</summary>
    private static string Truncate(string name)
    {
        const int maxLength = 256;
        return name.Length <= maxLength ? name : name[^maxLength..];
    }
}
