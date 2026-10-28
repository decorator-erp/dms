using Crm.Contracts.Clients;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-14 (C-2 follow-up, owner default): a project's owner always has access to it — and so to its
/// client — even with no stage team and no <c>read_all</c>.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task D14_TheOwnerOfAFreeStyleProjectWithNoTeam_SeesTheProjectAndItsClient()
    {
        (Guid clientId, _) = await SeedClientWithoutProjectAsync("d14-client");
        (_, string owner) = await HireEmployeeAsync("d14-owner");
        Guid projectId = await CreateProjectForAsync(clientId, owner);
        (_, string outsider) = await HireEmployeeAsync("d14-outsider");

        AssertOk(Plain(await QueryFor<GetProjectQuery, ProjectResponse>(owner, new GetProjectQuery(projectId, owner))));
        AssertOk(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(owner, new GetClientByIdQuery(clientId, owner))));

        AssertError(Plain(await QueryFor<GetProjectQuery, ProjectResponse>(outsider, new GetProjectQuery(projectId, outsider))), 404);
        AssertError(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(outsider, new GetClientByIdQuery(clientId, outsider))), 404);
    }
}
