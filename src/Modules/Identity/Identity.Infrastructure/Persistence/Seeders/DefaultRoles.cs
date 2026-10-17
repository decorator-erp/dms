using Identity.Contracts;

namespace Identity.Infrastructure.Persistence.Seeders;

/// <summary>
/// Ported from <c>../backend/Domain/Constants/Roles.cs</c>'s <c>DefaultRoles.All</c>, narrowed from
/// seven roles to four. The backend's other three (<c>DepartmentHead</c>, <c>Vendor</c>,
/// <c>Contractor</c>) still exist there only to carry permission claims from Sites and other modules not
/// in scope for this build; <c>Client</c> now does have a real consumer (CRM's Portal, G9-14) and is
/// added here — seeding an empty-claim placeholder role for a module that doesn't exist yet is the same
/// kind of stub the plan forbids for modules themselves, but that no longer describes <c>Client</c> once
/// CRM exists. <c>Admin</c> and <c>Employee</c> are the two roles that meant something with only Identity
/// (and soon HR) in scope, and <c>Manager</c> is kept because the backend gives it the identical
/// full-permission-set treatment as <c>Admin</c> and its actual seeded admin user is a member of it, not
/// <c>Admin</c>.
///
/// <para><b><c>Client</c> is NOT in <see cref="FullAccessRoles"/> and gets no automatic claim
/// seeding.</b> Its CRM-specific permission grants (<c>crm.portal</c>, <c>crm.briefs.view_own</c>,
/// <c>requirements.submit_own</c>, …) are assigned the same way <c>Employee</c>'s own permissions
/// already are in this repo — through the general-purpose role/permission admin endpoints, never
/// hard-coded here. Identity cannot reference <c>Crm.Contracts.CrmPermissions</c> to do that seeding
/// itself even if it wanted to (only <c>Crm.Contracts</c> may be referenced by other modules, and
/// Identity has no dependency on CRM at all per the module graph) — and CRM cannot write
/// <c>identity.role_claims</c> directly either (cross-module writes are outbox-only, per
/// <c>CLAUDE.md</c>). This closes the question G9-09's own PROGRESS.md entry flagged explicitly for this
/// task: yes, a real <c>"client"</c> role now exists for Portal permission-claim issuance to use.</para>
/// </summary>
internal static class DefaultRoles
{
    public const string Admin = "admin";
    public const string Manager = "manager";

    // The two provisionable roles are defined in Identity.Contracts, not here — other modules must be
    // able to name them and this class is internal. Aliased rather than re-spelled so the seeded role
    // and the one a caller asks for cannot drift apart into a silent RoleErrors.NotFound.
    public const string Employee = IdentityRoles.Employee;
    public const string Client = IdentityRoles.Client;

    public static readonly string[] All = [Admin, Manager, Employee, Client];

    /// <summary>Roles that get every permission the catalog currently knows about — computed from
    /// <see cref="Identity.Contracts.IPermissionCatalog"/>, never a hard-coded permission list, so this
    /// automatically widens as HR (and later modules) register their own permissions.</summary>
    public static readonly string[] FullAccessRoles = [Admin, Manager];
}
