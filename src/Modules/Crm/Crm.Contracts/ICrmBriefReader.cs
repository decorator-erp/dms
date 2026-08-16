namespace Crm.Contracts;

/// <summary>The design-brief facts a future Sites module needs — declared at G9-01, per Phase 4's
/// structural item 2, so Sites has a seam to build against without CRM changing shape later. This is
/// the "what was stated" half of the stated-vs-measured split the brief's own doc comment describes.
///
/// <para>A <c>SiteId</c> member was removed at S1-00. It mirrored <c>CrmDesignBrief.SiteId</c>, which
/// <c>PLAN-PHASE4.md</c> instructed G9-04 to add in error — the backend entity has no such field and
/// the real link is <c>Site.SourceBriefId</c>, pointing the other way. See that entity's doc comment.
/// </para>
///
/// <para><b>This interface has no implementation.</b> Nothing constructs a
/// <see cref="CrmDesignBriefSummary"/> and nothing registers an <see cref="ICrmBriefReader"/> — it is
/// a seam declared ahead of its consumer. Sites (Phase 5) did not become that consumer: its only use
/// would have been <c>GET /api/sites/{siteId}/variance</c>, deferred at S1-01 because
/// <c>Site.SourceBriefId</c> is never written in this system, so the route could only ever return
/// <c>BriefProvenanceLost</c>.</para></summary>
public sealed record CrmDesignBriefSummary(
    Guid Id, Guid ProjectId, Guid? ProjectStageId, CrmApprovalState Status);

public interface ICrmBriefReader
{
    Task<CrmDesignBriefSummary?> GetForProjectAsync(Guid projectId, CancellationToken ct);

    Task<IReadOnlyList<CrmDesignBriefSummary>> GetManyForProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, CancellationToken ct);
}
