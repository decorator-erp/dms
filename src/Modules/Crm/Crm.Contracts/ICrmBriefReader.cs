namespace Crm.Contracts;

/// <summary>The design-brief facts a future Sites module needs — declared now, per this phase's
/// structural item 2, so Sites has a seam to build against without CRM changing shape later.
/// <see cref="CrmDesignBriefSummary.SiteId"/> is the bare nullable Guid the brief entity itself
/// carries (no FK — Sites doesn't exist yet); everything else is the "what was stated" half of the
/// stated-vs-measured split the brief's own doc comment describes.</summary>
public sealed record CrmDesignBriefSummary(
    Guid Id, Guid ProjectId, Guid? ProjectStageId, Guid? SiteId, CrmApprovalState Status);

public interface ICrmBriefReader
{
    Task<CrmDesignBriefSummary?> GetForProjectAsync(Guid projectId, CancellationToken ct);

    Task<IReadOnlyList<CrmDesignBriefSummary>> GetManyForProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, CancellationToken ct);
}
