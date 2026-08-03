namespace Identity.Infrastructure.Persistence.Seeders;

/// <summary>
/// Ported from <c>../backend/Domain/Constants/Roles.cs</c>'s <c>DefaultRoles.All</c>, narrowed from
/// seven roles to three. The backend's other four (<c>DepartmentHead</c>, <c>Client</c>, <c>Vendor</c>,
/// <c>Contractor</c>) exist there only to carry permission claims from CRM, Sites, and Portal — none of
/// which are in scope for this build. Seeding an empty-claim placeholder role for a module that doesn't
/// exist yet is the same kind of stub the plan forbids for modules themselves; <c>Admin</c> and
/// <c>Employee</c> are the two roles that mean something with only Identity (and soon HR) in scope, and
/// <c>Manager</c> is kept because the backend gives it the identical full-permission-set treatment as
/// <c>Admin</c> and its actual seeded admin user is a member of it, not <c>Admin</c>.
/// </summary>
internal static class DefaultRoles
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Employee = "employee";

    public static readonly string[] All = [Admin, Manager, Employee];

    /// <summary>Roles that get every permission the catalog currently knows about — computed from
    /// <see cref="Identity.Contracts.IPermissionCatalog"/>, never a hard-coded permission list, so this
    /// automatically widens as HR (and later modules) register their own permissions.</summary>
    public static readonly string[] FullAccessRoles = [Admin, Manager];
}
