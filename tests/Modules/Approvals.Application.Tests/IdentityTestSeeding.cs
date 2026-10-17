using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Approvals.Application.Tests;

/// <summary>
/// Seeds an Identity login through the real <see cref="IUserProvisioning"/> contract. Replaces the
/// <c>RegisterCommand</c> handler every fixture in this repo used to call for the same purpose, which was
/// deleted along with self-registration.
///
/// <para><b>Provisioning leaves a user inactive, so this activates it.</b> In production a login becomes
/// usable only once its business record has committed and the resulting integration event has been
/// processed; these fixtures want that end state, not the window before it. The window itself is asserted
/// deliberately, by the activation tests in <c>Hr.Application.Tests</c> and <c>Crm.Application.Tests</c> —
/// activating here is a fixture convenience and must never be what those tests rely on.</para>
///
/// <para>Duplicated per test project rather than shared, for the reason <c>CrmTestSeeding</c> already
/// records: these test projects do not reference each other.</para>
/// </summary>
internal static class IdentityTestSeeding
{
    public const string Password = "P@ssw0rd123!";

    public static async Task<string> ProvisionActiveUserAsync(
        IServiceProvider sp,
        string email,
        string firstName = "Test",
        string lastName = "User",
        UserKind kind = UserKind.Employee,
        string roleName = IdentityRoles.Employee)
    {
        Result<string> provisioned = await sp.GetRequiredService<IUserProvisioning>().CreateAsync(
            new NewUserRequest(email, Password, firstName, lastName, null, kind, roleName),
            CancellationToken.None);

        Assert.True(provisioned.IsSuccess, provisioned.IsFailure ? provisioned.Error.ToString() : null);

        Result activated = await sp.GetRequiredService<IUserAdministration>()
            .ReactivateAsync(provisioned.Value!, CancellationToken.None);

        Assert.True(activated.IsSuccess, activated.IsFailure ? activated.Error.ToString() : null);

        return provisioned.Value!;
    }
}
