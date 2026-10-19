namespace Identity.Infrastructure.Services;

/// <summary>
/// K5: ends every session of a user in a transaction of its own. Every refresh token is revoked and the
/// security stamp bumped, so live access tokens die too (N2).
///
/// <para>Separate from the calling command on purpose. It is used when a rotated refresh token is
/// replayed, and the refresh command then <i>fails</i>, so <c>DomainEventDispatchDecorator</c> rolls its
/// transaction back. A revocation written in that transaction would be undone along with it, and the
/// thief would keep the token family.</para>
/// </summary>
internal interface ISessionRevocation
{
    Task RevokeAllInOwnTransactionAsync(string userId, CancellationToken ct = default);
}
