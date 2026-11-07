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
using Xunit.Abstractions;

namespace Projects.Application.Tests;

/// <summary><c>MyLevel</c>: the caller's own level on the project, on the single get, the paged list and the details,
/// read from <see cref="IProjectAccessLevelReader"/> — the reader the scope decorator enforces with.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectMyLevelTests(ProjectsApplicationFixture fixture, ITestOutputHelper output)
{
    /// <summary>L1: the owner reads Manager and a Viewer member reads Viewer, on all three reads.</summary>
    [Fact]
    public async Task L1_GetListAndDetails_CarryTheCallersLevel()
    {
        string label = Label("l1");
        (ScopeTestProject p, _, _) = await StageProjectWithTasksAsync(label, 1);
        (Guid viewerId, string viewerUser) = await HireAsync("l1-viewer");
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, viewerId, ProjectMemberAccess.Viewer));

        Assert.Equal(Three(ProjectMemberAccess.Manager), await MyLevelsAsync(p.ProjectId, p.OfficeUserId, label));
        Assert.Equal(Three(ProjectMemberAccess.Viewer), await MyLevelsAsync(p.ProjectId, viewerUser, label));
    }

    /// <summary>L2: the list reads the page's levels with one <see cref="IProjectAccessLevelReader.GetLevelsAsync"/> call,
    /// so a page of one row and a page of fifty issue the same number of queries. The caller is the owner without
    /// <c>read_all</c>, so both the scope filter and the reader's own statement run.</summary>
    [Fact]
    public async Task L2_TheListReadsLevelsOncePerPage_OneRowAndFiftyRowsIssueTheSameQueries()
    {
        string label = Label("l2");
        string ownerUser = await CreateOwnedProjectsAsync(label, 50);

        await CountListQueriesAsync(ownerUser, label, 1); // warm-up
        int one = await CountListQueriesAsync(ownerUser, label, 1);
        int fifty = await CountListQueriesAsync(ownerUser, label, 50);
        output.WriteLine($"GET /api/projects queries: 1 row = {one}, 50 rows = {fifty}");

        Assert.Equal(one, fifty);
    }

    /// <summary>L3: each source of <see cref="IProjectAccessLevelReader"/> gives its own <c>MyLevel</c>, the same on the
    /// get, the list and the details; an outsider gets no read at all (404 on get and details, absent from the list).</summary>
    [Fact]
    public async Task L3_EachLevelSource_GivesItsMyLevel_OnGetListAndDetails()
    {
        Sources s = await SeedSourcesAsync("l3-src");

        foreach (Source source in s.All)
        {
            ProjectMemberAccess?[] actual = await MyLevelsAsync(s.Project.ProjectId, source.User, s.Label, source.ReadAll);
            Assert.True(
                Three(source.Level).AsEnumerable().SequenceEqual(actual),
                $"{source.Name}: expected {source.Level?.ToString() ?? "no read"} on get, list and details, got [{string.Join(", ", actual)}]");
        }
    }

    /// <summary>L3: a caller with several sources reads the highest of them.</summary>
    [Fact]
    public async Task L3_TheHighestSourceWins()
    {
        Sources s = await SeedSourcesAsync("l3-max");
        ScopeTestProject p = s.Project;

        (Guid viewerHeadId, string viewerHead) = await HireAsync("l3-viewer-head");
        (Guid viewerAssigneeId, string viewerAssignee) = await HireAsync("l3-viewer-assignee");
        (Guid editorSeatId, string editorSeat) = await HireAsync("l3-editor-seat");
        (Guid viewerReadAllId, string viewerReadAll) = await HireAsync("l3-viewer-readall");
        (Guid managerSeatId, string managerSeat) = await HireAsync("l3-manager-seat");
        foreach (Guid id in new[] { viewerHeadId, viewerAssigneeId, viewerReadAllId })
        {
            await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, id, ProjectMemberAccess.Viewer));
        }

        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, editorSeatId, ProjectMemberAccess.Editor));
        await OfficeOkAsync(p, new ChangeStageTeamHeadCommand(p.ProjectId, s.TeamId, p.OfficeUserId, viewerHeadId));
        await OfficeOkAsync(p, new AssignProjectTaskCommand(p.ProjectId, s.TaskIds[1], p.OfficeUserId, viewerAssigneeId));
        await OfficeOkAsync<AddStageTeamMemberCommand, Guid>(p, new AddStageTeamMemberCommand(p.ProjectId, s.TeamId, p.OfficeUserId, editorSeatId, StageTeamMemberRole.Contributor));

        // The member row is the highest source here: a Manager row must not be lowered by an Editor-level seat.
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, managerSeatId, ProjectMemberAccess.Manager));
        await OfficeOkAsync<AddStageTeamMemberCommand, Guid>(p, new AddStageTeamMemberCommand(p.ProjectId, s.TeamId, p.OfficeUserId, managerSeatId, StageTeamMemberRole.Contributor));

        Assert.Equal(Three(ProjectMemberAccess.Manager), await MyLevelsAsync(p.ProjectId, viewerHead, s.Label));
        Assert.Equal(Three(ProjectMemberAccess.Editor), await MyLevelsAsync(p.ProjectId, viewerAssignee, s.Label));
        Assert.Equal(Three(ProjectMemberAccess.Editor), await MyLevelsAsync(p.ProjectId, editorSeat, s.Label));
        Assert.Equal(Three(ProjectMemberAccess.Manager), await MyLevelsAsync(p.ProjectId, managerSeat, s.Label));
        Assert.Equal(Three(ProjectMemberAccess.Manager), await MyLevelsAsync(p.ProjectId, viewerReadAll, s.Label, readAll: true));
    }

    /// <summary>L3 (C3): for every source, the project is in the list exactly when
    /// <see cref="IProjectAccessLevelReader.GetLevelsAsync"/> gives it a level, and with that level.</summary>
    [Fact]
    public async Task L3_TheListHoldsAProjectExactlyWhenTheReaderGivesItALevel()
    {
        Sources s = await SeedSourcesAsync("l3-agree");

        foreach (Source source in s.All)
        {
            await using AsyncServiceScope scope = As(source.User, source.ReadAll);
            IServiceProvider sp = scope.ServiceProvider;
            IReadOnlyDictionary<Guid, ProjectMemberAccess> levels = await sp.GetRequiredService<IProjectAccessLevelReader>()
                .GetLevelsAsync([s.Project.ProjectId], source.User, default);
            Result<PagedResult<ProjectResponse>> list = await sp.GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>()
                .HandleAsync(new GetProjectsQuery(1, 200, source.User, NameFilter: $"Scope {s.Label}"));

            ProjectMemberAccess? fromReader = levels.TryGetValue(s.Project.ProjectId, out ProjectMemberAccess l) ? l : null;
            ProjectMemberAccess? fromList = list.Value!.Items.SingleOrDefault(x => x.Id == s.Project.ProjectId)?.MyLevel;
            Assert.True(fromReader == fromList, $"{source.Name}: reader {fromReader}, list {fromList}");
        }
    }

    /// <summary>L3 (C5): <c>MyLevel</c> predicts the scope decorator. From a scan of the markers,
    /// <see cref="AddTaskCommentCommand"/> is <see cref="IRequiresProjectEditor"/> and <see cref="UpdateProjectCommand"/>
    /// is <see cref="IRequiresProjectManager"/>: each caller is allowed exactly when its <c>MyLevel</c> reaches the
    /// command's level, and is otherwise 403 <c>projects.InsufficientLevel</c>. A Viewer reads, and is 403 on the
    /// Editor command.</summary>
    [Fact]
    public async Task L3_MyLevelPredictsTheDecorator_OnAnEditorAndAManagerCommand()
    {
        Assert.True(typeof(IRequiresProjectEditor).IsAssignableFrom(typeof(AddTaskCommentCommand)));
        Assert.False(typeof(IRequiresProjectManager).IsAssignableFrom(typeof(AddTaskCommentCommand)));
        Assert.True(typeof(IRequiresProjectManager).IsAssignableFrom(typeof(UpdateProjectCommand)));

        Sources s = await SeedSourcesAsync("l3-403");
        Guid projectId = s.Project.ProjectId;

        foreach (Source source in s.All.Where(x => x.Level is not null))
        {
            await using AsyncServiceScope scope = As(source.User, source.ReadAll);
            IServiceProvider sp = scope.ServiceProvider;
            Result<ProjectResponse> read = await sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
                .HandleAsync(new GetProjectQuery(projectId, source.User));
            Assert.True(read.IsSuccess, $"{source.Name}: the read is allowed");
            ProjectMemberAccess myLevel = read.Value!.MyLevel;

            Result comment = await sp.GetRequiredService<ICommandHandler<AddTaskCommentCommand, Guid>>()
                .HandleAsync(new AddTaskCommentCommand(projectId, s.TaskIds[0], source.User, $"from {source.Name}"));
            Result edit = await sp.GetRequiredService<ICommandHandler<UpdateProjectCommand>>()
                .HandleAsync(new UpdateProjectCommand(projectId, source.User, Notes: $"edited by {source.Name}"));

            Assert.True(Predict(myLevel, ProjectMemberAccess.Editor) == Outcome(comment), $"{source.Name} ({myLevel}) comment: {Outcome(comment)}");
            Assert.True(Predict(myLevel, ProjectMemberAccess.Manager) == Outcome(edit), $"{source.Name} ({myLevel}) edit: {Outcome(edit)}");
        }

        Source viewer = s.All.Single(x => x.Name == "member Viewer");
        await using (AsyncServiceScope scope = As(viewer.User))
        {
            IServiceProvider sp = scope.ServiceProvider;
            Result<ProjectResponse> read = await sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
                .HandleAsync(new GetProjectQuery(projectId, viewer.User));
            Assert.Equal(ProjectMemberAccess.Viewer, read.Value!.MyLevel);
            Result comment = await sp.GetRequiredService<ICommandHandler<AddTaskCommentCommand, Guid>>()
                .HandleAsync(new AddTaskCommentCommand(projectId, s.TaskIds[0], viewer.User, "a viewer's comment"));
            Assert.Equal("403 projects.InsufficientLevel", Outcome(comment));
        }
    }

    // ---- helpers ----

    private sealed record Source(string Name, string User, bool ReadAll, ProjectMemberAccess? Level);

    private sealed record Sources(ScopeTestProject Project, string Label, Guid TeamId, Guid[] TaskIds, IReadOnlyList<Source> All);

    /// <summary>A one-stage project with two tasks and one caller per level source, each holding that source only.</summary>
    private async Task<Sources> SeedSourcesAsync(string prefix)
    {
        string label = Label(prefix);
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync(label, 2);
        Guid teamId = await TeamOfAsync(stageId);

        (_, string readAllUser) = await HireAsync($"{prefix}-readall");
        (Guid headId, string headUser) = await HireAsync($"{prefix}-head");
        (Guid viewerId, string viewerUser) = await HireAsync($"{prefix}-viewer");
        (Guid editorId, string editorUser) = await HireAsync($"{prefix}-editor");
        (Guid managerId, string managerUser) = await HireAsync($"{prefix}-manager");
        (Guid seatId, string seatUser) = await HireAsync($"{prefix}-seat");
        (Guid assigneeId, string assigneeUser) = await HireAsync($"{prefix}-assignee");
        (_, string outsiderUser) = await HireAsync($"{prefix}-outsider");

        await OfficeOkAsync(p, new ChangeStageTeamHeadCommand(p.ProjectId, teamId, p.OfficeUserId, headId));
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, viewerId, ProjectMemberAccess.Viewer));
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, editorId, ProjectMemberAccess.Editor));
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, managerId, ProjectMemberAccess.Manager));
        await OfficeOkAsync<AddStageTeamMemberCommand, Guid>(p, new AddStageTeamMemberCommand(p.ProjectId, teamId, p.OfficeUserId, seatId, StageTeamMemberRole.Contributor));
        await OfficeOkAsync(p, new AssignProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId, assigneeId));

        Source[] all =
        [
            new("read_all", readAllUser, true, ProjectMemberAccess.Manager),
            new("owner", p.OfficeUserId, false, ProjectMemberAccess.Manager),
            new("team head", headUser, false, ProjectMemberAccess.Manager),
            new("member Viewer", viewerUser, false, ProjectMemberAccess.Viewer),
            new("member Editor", editorUser, false, ProjectMemberAccess.Editor),
            new("member Manager", managerUser, false, ProjectMemberAccess.Manager),
            new("team member", seatUser, false, ProjectMemberAccess.Editor),
            new("assignee", assigneeUser, false, ProjectMemberAccess.Editor),
            new("outsider", outsiderUser, false, null),
        ];
        return new Sources(p, label, teamId, tasks, all);
    }

    private static string Predict(ProjectMemberAccess myLevel, ProjectMemberAccess needed) =>
        myLevel >= needed ? "ok" : "403 projects.InsufficientLevel";

    private static string Outcome(Result result) => result.IsSuccess ? "ok" : $"{result.Error.Status} {result.Error.Code}";

    private async Task<Guid> TeamOfAsync(Guid stageId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().StageTeams.AsNoTracking()
            .Where(t => t.ProjectStageId == stageId).Select(t => t.Id).FirstAsync();
    }

    private async Task OfficeOkAsync<TCommand>(ScopeTestProject p, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    /// <summary>The list's database commands for one page of <paramref name="pageSize"/> rows, every row checked to carry
    /// the owner's Manager level.</summary>
    private async Task<int> CountListQueriesAsync(string userId, string label, int pageSize)
    {
        await using AsyncServiceScope scope = As(userId);
        IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>> list =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>();
        DbCommandCounter.Counts counts = DbCommandCounter.Begin();
        Result<PagedResult<ProjectResponse>> page;
        try
        {
            page = await list.HandleAsync(new GetProjectsQuery(1, pageSize, userId, NameFilter: $"Scope {label}"));
        }
        finally
        {
            DbCommandCounter.End();
        }

        Assert.True(page.IsSuccess, page.IsFailure ? page.Error.ToString() : null);
        Assert.Equal(pageSize, page.Value!.Items.Count);
        Assert.All(page.Value.Items, x => Assert.Equal(ProjectMemberAccess.Manager, x.MyLevel));
        return counts.Queries;
    }

    /// <summary><paramref name="count"/> projects named "Scope {label} nn", all owned by one fresh employee; returns that
    /// employee's user id.</summary>
    private async Task<string> CreateOwnedProjectsAsync(string label, int count)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (_, string userId) = await HrTestSeeding.HireEmployeeAsync(sp, $"{label}-owner-{Guid.NewGuid():N}@example.com");
        sp.GetRequiredService<TestCurrentUser>().UserId = userId;
        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        ICommandHandler<CreateProjectCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        for (int i = 0; i < count; i++)
        {
            Result<Guid> project = await create.HandleAsync(new CreateProjectCommand(
                $"L2-{Guid.NewGuid():N}", $"Scope {label} {i:D2}", "1 Level St", WorkflowType.FreeStyle, clientId));
            Assert.True(project.IsSuccess, project.IsFailure ? project.Error.ToString() : null);
        }

        return userId;
    }

    private static string Label(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static ProjectMemberAccess?[] Three(ProjectMemberAccess? level) => [level, level, level];

    /// <summary>[get, list, details] — null where the read does not return the project.</summary>
    private async Task<ProjectMemberAccess?[]> MyLevelsAsync(Guid projectId, string userId, string label, bool readAll = false)
    {
        await using AsyncServiceScope scope = As(userId, readAll);
        IServiceProvider sp = scope.ServiceProvider;
        Result<ProjectResponse> get = await sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
            .HandleAsync(new GetProjectQuery(projectId, userId));
        Result<PagedResult<ProjectResponse>> list = await sp.GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>()
            .HandleAsync(new GetProjectsQuery(1, 200, userId, NameFilter: $"Scope {label}"));
        Result<ProjectDetailsResponse> details = await sp.GetRequiredService<IQueryHandler<GetProjectDetailsQuery, ProjectDetailsResponse>>()
            .HandleAsync(new GetProjectDetailsQuery(projectId, userId));
        Assert.True(list.IsSuccess, list.IsFailure ? list.Error.ToString() : null);

        return
        [
            get.IsSuccess ? get.Value!.MyLevel : null,
            list.Value!.Items.SingleOrDefault(x => x.Id == projectId)?.MyLevel,
            details.IsSuccess ? details.Value!.MyLevel : null,
        ];
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

    private async Task<(Guid EmployeeId, string AppUserId)> HireAsync(string label)
    {
        await using AsyncServiceScope seed = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(seed.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com");
    }

    private async Task OfficeOkAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command) where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<TResponse> result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
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
