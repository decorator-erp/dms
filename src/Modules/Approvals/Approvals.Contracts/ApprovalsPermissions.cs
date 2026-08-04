namespace Approvals.Contracts;

/// <summary>
/// <see cref="Read"/>/<see cref="Decide"/> ported verbatim from
/// <c>../backend/Domain/Constants/DefaultPermissions.cs</c>'s <c>Approvals</c> group — the backend has
/// no third "approvals.manage" string. <see cref="DecideAny"/> is new: an Approvals-owned replacement
/// for the backend's cross-module <c>isAdminOverride</c> (a caller holding Projects' <c>project.update</c>
/// claim could decide on behalf of the assigned approver) — Approvals must not hard-code another
/// module's permission string, so this grants the same escape hatch as a permission this module owns.
/// Not seeded to any default role except <c>admin</c>.
/// </summary>
public static class ApprovalsPermissions
{
    public const string Read = "approvals.read";
    public const string Decide = "approvals.decide";
    public const string DecideAny = "approvals.decide_any";
}
