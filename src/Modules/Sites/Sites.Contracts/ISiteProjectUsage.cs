namespace Sites.Contracts;

/// <summary>
/// P1: which projects a site still points at, so Projects can refuse to hard-delete a project that has a
/// site (409) instead of tripping the <c>sites.sites → projects.projects</c> foreign key. Soft-deleted sites
/// count: their rows still hold the key, so both methods ignore the <c>IsActive</c> query filter.
/// </summary>
public interface ISiteProjectUsage
{
    /// <summary>True when any site row, active or soft-deleted, names <paramref name="projectId"/>.</summary>
    Task<bool> IsProjectInUseAsync(Guid projectId, CancellationToken ct);

    /// <summary>The subset of <paramref name="projectIds"/> that any site row, active or soft-deleted, names.</summary>
    Task<IReadOnlyList<Guid>> ProjectsInUseAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct);
}
