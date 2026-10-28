using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>FINDING-P1-1 (D-14): the project list applies the same access rule as the guard — the owner sees
/// their own project with no stage team and no <c>read_all</c>; a stranger doesn't.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectOwnerListTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task TheOwner_SeesTheirProject_InTheList_WithoutATeamOrReadAll()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "owner-list", WorkflowType.FreeStyle);
        string stranger;
        await using (AsyncServiceScope seed = fixture.CreateScope())
        {
            (_, stranger) = await HrTestSeeding.HireEmployeeAsync(seed.ServiceProvider, $"stranger-{Guid.NewGuid():N}@example.com");
        }

        Assert.Contains(await ListAsync(p.OfficeUserId), x => x.Id == p.ProjectId);
        Assert.DoesNotContain(await ListAsync(stranger), x => x.Id == p.ProjectId);
    }

    private async Task<IReadOnlyList<ProjectResponse>> ListAsync(string userId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = userId;
        Result<PagedResult<ProjectResponse>> list = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>()
            .HandleAsync(new GetProjectsQuery(1, 200, userId, NameFilter: "Scope owner-list"));
        Assert.True(list.IsSuccess);
        return list.Value!.Items;
    }
}
