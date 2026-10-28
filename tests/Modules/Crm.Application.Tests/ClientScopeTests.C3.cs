using BuildingBlocks.Pagination;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>C-3 (LC-M3): the client list is paged, slim, searchable, and holds only the clients the C-1
/// rule lets the caller see.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C3_TheClientList_ShowsOnlyVisibleClients_PagedAndSlim()
    {
        string tag = $"c3-{Guid.NewGuid():N}";
        (_, string owner) = await HireEmployeeAsync("c3-owner");
        (_, string outsider) = await HireEmployeeAsync("c3-outsider");
        Guid withProject = await SeedTaggedClientAsync(tag, "with-project");
        await CreateProjectForAsync(withProject, owner);
        Guid pipelineA = await SeedTaggedClientAsync(tag, "pipeline-a");
        Guid pipelineB = await SeedTaggedClientAsync(tag, "pipeline-b");

        PagedResult<ClientListItemResponse> outsiderView = await ListAsync(outsider, new GetClientsQuery(outsider, 1, 50, tag));
        Assert.Equal(2, outsiderView.TotalCount);
        Assert.DoesNotContain(outsiderView.Items, c => c.Id == withProject);

        PagedResult<ClientListItemResponse> page1 = await ListAsync(owner, new GetClientsQuery(owner, 1, 2, tag), CrmPermissions.Clients.ReadAll);
        PagedResult<ClientListItemResponse> page2 = await ListAsync(owner, new GetClientsQuery(owner, 2, 2, tag), CrmPermissions.Clients.ReadAll);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(
            new[] { withProject, pipelineA, pipelineB }.Order(),
            page1.Items.Concat(page2.Items).Select(c => c.Id).Order());
        ClientListItemResponse row = page1.Items[0];
        Assert.False(string.IsNullOrEmpty(row.Email));
        Assert.Equal("Tagged Client", row.DisplayName);

        AssertOk(await Command(owner, new SetClientActiveCommand(pipelineB, false, owner), CrmPermissions.Clients.ReadAll));
        Assert.Equal(2, (await ListAsync(owner, new GetClientsQuery(owner, 1, 50, tag), CrmPermissions.Clients.ReadAll)).TotalCount);
        Assert.Equal(pipelineB, Assert.Single((await ListAsync(owner,
            new GetClientsQuery(owner, 1, 50, tag, IsActive: false), CrmPermissions.Clients.ReadAll)).Items).Id);

        AssertError(Plain(await QueryFor<GetClientsQuery, PagedResult<ClientListItemResponse>>(owner,
            new GetClientsQuery(owner, 0, 50, tag), CrmPermissions.Clients.ReadAll)), 400);
    }

    private async Task<Guid> SeedTaggedClientAsync(string tag, string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(scope.ServiceProvider,
            $"{label}-{Guid.NewGuid():N}@example.com", address: $"{tag} {label} street", firstName: "Tagged", lastName: "Client");
        return clientId;
    }

    private async Task<PagedResult<ClientListItemResponse>> ListAsync(string userId, GetClientsQuery query, params string[] grants)
    {
        Result<PagedResult<ClientListItemResponse>> result = await QueryFor<GetClientsQuery, PagedResult<ClientListItemResponse>>(userId, query, grants);
        AssertOk(Plain(result));
        return result.Value!;
    }
}
