namespace BuildingBlocks.Web;

/// <summary>
/// The order module migrations must run in, so a module's cross-schema foreign key (see
/// docs/cross-schema-foreign-keys.md) never references a table that doesn't exist yet — e.g.
/// <c>hr.employees.app_user_id → identity.users(id)</c> requires <c>identity.users</c> to exist first.
///
/// This is the SINGLE place that order is recorded. The Api host and the integration test fixture both
/// read from here — two independently hand-maintained lists would drift, and the failure would surface
/// as a migration error that only reproduces in one of them.
///
/// Scoped to what this solution actually builds. Extend — never reorder past an existing dependency —
/// as later modules are added; the full ten-module order is in docs/cross-schema-foreign-keys.md for
/// reference.
///
/// <para><b>"crm" is an INSERTION, not an append — the one exception to append-only so far.</b> Every
/// module since G0-08 appended to the end of this list. CRM cannot: <c>projects.projects.ClientId →
/// crm.clients.Id</c> (closed at G9-13) requires <c>crm</c> before <c>projects</c>, but
/// <c>documents.project_documents.ProjectId → projects.projects.Id</c> (G7-04) already requires
/// <c>projects</c> before <c>documents</c>. A naive append would put <c>crm</c> after
/// <c>documents</c>, which is backwards for the FK it needs. The insertion is legal because the rule
/// this list protects is "never reorder past an EXISTING dependency" — nothing already migrated
/// (identity/hr/approvals/projects/documents) has ever pointed OUTWARD at <c>crm</c>, since the schema
/// didn't exist until now, so inserting it here disturbs no prior ordering constraint. See
/// <c>PLAN-PHASE4.md</c>'s structural item 1 and this insertion's own <c>PROGRESS.md</c> entry (G9-07)
/// for the full reasoning, and <c>docs/adding-a-module.md</c> (G10-01) for the generalized rule a
/// future module should apply before assuming append is always safe.</para>
///
/// <para><b>"sites" is an append, and applying that same rule is what proved it.</b> Sites points
/// OUTWARD at four schemas — <c>projects</c> (ProjectId), <c>hr</c> (CreatedById, DecidedById,
/// AddedById), <c>crm</c> (SpaceId, CatalogItemId) and <c>documents</c> (DocumentId) — and <b>nothing
/// points into it</b>. The one thing that appeared to, <c>CrmDesignBrief.SiteId</c>, was a fabricated
/// column removed at S1-00; the real brief↔site link is <c>Site.SourceBriefId</c>, pointing outward like
/// the rest. Every schema Sites references therefore already exists by the time it migrates, so the end
/// of the list is the only position it needs — the append-vs-insert distinction now has both cases
/// worked, which is why <c>docs/adding-a-module.md</c> can state the general rule rather than one
/// example.</para>
///
/// <para><b>"notify" is an append, verified by the same rule (N1-03).</b> Notifications points OUTWARD
/// at <c>identity</c> only — <c>notifications.RecipientUserId</c> and <c>activities.ActorUserId</c>,
/// both to <c>identity.users</c> — and <b>nothing points into it</b>, confirmed by grep before the
/// append rather than assumed. <c>docs/cross-schema-foreign-keys.md</c> already listed
/// <c>notify → identity</c> and already placed <c>notify</c> last, two phases before the module
/// existed; this is that plan being carried out, not a new decision.</para>
///
/// <para>Note that <c>audit_logs</c> is <b>not</b> in <c>notify</c> despite Notifications serving the
/// audit endpoints. It lives in <c>shared</c>, which is FIRST — the audit interceptor fires on every
/// module's writes, so its table has to exist before any of them migrate. See
/// <c>BuildingBlocks.Auditing.AuditModelConfiguration</c>.</para>
/// </summary>
public static class ModuleDependencyOrder
{
    public static readonly IReadOnlyList<string> SchemaOrder =
    [
        "shared",
        "identity",
        "hr",
        "approvals",
        "crm",
        "projects",
        "documents",
        "sites",
        "notify",
    ];
}
