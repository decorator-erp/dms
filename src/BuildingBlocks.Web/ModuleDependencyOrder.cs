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
    ];
}
