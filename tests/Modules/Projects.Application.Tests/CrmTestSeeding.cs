using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Projects.Application.Tests;

/// <summary>
/// G9-13's ripple: <c>Project.ClientId</c> now carries a real FK to <c>crm.clients(Id)</c>
/// (<c>AddClientForeignKey</c>), so every test in this project that creates a project needs a real
/// client row first — <c>Guid.NewGuid()</c> no longer survives the insert. Unlike
/// <c>Integration.Tests</c>' own <c>CrmTestSeeding</c> (raw SQL, since CRM isn't registered into the API
/// host until G9-14), <see cref="ProjectsApplicationFixture"/> boots <c>Crm.Infrastructure</c> as a real
/// module in its own DI graph (added at this same task), so this goes through the real
/// <c>Crm.Application</c> command handler instead.
/// </summary>
internal static class CrmTestSeeding
{
    public static async Task<Guid> SeedClientAsync(IServiceProvider sp, string appUserId)
    {
        var createClient = sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();
        var result = await createClient.HandleAsync(new CreateClientCommand(appUserId, "1 Client Rd"));
        return result.Value;
    }
}
