using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Documents.Application.Tests;

/// <summary>
/// G9-13's ripple: <c>Project.ClientId</c> now carries a real FK to <c>crm.clients(Id)</c>
/// (<c>AddClientForeignKey</c>), so <c>FolderFlowTests</c> needs a real client row before it can create
/// a project at all. <see cref="DocumentsApplicationFixture"/> boots <c>Crm.Infrastructure</c> as a real
/// module in its own DI graph (added at this same task), so this goes through the real
/// <c>Crm.Application</c> command handler — same shape as <c>Projects.Application.Tests</c>' own
/// <c>CrmTestSeeding</c>, duplicated rather than shared because the two test projects don't reference
/// each other.
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
