using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Tasks;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>Sites/Projects review, phase 5 (P13): the task and comment lists page (optional pageNumber/pageSize,
/// opt-in since R1-5: either one given pages with the other defaulting to 1/200, within the project list's caps), in
/// a total order, so walking the pages gives every row once.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectPagingTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task P13_TaskAndCommentLists_PageWithinTheCaps_AndTheProjectListBreaksTiesById()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "p13", WorkflowType.FreeStyle);
        List<Guid> taskIds = [];
        for (int i = 0; i < 3; i++)
        {
            // The same order on each, so only the id breaks the tie.
            Result<Guid> task = await RunAsync<CreateProjectTaskCommand, Guid>(p, new CreateProjectTaskCommand(
                p.ProjectId, p.OfficeUserId, null, $"Task {i}", null, null, null, TaskPriority.Medium, 1m, 1, null));
            Assert.True(task.IsSuccess, task.IsFailure ? task.Error.ToString() : null);
            taskIds.Add(task.Value);
        }

        for (int i = 0; i < 3; i++)
        {
            Assert.True((await RunAsync<AddTaskCommentCommand, Guid>(p, new AddTaskCommentCommand(p.ProjectId, taskIds[0], p.OfficeUserId, $"Note {i}"))).IsSuccess);
        }

        IReadOnlyList<ProjectTaskResponse> tasks1 = (await QueryAsync<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>(
            p, new GetProjectTasksByProjectQuery(p.ProjectId, p.OfficeUserId, PageNumber: 1, PageSize: 2))).Value!;
        IReadOnlyList<ProjectTaskResponse> tasks2 = (await QueryAsync<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>(
            p, new GetProjectTasksByProjectQuery(p.ProjectId, p.OfficeUserId, PageNumber: 2, PageSize: 2))).Value!;
        Assert.Equal((2, 1), (tasks1.Count, tasks2.Count));
        Assert.Equal(taskIds.Order(), tasks1.Concat(tasks2).Select(t => t.Id).Order());
        Assert.Equal(3, (await QueryAsync<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>(
            p, new GetProjectTasksByProjectQuery(p.ProjectId, p.OfficeUserId))).Value!.Count);

        IReadOnlyList<TaskCommentResponse> comments1 = (await QueryAsync<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>>(
            p, new GetTaskCommentsQuery(p.ProjectId, taskIds[0], p.OfficeUserId, PageNumber: 1, PageSize: 2))).Value!;
        IReadOnlyList<TaskCommentResponse> comments2 = (await QueryAsync<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>>(
            p, new GetTaskCommentsQuery(p.ProjectId, taskIds[0], p.OfficeUserId, PageNumber: 2, PageSize: 2))).Value!;
        Assert.Equal(["Note 0", "Note 1", "Note 2"], comments1.Concat(comments2).Select(c => c.Content));

        foreach ((int page, int size) in new[] { (0, 10), (1, 0), (1, 201), (1_000_001, 10) })
        {
            Assert.Equal(400, (await QueryAsync<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>(
                p, new GetProjectTasksByProjectQuery(p.ProjectId, p.OfficeUserId, PageNumber: page, PageSize: size))).Error.Status);
            Assert.Equal(400, (await QueryAsync<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>>(
                p, new GetTaskCommentsQuery(p.ProjectId, taskIds[0], p.OfficeUserId, page, size))).Error.Status);
        }

        // Three projects with one name: one per page, each once.
        string name = $"p13-tie-{Guid.NewGuid():N}";
        List<Guid> projectIds = [];
        for (int i = 0; i < 3; i++)
        {
            Result<Guid> project = await RunAsync<CreateProjectCommand, Guid>(p, new CreateProjectCommand(
                $"T-{Guid.NewGuid():N}"[..20], name, "1 Tie St", WorkflowType.FreeStyle, await ClientOfAsync(p)));
            Assert.True(project.IsSuccess, project.IsFailure ? project.Error.ToString() : null);
            projectIds.Add(project.Value);
        }

        List<Guid> walked = [];
        for (int page = 1; page <= 3; page++)
        {
            walked.AddRange((await QueryAsync<GetProjectsQuery, PagedResult<ProjectResponse>>(
                p, new GetProjectsQuery(page, 1, p.OfficeUserId, NameFilter: name))).Value!.Items.Select(x => x.Id));
        }

        Assert.Equal(projectIds.Order(), walked.Order());
    }

    /// <summary>R1-5: paging is opt-in. With neither pageNumber nor pageSize the task and comment lists return every
    /// row, as before P13 (no silent cap at 200); with either one given they page, within the caps.</summary>
    [Fact]
    public async Task R1_5_WithoutPagingParameters_TheListsAreUnbounded()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "r1-5", WorkflowType.FreeStyle);
        Result<Guid> created = await RunAsync<CreateProjectTaskCommand, Guid>(p, new CreateProjectTaskCommand(
            p.ProjectId, p.OfficeUserId, null, "Busy task", null, null, null, TaskPriority.Medium, 1m, 1, null));
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        Guid taskId = created.Value;

        await using (AsyncServiceScope seed = fixture.CreateScope())
        {
            Infrastructure.Persistence.ProjectsDbContext db = seed.ServiceProvider.GetRequiredService<Infrastructure.Persistence.ProjectsDbContext>();
            db.TaskComments.AddRange(Enumerable.Range(0, 205).Select(i => Domain.TaskComment.Create($"Note {i}", taskId, p.OfficeEmployeeId)));
            db.ProjectTasks.AddRange(Enumerable.Range(0, 204).Select(i => Domain.ProjectTask.Create(
                p.ProjectId, $"Bulk {i}", i + 2, Domain.WorkflowType.FreeStyle, 1m, null).Value!));
            await db.SaveChangesAsync();
        }

        Assert.Equal(205, (await QueryAsync<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>(
            p, new GetProjectTasksByProjectQuery(p.ProjectId, p.OfficeUserId))).Value!.Count);
        Assert.Equal(205, (await QueryAsync<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>>(
            p, new GetTaskCommentsQuery(p.ProjectId, taskId, p.OfficeUserId))).Value!.Count);

        // Either parameter alone opts in: the other takes its default (page 1, 200 rows).
        Assert.Equal(200, (await QueryAsync<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>(
            p, new GetProjectTasksByProjectQuery(p.ProjectId, p.OfficeUserId, PageNumber: 1))).Value!.Count);
        Assert.Equal(5, (await QueryAsync<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>>(
            p, new GetTaskCommentsQuery(p.ProjectId, taskId, p.OfficeUserId, PageNumber: 2))).Value!.Count);
        Assert.Equal(10, (await QueryAsync<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>>(
            p, new GetTaskCommentsQuery(p.ProjectId, taskId, p.OfficeUserId, PageSize: 10))).Value!.Count);
    }

    private async Task<Guid> ClientOfAsync(ScopeTestProject p) =>
        (await QueryAsync<GetProjectQuery, ProjectResponse>(p, new GetProjectQuery(p.ProjectId, p.OfficeUserId))).Value!.ClientId;

    private async Task<Result<TResponse>> RunAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command) where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().Grant(ProjectsPermissions.StageTasks.Manage);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> QueryAsync<TQuery, TResponse>(ScopeTestProject p, TQuery query) where TQuery : IQuery<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>().HandleAsync(query);
    }
}
