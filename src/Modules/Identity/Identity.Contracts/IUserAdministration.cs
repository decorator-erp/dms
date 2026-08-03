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
}
