namespace Identity.Contracts;

/// <summary>
/// N2: what an access token's session check needs to know about its user: the current security stamp,
/// and whether the account is active. The host's JwtBearer <c>OnTokenValidated</c> compares the token's
/// <c>sstamp</c> claim with <see cref="UserSessionSnapshot.SecurityStamp"/> on every request. A missing
/// user, an inactive one, or a different stamp means the token predates a change to the account
/// (deactivation, roles, permissions, password) and is refused with <c>authentications.StaleToken</c>.
///
/// <para>Answers are cached briefly (<c>Identity:SessionStateCacheSeconds</c>, default 30), because this
/// runs on every authenticated request. Identity evicts a user's entry whenever it changes their stamp,
/// so the cache only delays changes made outside this process.</para>
/// </summary>
public interface IUserSessionState
{
    Task<UserSessionSnapshot?> GetAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<UserSessionSnapshot>> GetManyAsync(IReadOnlyCollection<string> userIds, CancellationToken ct);
}

public sealed record UserSessionSnapshot(string UserId, string SecurityStamp, bool IsActive);
