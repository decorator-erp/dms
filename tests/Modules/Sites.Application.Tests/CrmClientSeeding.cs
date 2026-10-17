using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Sites.Application.Tests;

/// <summary>
/// Creates a client through CRM's real command. <c>CreateClientCommand</c> now creates the portal login
/// as well, so the <c>AppUserId</c> the caller used to supply has to be read back out through
/// <c>IClientDirectory</c>.
///
/// <para>The login is created inactive and is activated here, which is the state a drained outbox would
/// have left. The inactive window is asserted deliberately by <c>Crm.Application.Tests</c>' own
/// <c>ClientPortalActivationTests</c> and must not be papered over there.</para>
///
/// <para>Duplicated per test project, for the reason <c>CrmTestSeeding</c> already records: these test
/// projects do not reference each other.</para>
/// </summary>
internal static class CrmClientSeeding
{
    public static async Task<(Guid ClientId, string AppUserId)> CreateClientAsync(
        IServiceProvider sp,
        string email,
        string address = "1 Test Way",
        string firstName = "Test",
        string lastName = "Client")
    {
        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>()
            .HandleAsync(new CreateClientCommand(
                email, IdentityTestSeeding.Password, firstName, lastName, address));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);

        ClientSummary? client = await sp.GetRequiredService<IClientDirectory>()
            .GetAsync(created.Value, CancellationToken.None);
        Assert.NotNull(client);

        Result activated = await sp.GetRequiredService<IUserAdministration>()
            .ReactivateAsync(client!.AppUserId, CancellationToken.None);
        Assert.True(activated.IsSuccess, activated.IsFailure ? activated.Error.ToString() : null);

        return (created.Value, client.AppUserId);
    }
}
