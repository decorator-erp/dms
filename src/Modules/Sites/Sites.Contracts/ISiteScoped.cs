namespace Sites.Contracts;

/// <summary>
/// Marker for a command/query whose access must be checked before the handler runs — the ORIGINAL of
/// the pattern this repo already copied twice. <c>Projects.Contracts.IProjectScoped</c> (G5-07) and
/// <c>Crm.Contracts.ICrmScoped</c> (G9-08) are both descendants of the backend's own
/// <c>ISiteScoped</c>/<c>SiteScopeDecorator</c>; Sites gets the original last, and mirrors the two
/// existing implementations rather than the backend directly.
///
/// <para><see cref="AppUserId"/> travels on the message itself — never bound from the request body,
/// the endpoint populates it from <c>ICurrentUser</c> — because <c>Sites.Application</c> takes no
/// ASP.NET dependency and cannot read the HTTP principal directly.</para>
///
/// <para><b>A site is not its own access boundary; its project is.</b> <c>SiteScopeDecorator</c>
/// (S1-05) resolves <see cref="SiteId"/> to its <c>ProjectId</c> and defers to
/// <c>Projects.Contracts.IProjectAccessGuard</c>, collapsing an unresolvable site to 404 — the
/// <c>ProjectScopeDecorator</c> shape, not the <c>CrmScopeDecorator</c> one (which propagates the
/// guard's own 403). That divergence is deliberate on both sides; see <c>CLAUDE.md</c>'s
/// authorization order.</para>
///
/// <para><b>Global master data is deliberately outside this.</b> The definition layer (modules, items,
/// options, links) has no project at all, so none of its 20 messages implement this interface — the
/// backend's <c>SiteEndpoints</c> doc comment says so explicitly. That is an exemption, not an
/// oversight, which is why S1-05 also ports the backend's startup coverage guard: it fails the build
/// for any message carrying a <c>Guid SiteId</c> that does NOT implement this interface, so the
/// exemption can only ever be claimed by a message that genuinely has no site.</para>
/// </summary>
public interface ISiteScoped
{
    Guid SiteId { get; }
    string AppUserId { get; }
}
