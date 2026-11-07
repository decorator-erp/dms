using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Members;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>C7: one accessible-projects source (owner ∪ stage team ∪ member). For an employee who is only a
/// project member, the single check, the batch filter, the paged list and <c>mine=true</c> all agree; once
/// removed, all four agree again the other way.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectMemberScopeAgreementTests(ProjectsApplicationFixture fixture)
{
    private static readonly bool[] NoneOfFour = [false, false, false, false];
    private static readonly bool[] AllOfFour = [true, true, true, true];

    [Fact]
    public async Task A_member_only_employee_is_in_scope_in_all_four_and_out_of_all_four_once_removed()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "member-agree", WorkflowType.FreeStyle);
        (Guid memberEmployeeId, string memberUserId) = await HireAsync();

        Assert.Equal(NoneOfFour, await FourAsync(p.ProjectId, memberUserId));

        Guid memberId;
        await using (AsyncServiceScope scope = As(p.OfficeUserId))
        {
            Result<Guid> added = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<AddProjectMemberCommand, Guid>>()
                .HandleAsync(new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, memberEmployeeId, ProjectMemberAccess.Viewer));
            Assert.True(added.IsSuccess, added.Error.Code);
            memberId = added.Value;
        }

        Assert.Equal(AllOfFour, await FourAsync(p.ProjectId, memberUserId));

        await using (AsyncServiceScope scope = As(p.OfficeUserId))
        {
            Result removed = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<RemoveProjectMemberCommand>>()
                .HandleAsync(new RemoveProjectMemberCommand(p.ProjectId, p.OfficeUserId, memberId));
            Assert.True(removed.IsSuccess, removed.Error.Code);
        }

        Assert.Equal(NoneOfFour, await FourAsync(p.ProjectId, memberUserId));
    }

    /// <summary>[single check, batch filter, paged list, mine=true].</summary>
    private async Task<bool[]> FourAsync(Guid projectId, string userId)
    {
        await using AsyncServiceScope scope = As(userId);
        IProjectAccessGuard guard = scope.ServiceProvider.GetRequiredService<IProjectAccessGuard>();
        bool single = (await guard.EnsureProjectAccessAsync(projectId, userId, default)).IsSuccess;
        bool batch = (await guard.FilterAccessibleProjectsAsync([projectId], userId, default)).Contains(projectId);

        IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>> list = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>();
        Result<PagedResult<ProjectResponse>> all = await list.HandleAsync(new GetProjectsQuery(1, 200, userId, NameFilter: "Scope member-agree"));
        Result<PagedResult<ProjectResponse>> mine = await list.HandleAsync(new GetProjectsQuery(1, 200, userId, NameFilter: "Scope member-agree", Mine: true));

        return [single, batch, all.Value!.Items.Any(x => x.Id == projectId), mine.Value!.Items.Any(x => x.Id == projectId)];
    }

    private async Task<(Guid EmployeeId, string AppUserId)> HireAsync()
    {
        await using AsyncServiceScope seed = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(seed.ServiceProvider, $"member-agree-{Guid.NewGuid():N}@example.com");
    }

    private AsyncServiceScope As(string userId)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = userId;
        return scope;
    }
}
