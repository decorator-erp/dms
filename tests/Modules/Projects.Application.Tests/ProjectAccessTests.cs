using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Tasks;
using Projects.Contracts.Teams;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>Sites/Projects review, phase 5 (Projects access): who reaches a project, and at what level.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectAccessTests(ProjectsApplicationFixture fixture)
{
    private static readonly bool[] AllThree = [true, true, true];

    /// <summary>P6/NP6: a stage-team head who is on no team's member list, and a task's assignee who is on no team at
    /// all, both reach the project — through the single check, the batch filter and the paged list alike.</summary>
    [Fact]
    public async Task P6_ATeamHeadAndATaskAssignee_ReachTheProject()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("p6", 1);
        (Guid headId, string headUser) = await HireAsync("p6-head");
        (Guid assigneeId, string assigneeUser) = await HireAsync("p6-assignee");

        Guid teamId = await TeamOfAsync(stageId);
        await OfficeOkAsync(p, new ChangeStageTeamHeadCommand(p.ProjectId, teamId, p.OfficeUserId, headId));
        await OfficeOkAsync(p, new AssignProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId, assigneeId));

        Assert.Equal(AllThree, await ThreeAsync(p.ProjectId, headUser, "Scope p6"));
        Assert.Equal(AllThree, await ThreeAsync(p.ProjectId, assigneeUser, "Scope p6"));
    }

    /// <summary>[single check, batch filter, paged list].</summary>
    private async Task<bool[]> ThreeAsync(Guid projectId, string userId, string name)
    {
        await using AsyncServiceScope scope = As(userId);
        IProjectAccessGuard guard = scope.ServiceProvider.GetRequiredService<IProjectAccessGuard>();
        bool single = (await guard.EnsureProjectAccessAsync(projectId, userId, default)).IsSuccess;
        bool batch = (await guard.FilterAccessibleProjectsAsync([projectId], userId, default)).Contains(projectId);
        Result<PagedResult<ProjectResponse>> list = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>()
            .HandleAsync(new GetProjectsQuery(1, 200, userId, NameFilter: name));
        return [single, batch, list.Value!.Items.Any(x => x.Id == projectId)];
    }

    private async Task<(ScopeTestProject Project, Guid StageId, Guid[] TaskIds)> StageProjectWithTasksAsync(string label, int taskCount)
    {
        (Guid templateId, IReadOnlyList<SeededStage> stages) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, label, [null]);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, stages[0].Id, taskCount);
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, label, stageTemplateId: templateId);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        Guid stageId = await db.ProjectStages.AsNoTracking().Where(s => s.ProjectId == p.ProjectId)
            .OrderBy(s => s.Order).Select(s => s.Id).FirstAsync();
        Guid[] taskIds = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectStageId == stageId)
            .OrderBy(t => t.Order).Select(t => t.Id).ToArrayAsync();
        return (p, stageId, taskIds);
    }

    private async Task<Guid> TeamOfAsync(Guid stageId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().StageTeams.AsNoTracking()
            .Where(t => t.ProjectStageId == stageId).Select(t => t.Id).FirstAsync();
    }

    private async Task<(Guid EmployeeId, string AppUserId)> HireAsync(string label)
    {
        await using AsyncServiceScope seed = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(seed.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com");
    }

    private async Task OfficeOkAsync<TCommand>(ScopeTestProject p, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private AsyncServiceScope As(string userId)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = userId;
        return scope;
    }
}
