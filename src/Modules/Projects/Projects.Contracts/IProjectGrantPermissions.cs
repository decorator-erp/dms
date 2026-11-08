namespace Projects.Contracts;

/// <summary>
/// P1-A: the route permission codes a user's <b>active</b> project access grants imply, read live, through
/// <see cref="GrantAreaPermissions"/>. The route permission handlers fall back to it when the token lacks a code
/// (via <c>BuildingBlocks.IImpliedPermissionSource</c>), so granting and revoking take effect on the next request;
/// nothing is ever written into a token.
///
/// <para>Safe because every route a mapped code opens is project-scoped (the coverage test in Integration.Tests
/// holds that), and a request opened only by an implied code is checked against grants only (see
/// <see cref="IProjectAccessGuard"/>). Handlers' own <c>ICurrentUser.HasPermission</c> checks never see implied
/// codes: they are user-wide overrides, and a grant must not widen anything beyond its project.</para>
/// </summary>
public interface IProjectGrantPermissions
{
    Task<bool> HasImpliedPermissionAsync(string userId, string permission, CancellationToken ct);

    /// <summary>Batch sibling: every implied code, from one read of the user's active grants.</summary>
    Task<IReadOnlySet<string>> GetImpliedPermissionsAsync(string userId, CancellationToken ct);
}
