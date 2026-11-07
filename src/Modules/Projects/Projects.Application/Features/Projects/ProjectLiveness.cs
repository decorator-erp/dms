using Microsoft.EntityFrameworkCore;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Projects;

/// <summary>
/// NP3 (owner decision): the project's own state gates every write to its work — tasks, sub-tasks, stages, gates,
/// the scope of work and stage teams. Only an active project that is Draft or InProgress takes work writes; an
/// inactive, OnHold, Completed or Cancelled one is 409 <see cref="Domain.ProjectErrors.Closed"/>, and reads still
/// work. Runs after the route claim and the scope decorator (404), and before the handler's own state checks (409),
/// per <c>CLAUDE.md</c>'s authorization order. A missing project is 404, as everywhere in this module.
///
/// <para>A plain read, not a lock: a status change racing a work write may let that one write land. Locking the
/// project row on every task transition would serialise all of a project's work behind its structural edits.</para>
/// </summary>
internal static class ProjectLiveness
{
    public static async Task<Result> EnsureWritableAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.IsActive, p.Status })
            .FirstOrDefaultAsync(ct);
        if (project is null)
        {
            return Domain.ProjectErrors.NotFound;
        }

        return project.IsActive && project.Status is Domain.ProjectStatus.Draft or Domain.ProjectStatus.InProgress
            ? Result.Success()
            : Domain.ProjectErrors.Closed;
    }
}
