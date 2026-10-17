using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Seeds an Identity login through the real <see cref="IUserProvisioning"/> contract, resolved from the
/// running host's own container. Replaces the <c>POST /api/auth/register</c> call thirteen files in this
/// project used for the same purpose, which went with self-registration.
///
/// <para><b>There is no HTTP route that creates a bare login any more, and that is the point.</b> A user
/// now comes into existence only as one half of an employee or a client, so a test that wants a login
/// without either has to ask the contract directly — the same way <c>ApiFixture</c> already seeds its
/// public-lead intake owner outside any module's command surface. Tests that want the real end-to-end
/// path should go through <c>POST /api/employees</c> or <c>POST /api/clients</c> instead.</para>
///
/// <para>Provisioned users are inactive; this activates them, because these fixtures want the end state
/// a committed business record would have produced. The inactive window is asserted deliberately
/// elsewhere — see <c>Identity.Application.Tests.AuthFlowTests</c> and HR's and CRM's activation
/// tests.</para>
/// </summary>
internal static class IdentityTestSeeding
{
    public const string Password = "P@ssw0rd123!";

    public static async Task<string> ProvisionActiveUserAsync(
        ApiFixture fixture,
        string email,
        string firstName = "Test",
        string lastName = "User",
        UserKind kind = UserKind.Employee,
        string roleName = IdentityRoles.Employee)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();

        Result<string> provisioned = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateAsync(
                new NewUserRequest(email, Password, firstName, lastName, null, kind, roleName),
                CancellationToken.None);

        Assert.True(provisioned.IsSuccess, provisioned.IsFailure ? provisioned.Error.ToString() : null);

        Result activated = await scope.ServiceProvider.GetRequiredService<IUserAdministration>()
            .ReactivateAsync(provisioned.Value!, CancellationToken.None);

        Assert.True(activated.IsSuccess, activated.IsFailure ? activated.Error.ToString() : null);

        return provisioned.Value!;
    }
}
