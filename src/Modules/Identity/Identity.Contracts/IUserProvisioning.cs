using SharedKernel;

namespace Identity.Contracts;

public enum UserKind
{
    Employee,
    Client,
}

public sealed record NewUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    UserKind Kind,
    string RoleName);

/// <summary>
/// <para><b>The user is created INACTIVE and this contract never activates it.</b> The caller commits
/// its own business row and an integration event in one transaction, and activation happens in that
/// event's handler. So a crash between provisioning and the business row leaves an account that cannot
/// log in, rather than a usable orphan — and <c>UnlinkedUserReaperJob</c> eventually deletes it. Do not
/// add an "activate here" overload; the gap is the design.</para>
///
/// <para><b>Privileged roles are rejected outright</b> (<c>DefaultRoles.FullAccessRoles</c>), not
/// permission-checked. Together with <see cref="IdentityRoles"/> naming only the two provisionable
/// roles, admin and manager are unreachable through this surface rather than merely guarded.</para>
///
/// <para>Reaper operations live on <c>IUserLifecycleMaintenance</c>, deliberately not here: HR and CRM
/// both reference this interface and must not acquire enumerate-and-delete with it.</para>
/// </summary>
public interface IUserProvisioning
{
    Task<Result<string>> CreateAsync(NewUserRequest request, CancellationToken ct);
}
