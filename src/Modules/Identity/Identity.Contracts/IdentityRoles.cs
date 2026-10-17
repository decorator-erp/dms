namespace Identity.Contracts;

/// <summary>
/// The role names a module may name when provisioning a user. The seeded set is wider
/// (<c>DefaultRoles</c> in <c>Identity.Infrastructure</c>, which also holds <c>admin</c> and
/// <c>manager</c>) but that class is <c>internal</c>, so before this existed every caller — including
/// every test fixture — hard-coded the literal <c>"employee"</c>.
///
/// <para><b>Only the two provisionable roles are named here, and that is the point.</b>
/// <c>IUserProvisioning</c> rejects <c>DefaultRoles.FullAccessRoles</c> at runtime; this class means a
/// caller cannot even spell <c>admin</c> without inventing the string itself. Two mechanisms, so the
/// escalation path is structurally absent rather than merely checked. Do not add the privileged names
/// here for symmetry.</para>
/// </summary>
public static class IdentityRoles
{
    public const string Employee = "employee";
    public const string Client = "client";
}
