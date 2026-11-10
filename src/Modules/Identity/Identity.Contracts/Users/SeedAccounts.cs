namespace Identity.Contracts.Users;

/// <summary>
/// The accounts Identity seeds on an empty database. Shared through the contract so the module that
/// owns the matching business record can recognize them without referencing Identity's internals.
///
/// <para><b>Every login is an employee or a client.</b> The seeded admin is no exception: Identity
/// creates the user (<c>AdminUserSeeder</c>), and HR, which seeds after Identity, gives it its employee
/// profile (<c>AdminEmployeeSeeder</c>). Without that profile the admin could not own a project
/// (<c>projects.NoEmployeeProfile</c>) or appear in the employee directory.</para>
/// </summary>
public static class SeedAccounts
{
    /// <summary>The seeded admin's fixed user id (<c>admin@system.dms</c>).</summary>
    public const string AdminUserId = "00000000-0000-0000-0000-000000000001";
}
