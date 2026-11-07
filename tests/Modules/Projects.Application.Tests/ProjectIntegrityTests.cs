using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>Sites/Projects review, phase 3 (Projects data integrity): one red test per finding.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectIntegrityTests(ProjectsApplicationFixture fixture)
{
    /// <summary>P8: a second project with a code already in use is a 409, not a 500 from the unique index.</summary>
    [Fact]
    public async Task P8_ASecondProjectWithTheSameCode_IsCodeTaken()
    {
        ScopeTestProject first = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "p8-first");
        string code = await CodeOfAsync(first.ProjectId);

        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, first);
        IServiceProvider sp = scope.ServiceProvider;
        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        Result<Guid> second = await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(code, "Duplicate", "1 Dup St", WorkflowType.Stage, clientId));

        Assert.True(second.IsFailure);
        Assert.Equal("projects.CodeTaken", second.Error.Code);
        Assert.Equal(409, second.Error.Status);
    }

    /// <summary>P8: two creates racing past the pre-check — the unique index violation (IX_projects_Code) is
    /// mapped to the same 409.</summary>
    [Fact]
    public async Task P8_ARacingInsertOfTheSameCode_MapsTheIndexViolationToCodeTaken()
    {
        ScopeTestProject first = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "p8-race");
        string code = await CodeOfAsync(first.ProjectId);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        Domain.Project existing = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == first.ProjectId);
        db.Projects.Add(Domain.Project.Create(
            code, "Racer", "1 Race St", Domain.WorkflowType.Stage, existing.ClientId, existing.OwnerId));

        Result saved = await ProjectWrites.SaveAsync(db, CancellationToken.None);

        Assert.True(saved.IsFailure);
        Assert.Equal("projects.CodeTaken", saved.Error.Code);
    }

    /// <summary>P9: an edit made from a stale copy of the project (another edit committed after it was read) is a
    /// 409 ConcurrentUpdate, not a silent lost update. The stale copy is pre-tracked in the handler's own scope,
    /// so the handler's <c>FindAsync</c> returns it, as a read racing the other edit would.</summary>
    [Fact]
    public async Task P9_AnEditFromAStaleCopyOfTheProject_IsConcurrentUpdate()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "p9");

        await using AsyncServiceScope stale = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        ProjectsDbContext staleDb = stale.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        Assert.NotNull(await staleDb.Projects.FindAsync(p.ProjectId));

        await using (AsyncServiceScope other = ScopeOfWorkSeeding.OfficeScope(fixture, p))
        {
            Result first = await other.ServiceProvider.GetRequiredService<ICommandHandler<UpdateProjectCommand>>()
                .HandleAsync(new UpdateProjectCommand(p.ProjectId, p.OfficeUserId, Name: "Renamed first"));
            Assert.True(first.IsSuccess, first.IsFailure ? first.Error.ToString() : null);
        }

        Result second = await stale.ServiceProvider.GetRequiredService<ICommandHandler<UpdateProjectCommand>>()
            .HandleAsync(new UpdateProjectCommand(p.ProjectId, p.OfficeUserId, Notes: "From a stale read"));

        Assert.True(second.IsFailure);
        Assert.Equal("projects.ConcurrentUpdate", second.Error.Code);
        Assert.Equal(409, second.Error.Status);
    }

    /// <summary>P2 (C1): two tasks of one stage completed at the same time both count — the stage's counters are
    /// recounted in SQL inside each completion's transaction, under the stage row lock, never read-modify-written
    /// from a stale copy.</summary>
    [Fact]
    public async Task P2_TwoTasksOfOneStageCompletedInParallel_BothCount()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("p2-par", 2);
        foreach (Guid task in tasks)
        {
            await ReadyAsync(p, task);
        }

        Result[] completed = await Task.WhenAll(tasks.Select(t => RunAsync<CompleteProjectTaskCommand>(
            p, new CompleteProjectTaskCommand(p.ProjectId, t, p.OfficeUserId))));
        Assert.All(completed, r => Assert.True(r.IsSuccess, r.IsFailure ? r.Error.ToString() : null));

        ProjectStageResponse stage = await StageAsync(p, stageId);
        Assert.Equal(2, stage.CompletedTasks);
        Assert.Equal(2, stage.TotalTasks);
        Assert.Equal(100m, stage.ProgressPercent);
        Assert.Equal(100m, (await ProjectAsync(p)).ProgressPercentage);
    }

    /// <summary>P2/NP5 (C2): the stage counters and the project's progress (computed on read: details, single, list
    /// and <see cref="IProjectDirectory.GetProgressAsync"/>) follow complete, reopen and cancel. A cancelled task
    /// leaves the total.</summary>
    [Fact]
    public async Task P2_ProgressFollowsCompleteReopenAndCancel()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("p2-flow", 2);
        await ReadyAsync(p, tasks[0]);

        await ExpectOkAsync(p, new CompleteProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));
        await AssertProgressAsync(p, stageId, completed: 1, total: 2, percent: 50m);

        await ExpectOkAsync(p, new ReopenProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));
        await AssertProgressAsync(p, stageId, completed: 0, total: 2, percent: 0m);

        await ExpectOkAsync(p, new CancelProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId));
        await AssertProgressAsync(p, stageId, completed: 0, total: 1, percent: 0m);

        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));
        await ExpectOkAsync(p, new CompleteProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));
        await AssertProgressAsync(p, stageId, completed: 1, total: 1, percent: 100m);

        await ExpectOkAsync(p, new ReopenProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId));
        await AssertProgressAsync(p, stageId, completed: 1, total: 2, percent: 50m);
    }

    /// <summary>P2 with P9: completing a task writes nothing to the project row, so an edit made from a copy of the
    /// project read before the completion still saves (no xmin conflict).</summary>
    [Fact]
    public async Task P2_AProjectEditAfterATaskCompletes_IsNotAConflict()
    {
        (ScopeTestProject p, _, Guid[] tasks) = await StageProjectWithTasksAsync("p2-edit", 1);
        await ReadyAsync(p, tasks[0]);

        await using AsyncServiceScope editor = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Assert.NotNull(await editor.ServiceProvider.GetRequiredService<ProjectsDbContext>().Projects.FindAsync(p.ProjectId));

        await ExpectOkAsync(p, new CompleteProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));

        Result edit = await editor.ServiceProvider.GetRequiredService<ICommandHandler<UpdateProjectCommand>>()
            .HandleAsync(new UpdateProjectCommand(p.ProjectId, p.OfficeUserId, Notes: "Edited after the completion"));
        Assert.True(edit.IsSuccess, edit.IsFailure ? edit.Error.ToString() : null);
    }

    private async Task<(ScopeTestProject Project, Guid StageId, Guid[] TaskIds)> StageProjectWithTasksAsync(string label, int taskCount)
    {
        (Guid templateId, IReadOnlyList<SeededStage> stages) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, label, (decimal?)null);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, stages[0].Id, taskCount);
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, label, stageTemplateId: templateId);

        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<IReadOnlyList<ProjectTaskResponse>> tasks = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>()
            .HandleAsync(new GetProjectTasksByProjectQuery(p.ProjectId, p.OfficeUserId));
        Assert.Equal(taskCount, tasks.Value!.Count);
        return (p, Assert.Single(tasks.Value!.Select(t => t.ProjectStageId!.Value).Distinct()), [.. tasks.Value!.Select(t => t.Id)]);
    }

    /// <summary>Starts the task and completes its sub-tasks, so it can be completed.</summary>
    private async Task ReadyAsync(ScopeTestProject p, Guid taskId)
    {
        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, taskId, p.OfficeUserId));
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<IReadOnlyList<ProjectSubTaskResponse>> subTasks = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectSubTasksByTaskQuery, IReadOnlyList<ProjectSubTaskResponse>>>()
            .HandleAsync(new GetProjectSubTasksByTaskQuery(p.ProjectId, taskId, p.OfficeUserId));
        foreach (ProjectSubTaskResponse subTask in subTasks.Value!)
        {
            await ExpectOkAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, subTask.Id, p.OfficeUserId));
        }
    }

    private async Task<Result> RunAsync<TCommand>(ScopeTestProject p, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task ExpectOkAsync<TCommand>(ScopeTestProject p, TCommand command) where TCommand : ICommand
    {
        Result result = await RunAsync(p, command);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private async Task AssertProgressAsync(ScopeTestProject p, Guid stageId, int completed, int total, decimal percent)
    {
        ProjectStageResponse stage = await StageAsync(p, stageId);
        Assert.Equal((completed, total, percent), (stage.CompletedTasks, stage.TotalTasks, stage.ProgressPercent));

        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        IServiceProvider sp = scope.ServiceProvider;
        Assert.Equal(percent, (await ProjectAsync(p)).ProgressPercentage);

        Result<ProjectDetailsResponse> details = await sp.GetRequiredService<IQueryHandler<GetProjectDetailsQuery, ProjectDetailsResponse>>()
            .HandleAsync(new GetProjectDetailsQuery(p.ProjectId, p.OfficeUserId));
        Assert.Equal(percent, details.Value!.Project.ProgressPercentage);
        ProjectStageDetailsResponse detailStage = Assert.Single(details.Value!.Stages);
        Assert.Equal((completed, total, percent), (detailStage.CompletedTasks, detailStage.TotalTasks, detailStage.ProgressPercent));

        string name = (await ProjectAsync(p)).Name;
        Result<PagedResult<ProjectResponse>> list = await sp.GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>()
            .HandleAsync(new GetProjectsQuery(1, 200, p.OfficeUserId, NameFilter: name));
        Assert.Equal(percent, Assert.Single(list.Value!.Items, x => x.Id == p.ProjectId).ProgressPercentage);

        IReadOnlyList<ProjectProgressSummary> progress = await sp.GetRequiredService<IProjectDirectory>()
            .GetProgressAsync([p.ProjectId], CancellationToken.None);
        Assert.Equal(percent, Assert.Single(progress).ProgressPercentage);
    }

    private async Task<ProjectStageResponse> StageAsync(ScopeTestProject p, Guid stageId)
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<IReadOnlyList<ProjectStageResponse>> stages = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>()
            .HandleAsync(new GetProjectStagesByProjectQuery(p.ProjectId, p.OfficeUserId));
        return Assert.Single(stages.Value!, s => s.Id == stageId);
    }

    private async Task<ProjectResponse> ProjectAsync(ScopeTestProject p)
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<ProjectResponse> project = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
            .HandleAsync(new GetProjectQuery(p.ProjectId, p.OfficeUserId));
        return project.Value!;
    }

    private async Task<string> CodeOfAsync(Guid projectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        return await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Code).SingleAsync();
    }
}
