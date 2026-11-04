using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Projects.Contracts.Projects;
using Projects.Contracts.Teams;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>B2-be: <see cref="GetProjectDetailsQuery"/>, the project page's one-shot read. The owner and a
/// stage-team member see it; an outsider gets <c>projects.NotFound</c> from the scope decorator; an inactive
/// project reads exactly as <see cref="GetProjectQuery"/> reads it (returned, <c>IsActive</c> false).</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectDetailsTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task Owner_sees_the_project_its_stages_with_tasks_task_counts_gate_summary_and_client_name()
    {
        Seeded s = await SeedAsync("details-owner");

        ProjectDetailsResponse details = await GetAsync(s.ProjectId, s.OwnerUserId);

        Assert.Equal(s.ProjectId, details.Project.Id);
        Assert.Equal("Test Client", details.Project.ClientName);
        Assert.Equal(s.ClientId, details.Project.ClientId);
        Assert.Equal(s.OwnerEmployeeId, details.Project.OwnerId);
        Assert.Equal(2, details.Project.StageCount);
        Assert.True(details.Project.IsActive);

        Assert.Equal(2, details.Stages.Count);
        ProjectStageDetailsResponse first = details.Stages[0];
        ProjectStageDetailsResponse second = details.Stages[1];
        Assert.True(first.Order < second.Order);
        Assert.Equal(2, first.Tasks.Count);
        Assert.Single(second.Tasks);
        Assert.All(first.Tasks, t => Assert.Equal(first.Id, t.ProjectStageId));
        Assert.Equal(new[] { new TaskStatusCount(ProjectTaskStatus.Open, 2) }, first.TaskCounts);
        Assert.Equal(new[] { new TaskStatusCount(ProjectTaskStatus.Open, 1) }, second.TaskCounts);
        Assert.NotNull(first.StageId);

        // One stage gate and one gate on a task of the first stage: both count for that stage.
        Assert.Equal(new GateSummary(2, 1, 1, 0), first.Gates);
        Assert.Equal(new GateSummary(0, 0, 0, 0), second.Gates);
        Assert.Equal(new GateSummary(2, 1, 1, 0), details.Gates);

        Assert.Empty(details.UnstagedTasks);
        Assert.Equal(new[] { new TaskStatusCount(ProjectTaskStatus.Open, 3) }, details.TaskCounts);
    }

    [Fact]
    public async Task A_stage_team_member_sees_it_and_an_outsider_gets_not_found()
    {
        Seeded s = await SeedAsync("details-member");
        (Guid memberEmployeeId, string memberUserId) = await HireAsync("details-member-emp");
        (_, string outsiderUserId) = await HireAsync("details-outsider");

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            sp.GetRequiredService<TestCurrentUser>().UserId = s.OwnerUserId;
            ProjectsDbContext db = sp.GetRequiredService<ProjectsDbContext>();
            Guid teamId = await db.StageTeams.AsNoTracking()
                .Join(db.ProjectStages.AsNoTracking().Where(st => st.ProjectId == s.ProjectId), t => t.ProjectStageId, st => st.Id, (t, _) => t.Id)
                .FirstAsync();
            Result<Guid> added = await sp.GetRequiredService<ICommandHandler<AddStageTeamMemberCommand, Guid>>()
                .HandleAsync(new AddStageTeamMemberCommand(s.ProjectId, teamId, s.OwnerUserId, memberEmployeeId, StageTeamMemberRole.Contributor));
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.ToString() : null);
        }

        Assert.Equal(s.ProjectId, (await GetAsync(s.ProjectId, memberUserId)).Project.Id);

        Result<ProjectDetailsResponse> outsider = await TryGetAsync(s.ProjectId, outsiderUserId);
        Assert.True(outsider.IsFailure);
        Assert.Equal("projects.NotFound", outsider.Error.Code);

        Result<ProjectDetailsResponse> unknown = await TryGetAsync(Guid.NewGuid(), s.OwnerUserId);
        Assert.True(unknown.IsFailure);
        Assert.Equal("projects.NotFound", unknown.Error.Code);
    }

    [Fact]
    public async Task An_inactive_project_reads_as_the_single_get_reads_it()
    {
        Seeded s = await SeedAsync("details-inactive");
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = s.OwnerUserId;
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICommandHandler<SetProjectActiveCommand>>()
                .HandleAsync(new SetProjectActiveCommand(s.ProjectId, s.OwnerUserId, false))).IsSuccess);
        }

        ProjectResponse single;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = s.OwnerUserId;
            Result<ProjectResponse> get = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
                .HandleAsync(new GetProjectQuery(s.ProjectId, s.OwnerUserId));
            Assert.True(get.IsSuccess);
            single = get.Value!;
        }

        ProjectDetailsResponse details = await GetAsync(s.ProjectId, s.OwnerUserId);
        Assert.False(single.IsActive);
        Assert.False(details.Project.IsActive);
        Assert.Equal(2, details.Stages.Count);
    }

    private sealed record Seeded(Guid ProjectId, Guid ClientId, string OwnerUserId, Guid OwnerEmployeeId);

    /// <summary>A two-stage template (two tasks, then one), each stage with a headed department so a stage
    /// team exists; the project is created by an owner holding no read-all claim. A ManualCheck gate on the
    /// first stage stays pending; one on the first stage's first task is satisfied.</summary>
    private async Task<Seeded> SeedAsync(string label)
    {
        (Guid templateId, IReadOnlyList<SeededStage> stages) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, label, null, null);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, stages[0].Id, 2);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, stages[1].Id, 1);
        (Guid ownerEmployeeId, string ownerUserId) = await HireAsync(label);

        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = ownerUserId;
        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        Result<Guid> project = await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(
                $"DET-{Guid.NewGuid():N}", $"Details {label}", "1 Details St", WorkflowType.Stage, clientId,
                StageTemplateId: templateId));
        Assert.True(project.IsSuccess, project.IsFailure ? project.Error.ToString() : null);

        ProjectsDbContext db = sp.GetRequiredService<ProjectsDbContext>();
        Guid firstStageId = await db.ProjectStages.AsNoTracking()
            .Where(st => st.ProjectId == project.Value).OrderBy(st => st.Order).Select(st => st.Id).FirstAsync();
        Guid firstTaskId = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.ProjectStageId == firstStageId).OrderBy(t => t.Order).Select(t => t.Id).FirstAsync();

        Result<Guid> stageGate = await sp.GetRequiredService<ICommandHandler<CreateStageGateConditionCommand, Guid>>()
            .HandleAsync(new CreateStageGateConditionCommand(
                project.Value, firstStageId, ownerUserId, ConditionType.ManualCheck, GateTrigger.Complete, "Stage check"));
        Assert.True(stageGate.IsSuccess, stageGate.IsFailure ? stageGate.Error.ToString() : null);
        Result<Guid> taskGate = await sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>()
            .HandleAsync(new CreateTaskGateConditionCommand(
                project.Value, firstTaskId, ownerUserId, ConditionType.ManualCheck, GateTrigger.Complete, "Task check"));
        Assert.True(taskGate.IsSuccess, taskGate.IsFailure ? taskGate.Error.ToString() : null);
        Result satisfied = await sp.GetRequiredService<ICommandHandler<SatisfyGateConditionCommand>>()
            .HandleAsync(new SatisfyGateConditionCommand(project.Value, taskGate.Value, ownerUserId));
        Assert.True(satisfied.IsSuccess, satisfied.IsFailure ? satisfied.Error.ToString() : null);

        return new Seeded(project.Value, clientId, ownerUserId, ownerEmployeeId);
    }

    private async Task<(Guid EmployeeId, string UserId)> HireAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(scope.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com", lastName: label);
    }

    private async Task<ProjectDetailsResponse> GetAsync(Guid projectId, string userId)
    {
        Result<ProjectDetailsResponse> result = await TryGetAsync(projectId, userId);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value!;
    }

    private async Task<Result<ProjectDetailsResponse>> TryGetAsync(Guid projectId, string userId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = userId;
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetProjectDetailsQuery, ProjectDetailsResponse>>()
            .HandleAsync(new GetProjectDetailsQuery(projectId, userId));
    }
}
