namespace Crm.Contracts;

/// <summary>
/// Cross-module read surface for CRM spaces. Every lookup has a batch sibling — no exceptions, per
/// <c>docs/adding-a-module.md</c>'s Contracts rule. IDs and primitives only, no navigations —
/// consistent with <c>IHrDirectory</c>/<c>IProjectDirectory</c>/<c>IDocumentQueries</c>.
///
/// <para><b>Added at S1-01 for Sites, which cannot enforce its own invariant without it.</b> A
/// <c>SiteRecord</c> may pin a captured record to a CRM space, and the rule
/// <c>SiteErrors.SpaceNotInProject</c> ("that space belongs to a different project") is one the
/// backend enforces by reaching straight into the spaces table from a site repository
/// (<c>SiteRepositories.SpaceBelongsToSiteProjectAsync</c>). A module may never read another module's
/// tables here, and <c>GetCrmSpacesByProjectQuery</c> is an <c>IQuery&lt;T&gt;</c> that rule 10
/// forbids Sites from depending on — so this interface is the only shape the invariant can take.
/// Without it the rule would have been silently dropped, and a foreign-key constraint cannot
/// substitute: an FK proves the space EXISTS, never that it belongs to the right project.</para>
///
/// <para><see cref="CrmSpaceSummary.IsActive"/> is carried deliberately. The backend's check requires
/// the space to be active as well as same-project, and CRM's own soft-delete state is not something
/// Sites can infer from a bare id.</para>
/// </summary>
public sealed record CrmSpaceSummary(Guid Id, Guid ProjectId, Guid SpaceTypeId, string Name, bool IsActive);

public interface ICrmSpaceReader
{
    Task<CrmSpaceSummary?> GetAsync(Guid spaceId, CancellationToken ct = default);

    /// <summary>Batch, mandatory — never call <see cref="GetAsync"/> in a loop over a list. One
    /// set-based query for the whole batch (the grid save at <c>BulkSaveSiteRecordsCommand</c> is
    /// exactly that caller).</summary>
    Task<IReadOnlyList<CrmSpaceSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> spaceIds, CancellationToken ct = default);
}
