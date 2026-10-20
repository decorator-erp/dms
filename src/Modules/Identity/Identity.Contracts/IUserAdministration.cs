using SharedKernel;

namespace Identity.Contracts;

/// <summary>
/// Write operations other modules may perform on a user account. Deliberately narrow.
/// HR calls this when an employee is deactivated — HR → Identity is an allowed direction, and it is
/// the only way to keep "employee left" and "login disabled" in step now that Identity is a sink and
/// cannot subscribe to Hr.Contracts events.
/// </summary>
public interface IUserAdministration
{
    Task<Result> DeactivateAsync(string userId, CancellationToken ct);
    Task<Result> ReactivateAsync(string userId, CancellationToken ct);

    /// <summary>N7: sets the login's first and last name (trimmed, 1..100 each). Idempotent: the same
    /// name again succeeds. No session change: tokens carry the user name (the email), not this
    /// name.</summary>
    Task<Result> UpdateNameAsync(string userId, string firstName, string lastName, CancellationToken ct);
}
