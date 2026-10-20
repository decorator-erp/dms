namespace Identity.Infrastructure.Services;

/// <summary>
/// K4: checks a login's credentials, with lockout, and gives every failure the same answer.
///
/// <para><b>It runs in a scope of its own.</b> Every command runs inside a transaction that
/// <c>DomainEventDispatchDecorator</c> rolls back when the handler returns a failure, and a failed login
/// <i>is</i> a failure. The failed-attempt counter and the lockout end date written by
/// <c>SignInManager.CheckPasswordSignInAsync</c> would be rolled back with it, and lockout would never
/// trigger. A separate scope means a separate <c>DbContext</c> outside that transaction, so the
/// bookkeeping commits whatever the login's outcome.</para>
/// </summary>
internal interface IPasswordSignIn
{
    Task<PasswordSignInResult> CheckAsync(string email, string password, CancellationToken ct = default);
}

internal enum PasswordSignInOutcome
{
    Succeeded,

    /// <summary>Unknown email, wrong password, or an inactive account: deliberately indistinguishable.</summary>
    InvalidCredentials,

    /// <summary>Locked out. Reported without checking the password, so it reveals nothing about it.</summary>
    LockedOut,
}

internal sealed record PasswordSignInResult(PasswordSignInOutcome Outcome, string? UserId = null);
