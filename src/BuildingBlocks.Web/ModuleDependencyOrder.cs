namespace BuildingBlocks.Web;

/// <summary>
/// The order module migrations must run in, so a module's cross-schema foreign key (see
/// docs/cross-schema-foreign-keys.md) never references a table that doesn't exist yet — e.g.
/// <c>hr.employees.app_user_id → identity.users(id)</c> requires <c>identity.users</c> to exist first.
///
/// This is the SINGLE place that order is recorded in code. Only the test fixtures read it today
/// (<c>ApiFixture</c>, and architecture Rules 9 and 12); the Api host does not migrate at all, so a
/// deployment applies the migrations in this order itself. Two hand-maintained lists would drift, and
/// the failure would surface as a migration error that only reproduces in one of them.
///
/// It lists all ten schemas, the same order as docs/cross-schema-foreign-keys.md. Extend — never
/// reorder past an existing dependency — as later modules are added; docs/adding-a-module.md has the
/// rule for deciding between an append and an insertion.
///
/// <para><b>"crm" is an INSERTION, not an append — the one exception to append-only so far.</b> Every
/// module since G0-08 appended to the end of this list. CRM cannot: <c>projects.projects.ClientId →
/// crm.clients.Id</c> (closed at G9-13) requires <c>crm</c> before <c>projects</c>, but
/// <c>documents.project_documents.ProjectId → projects.projects.Id</c> (G7-04) already requires
/// <c>projects</c> before <c>documents</c>. A naive append would put <c>crm</c> after
/// <c>documents</c>, which is backwards for the FK it needs. The insertion is legal because the rule
/// this list protects is "never reorder past an EXISTING dependency" — nothing already migrated
/// (identity/hr/approvals/projects/documents) has ever pointed OUTWARD at <c>crm</c>, since the schema
/// didn't exist until now, so inserting it here disturbs no prior ordering constraint (G9-07). See
/// <c>docs/cross-schema-foreign-keys.md</c> for the foreign keys involved and
/// <c>docs/adding-a-module.md</c> (G10-01) for the generalized rule a future module should apply before
/// assuming append is always safe.</para>
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
/// <para><b>"finance" is the second INSERTION, and unlike CRM it is not forced — it is honoured.</b>
/// Applying the same rule proves the position is free: Finance points OUTWARD at <c>projects</c>
/// (ProjectId, ProjectStageId, ProjectTaskId), <c>crm</c> (ClientId, SignedByClientId) and <c>hr</c>
/// (RecordedById, RequestedById, ApprovedById, GeneratedById), and <b>nothing points into it</b> — the
/// one thing that appears to, <c>projects.gate_conditions.VariationId</c> (F1-00), deliberately carries
/// no foreign key precisely because a <c>projects → finance</c> constraint would make this order
/// unsatisfiable in both directions at once. So <c>finance</c> would migrate correctly appended at the
/// end, exactly as <c>sites</c> and <c>notify</c> do.
///
/// It is inserted before <c>notify</c> anyway because
/// <c>docs/cross-schema-foreign-keys.md</c> has shipped that ten-module order since N1-03, two phases
/// before this module existed, with prose saying finance would insert there. Choosing the append would
/// have meant editing a document that was already correct. <b>The position is arbitrary with respect to
/// correctness and deliberate with respect to the record</b> — which is the useful distinction for the
/// next module to apply, since it is the FK graph, never the arrival order, that decides.</para>
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
        "finance",
        "notify",
    ];
}
