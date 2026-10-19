namespace Identity.Infrastructure.Services;

/// <summary>
/// N2: invalidates the access tokens a user already holds. A token carries the security stamp it was
/// issued with (<c>sstamp</c>); changing the stored stamp makes every earlier token
/// <c>authentications.StaleToken</c> at its next request. The user's refresh token still works (unless
/// the change also revoked it), so the client refreshes and receives a token with the new roles and
/// permissions. Every call also evicts the user's cached session state, so the change takes effect
/// immediately in this process.
///
/// <para>Called on deactivation, role assign/remove, any claim change on a role (for every holder),
/// override grant/deny/revoke (the target), password change, logout and refresh-token reuse. It runs
/// inside the calling command's transaction: if the command fails, the stamp change rolls back with it.</para>
/// </summary>
internal interface ISecurityStamps
{
    Task BumpAsync(string userId, CancellationToken ct = default);

    /// <summary>Bumps every user holding <paramref name="roleId"/>: a claim change on a role changes what
    /// all of their tokens should say.</summary>
    Task BumpRoleHoldersAsync(string roleId, CancellationToken ct = default);
}
