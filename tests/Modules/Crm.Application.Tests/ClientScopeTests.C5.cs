using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Notes;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;
using ContactRole = Crm.Contracts.ContactRole;

namespace Crm.Application.Tests;

/// <summary>C-5 (LC-M5): a deactivated client takes no new writes — contacts, notes, children and its
/// profile answer 409 <c>crm.clients.Inactive</c> — and Projects no longer opens a project for it.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C5_EveryWriteOnAnInactiveClient_Is409()
    {
        ClientFixture c = await SeedAsync("c5-inactive");
        Guid clientId = c.P.ClientId;
        string office = CrmTestAccess.OfficeUserId;
        Result<Guid> child = await CommandFor<AddClientChildCommand, Guid>(office, new AddClientChildCommand(clientId, 'F', 6, office), Office);
        AssertOk(Plain(child));
        AssertOk(await Command(office, new SetClientActiveCommand(clientId, false, office), Office));

        Result[] results =
        [
            await Command(office, new UpdateClientProfileCommand(clientId, office, JobTitle: "Architect"), Office),
            Plain(await CommandFor<AddClientChildCommand, Guid>(office, new AddClientChildCommand(clientId, 'M', 3, office), Office)),
            await Command(office, new UpdateClientChildCommand(clientId, child.Value, 'F', 7, office), Office),
            await Command(office, new RemoveClientChildCommand(clientId, child.Value, office), Office),
            Plain(await CommandFor<CreateCrmContactCommand, Guid>(office,
                new CreateCrmContactCommand(clientId, "New", "Contact", ContactRole.Other, IsPrimary: false, office), Office)),
            await Command(office, new UpdateCrmContactCommand(c.ContactId, "Jane", "Renamed", ContactRole.Owner, null, null, null, null, office), Office),
            await Command(office, new SetPrimaryCrmContactCommand(clientId, c.ContactId, office), Office),
            Plain(await CommandFor<CreateCrmClientNoteCommand, Guid>(office,
                new CreateCrmClientNoteCommand(clientId, null, "Late note", c.P.EmployeeId, office), Office)),
        ];

        Assert.All(results, r => AssertError(r, 409, "crm.clients.Inactive"));
    }

    [Fact]
    public async Task C5_AProjectForAnInactiveClient_IsRefused_AsClientNotFound()
    {
        (Guid clientId, _) = await SeedClientWithoutProjectAsync("c5-project");
        (_, string owner) = await HireEmployeeAsync("c5-owner");
        string office = CrmTestAccess.OfficeUserId;
        AssertOk(await Command(office, new SetClientActiveCommand(clientId, false, office), Office));

        await using AsyncServiceScope scope = ScopeFor(owner, ProjectsPermissions.Projects.ReadAll);
        Result<Guid> project = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand($"C5-{Guid.NewGuid():N}"[..12], "Inactive client", "1 St", WorkflowType.FreeStyle, clientId));

        AssertError(Plain(project), 404, "projects.ClientNotFound");
    }
}
