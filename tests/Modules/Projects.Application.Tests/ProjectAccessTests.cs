using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Members;
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

    /// <summary>P10 (C4/C5): the source × action matrix. Each source gives one effective level; a read needs Viewer,
    /// a comment (task execution) Editor, a project edit Manager. Below the level is 403
    /// <c>projects.InsufficientLevel</c>; no source at all is 404; and the level check comes before the 409 a
    /// closed project gives.</summary>
    [Fact]
    public async Task P10_EachSourceGivesItsLevel_AndEachActionNeedsItsLevel()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("p10", 2);
        Guid teamId = await TeamOfAsync(stageId);

        (_, string readAllUser) = await HireAsync("p10-readall");
        (Guid headId, string headUser) = await HireAsync("p10-head");
        (Guid viewerId, string viewerUser) = await HireAsync("p10-viewer");
        (Guid editorId, string editorUser) = await HireAsync("p10-editor");
        (Guid managerId, string managerUser) = await HireAsync("p10-manager");
        (Guid seatId, string seatUser) = await HireAsync("p10-seat");
        (Guid assigneeId, string assigneeUser) = await HireAsync("p10-assignee");
        (_, string outsiderUser) = await HireAsync("p10-outsider");

        await OfficeOkAsync(p, new ChangeStageTeamHeadCommand(p.ProjectId, teamId, p.OfficeUserId, headId));
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, viewerId, ProjectMemberAccess.Viewer));
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, editorId, ProjectMemberAccess.Editor));
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, managerId, ProjectMemberAccess.Manager));
        await OfficeOkAsync<AddStageTeamMemberCommand, Guid>(p, new AddStageTeamMemberCommand(p.ProjectId, teamId, p.OfficeUserId, seatId, StageTeamMemberRole.Contributor));
        await OfficeOkAsync(p, new AssignProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId, assigneeId));

        (string Source, string User, bool ReadAll, ProjectMemberAccess? Level)[] matrix =
        [
            ("read_all", readAllUser, true, ProjectMemberAccess.Manager),
            ("owner", p.OfficeUserId, false, ProjectMemberAccess.Manager),
            ("team head", headUser, false, ProjectMemberAccess.Manager),
            ("member Viewer", viewerUser, false, ProjectMemberAccess.Viewer),
            ("member Editor", editorUser, false, ProjectMemberAccess.Editor),
            ("member Manager", managerUser, false, ProjectMemberAccess.Manager),
            ("team seat", seatUser, false, ProjectMemberAccess.Editor),
            ("assignee", assigneeUser, false, ProjectMemberAccess.Editor),
            ("outsider", outsiderUser, false, null),
        ];

        foreach ((string source, string user, bool readAll, ProjectMemberAccess? level) in matrix)
        {
            await using AsyncServiceScope scope = As(user, readAll);
            IServiceProvider sp = scope.ServiceProvider;

            Assert.Equal(level, await sp.GetRequiredService<IProjectAccessLevelReader>().GetLevelAsync(p.ProjectId, user, default));
            IReadOnlyDictionary<Guid, ProjectMemberAccess> batch = await sp.GetRequiredService<IProjectAccessLevelReader>()
                .GetLevelsAsync([p.ProjectId, Guid.NewGuid()], user, default);
            Assert.Equal(level, batch.TryGetValue(p.ProjectId, out ProjectMemberAccess b) ? b : null);

            Result read = await sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
                .HandleAsync(new GetProjectQuery(p.ProjectId, user));
            Result comment = await sp.GetRequiredService<ICommandHandler<AddTaskCommentCommand, Guid>>()
                .HandleAsync(new AddTaskCommentCommand(p.ProjectId, tasks[0], user, $"from {source}"));
            Result edit = await sp.GetRequiredService<ICommandHandler<UpdateProjectCommand>>()
                .HandleAsync(new UpdateProjectCommand(p.ProjectId, user, Notes: $"edited by {source}"));

            Assert.Equal(Expect(level, ProjectMemberAccess.Viewer), Outcome(read));
            Assert.Equal(Expect(level, ProjectMemberAccess.Editor), Outcome(comment));
            Assert.Equal(Expect(level, ProjectMemberAccess.Manager), Outcome(edit));
        }

        // Level (403) before state (409): on a project on hold a Viewer's start is 403, an Editor's is 409.
        await OfficeOkAsync(p, new ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.InProgress));
        await OfficeOkAsync(p, new ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.OnHold));
        Assert.Equal("403 projects.InsufficientLevel", Outcome(await RunAsAsync(viewerUser, new StartProjectTaskCommand(p.ProjectId, tasks[0], viewerUser))));
        Assert.Equal("409 projects.Closed", Outcome(await RunAsAsync(editorUser, new StartProjectTaskCommand(p.ProjectId, tasks[0], editorUser))));
    }

    private static string Expect(ProjectMemberAccess? level, ProjectMemberAccess needed) =>
        level is null ? "404 projects.NotFound" : level >= needed ? "ok" : "403 projects.InsufficientLevel";

    private static string Outcome(Result result) => result.IsSuccess ? "ok" : $"{result.Error.Status} {result.Error.Code}";

    private async Task<Result> RunAsAsync<TCommand>(string userId, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = As(userId);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task OfficeOkAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command) where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<TResponse> result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
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

    private AsyncServiceScope As(string userId, bool readAll = false)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser user = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        user.UserId = userId;
        if (readAll)
        {
            user.Grant(ProjectsPermissions.Projects.ReadAll);
        }

        return scope;
    }
}
