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
/// Scoped to what this solution actually builds (shared, identity, hr). Extend — never reorder past an
/// existing dependency — as later modules are added; the full ten-module order is in
/// docs/cross-schema-foreign-keys.md for reference.
/// </summary>
public static class ModuleDependencyOrder
{
    public static readonly IReadOnlyList<string> SchemaOrder =
    [
        "shared",
        "identity",
        "hr",
    ];
}
