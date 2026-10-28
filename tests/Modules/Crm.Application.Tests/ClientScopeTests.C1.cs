using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Notes;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using ContactRole = Crm.Contracts.ContactRole;

namespace Crm.Application.Tests;

/// <summary>C-1 (LC-M2): client, contact and note messages are client-scoped. A caller reaches a client
/// through <c>crm.clients.read_all</c>, through a project of that client they can reach, or while the
/// client has no project at all (owner amendment M2). Anyone else gets a 404.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C1_AnEmployeeOffTheClientsProjects_Gets404_OnEveryClientContactAndNoteMessage()
    {
        ClientFixture c = await SeedAsync("c1-outsider");
        Guid clientId = c.P.ClientId;
        string office = CrmTestAccess.OfficeUserId;
        Result<Guid> child = await CommandFor<AddClientChildCommand, Guid>(office, new AddClientChildCommand(clientId, 'F', 6, office), Office);
        AssertOk(Plain(child));
        (_, string outsider) = await HireEmployeeAsync("c1-outsider");

        Result[] results =
        [
            Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(outsider, new GetClientByIdQuery(clientId, outsider))),
            await Command(outsider, new UpdateClientProfileCommand(clientId, outsider, JobTitle: "Spy")),
            Plain(await CommandFor<AddClientChildCommand, Guid>(outsider, new AddClientChildCommand(clientId, 'M', 3, outsider))),
            await Command(outsider, new UpdateClientChildCommand(clientId, child.Value, 'M', 7, outsider)),
            await Command(outsider, new RemoveClientChildCommand(clientId, child.Value, outsider)),
            Plain(await CommandFor<CreateCrmContactCommand, Guid>(outsider,
                new CreateCrmContactCommand(clientId, "Sneaky", "Contact", ContactRole.Other, IsPrimary: false, outsider))),
            await Command(outsider, new UpdateCrmContactCommand(c.ContactId, "Jane", "Renamed", ContactRole.Owner, null, null, null, null, outsider)),
            await Command(outsider, new SetPrimaryCrmContactCommand(clientId, c.ContactId, outsider)),
            Plain(await QueryFor<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>(outsider,
                new GetCrmContactsByClientQuery(clientId, outsider))),
            Plain(await CommandFor<CreateCrmClientNoteCommand, Guid>(outsider,
                new CreateCrmClientNoteCommand(clientId, null, "Sneaky note", c.P.EmployeeId, outsider))),
            await Command(outsider, new UpdateCrmClientNoteCommand(c.NoteId, "Rewritten", outsider)),
            Plain(await QueryFor<GetCrmClientNotesByClientQuery, IReadOnlyList<CrmClientNoteResponse>>(outsider,
                new GetCrmClientNotesByClientQuery(clientId, outsider))),
            await Command(outsider, new DeleteCrmClientNoteCommand(c.NoteId, outsider)),
            await Command(outsider, new SetClientActiveCommand(clientId, false, outsider)),
            await Command(outsider, new DeleteClientCommand(clientId, outsider)),
        ];

        Assert.All(results, r => AssertError(r, 404));
        ClientResponse after = (await QueryFor<GetClientByIdQuery, ClientResponse>(office, new GetClientByIdQuery(clientId, office), Office)).Value!;
        Assert.True(after.IsActive);
        Assert.Single(after.Children);
    }

    [Fact]
    public async Task C1_ReadAll_ProjectReadAll_OrStageTeamMembership_GetsThrough()
    {
        (Guid headEmployeeId, _) = await HireEmployeeAsync("c1-head");
        ClientFixture c = await SeedAsync("c1-member", headEmployeeId);
        (Guid memberEmployeeId, string member) = await HireEmployeeAsync("c1-member");
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            await BriefTestSeeding.AddStageTeamMemberAsync(scope.ServiceProvider, fixture.ConnectionString, c.P, c.P.StageId, memberEmployeeId);
        }

        (_, string reader) = await HireEmployeeAsync("c1-reader");
        GetClientByIdQuery Get(string userId) => new(c.P.ClientId, userId);

        AssertOk(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(member, Get(member))));
        AssertOk(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(reader, Get(reader), CrmPermissions.Clients.ReadAll)));
        AssertOk(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(reader, Get(reader), ProjectsPermissions.Projects.ReadAll)));
        AssertOk(Plain(await QueryFor<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>(member,
            new GetCrmContactsByClientQuery(c.P.ClientId, member))));
    }

    /// <summary>Owner amendment M2: before its first project a client is the pipeline's, visible to any
    /// <c>clients.read</c> holder. Once it has a project, the project rule applies.</summary>
    [Fact]
    public async Task C1_AClientWithNoProject_IsVisibleToAnyone_UntilItGetsAProject()
    {
        (Guid clientId, _) = await SeedClientWithoutProjectAsync("c1-pipeline");
        (_, string outsider) = await HireEmployeeAsync("c1-pipeline-outsider");
        (_, string owner) = await HireEmployeeAsync("c1-pipeline-owner");

        AssertOk(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(outsider, new GetClientByIdQuery(clientId, outsider))));
        AssertOk(Plain(await CommandFor<CreateCrmContactCommand, Guid>(outsider,
            new CreateCrmContactCommand(clientId, "First", "Contact", ContactRole.Owner, IsPrimary: true, outsider))));

        await CreateProjectForAsync(clientId, owner);

        AssertError(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(outsider, new GetClientByIdQuery(clientId, outsider))),
            404, "crm.clients.NotFound");
        AssertError(Plain(await QueryFor<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>(outsider,
            new GetCrmContactsByClientQuery(clientId, outsider))), 404, "crm.contacts.NotFound");
    }
}
