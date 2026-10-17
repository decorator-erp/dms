using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Teams;
using Projects.Contracts.Templates;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>
/// G5-07's own verify block: full instantiate-and-run flow; task lifecycle transitions persist and
/// publish the right events (asserted via outbox rows, not delivery — no subscriber exists until
/// G5-08); <c>ProjectScopeDecorator</c> denies a non-member and allows a member. Every actor here is a
/// REAL Identity user + HR employee (via <c>RegisterEmployeeAsync</c>), not a bare Guid — the same
/// reasoning <c>CrossModuleSmokeTests</c> (G3-01) uses: <c>IHrDirectory</c> resolution and
/// <c>ProjectAccessGuard</c>'s stage-team-membership check are exactly what's under test, so faking
/// either side would test nothing.
/// </summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectFlowTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task Instantiate_from_template_then_run_task_lifecycle_publishes_outbox_events()
    {
        Guid projectId, stageId, taskId, subTaskId, stageTeamId;
        string ownerUserId, headUserId;
        Guid headEmployeeId;

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();

            (ownerUserId, _) = await RegisterEmployeeAsync(sp, "flow-owner");
            (headUserId, headEmployeeId) = await RegisterEmployeeAsync(sp, "flow-head");

            currentUser.UserId = ownerUserId;
            currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

            Guid departmentId = await CreateDepartmentWithHeadAsync(sp, headEmployeeId);
            (Guid templateId, Guid _) = await CreateTemplateAsync(sp, departmentId);
            Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);

            ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
            Result<Guid> projectResult = await createProject.HandleAsync(new CreateProjectCommand(
                $"P-{Guid.NewGuid():N}", "New Build", "123 Main St", WorkflowType.Stage, clientId,
                StageTemplateId: templateId));
            Assert.True(projectResult.IsSuccess);
            projectId = projectResult.Value;

            IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>> getStages = sp.GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>();
            Result<IReadOnlyList<ProjectStageResponse>> stages = await getStages.HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerUserId));
            Assert.True(stages.IsSuccess);
            ProjectStageResponse projectStage = Assert.Single(stages.Value!);
            stageId = projectStage.Id;
            Assert.Equal(1, projectStage.TotalTasks);

            IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
            Result<IReadOnlyList<ProjectTaskResponse>> tasks = await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId));
            ProjectTaskResponse projectTask = Assert.Single(tasks.Value!);
            taskId = projectTask.Id;
            Assert.Equal(ProjectTaskStatus.Open, projectTask.Status);
            Assert.Equal(TaskPriority.Medium, projectTask.Priority);

            IQueryHandler<GetProjectSubTasksByTaskQuery, IReadOnlyList<ProjectSubTaskResponse>> getSubTasks = sp.GetRequiredService<IQueryHandler<GetProjectSubTasksByTaskQuery, IReadOnlyList<ProjectSubTaskResponse>>>();
            Result<IReadOnlyList<ProjectSubTaskResponse>> subTasks = await getSubTasks.HandleAsync(new GetProjectSubTasksByTaskQuery(projectId, taskId, ownerUserId));
            subTaskId = Assert.Single(subTasks.Value!).Id;

            ProjectsDbContext db = sp.GetRequiredService<ProjectsDbContext>();
            Domain.StageTeam stageTeam = await db.StageTeams.AsNoTracking().SingleAsync(t => t.ProjectStageId == stageId);
            stageTeamId = stageTeam.Id;
            Assert.Equal(headEmployeeId, stageTeam.HeadId);
            Assert.Equal(departmentId, stageTeam.DepartmentId);

            ICommandHandler<AddStageTeamMemberCommand, Guid> addMember = sp.GetRequiredService<ICommandHandler<AddStageTeamMemberCommand, Guid>>();
            Result<Guid> addMemberResult = await addMember.HandleAsync(new AddStageTeamMemberCommand(
                projectId, stageTeamId, ownerUserId, headEmployeeId, StageTeamMemberRole.Responsible));
            Assert.True(addMemberResult.IsSuccess);
        }

        // Fresh scope, as the now-team-member head - proves the task lifecycle runs under the head's
        // OWN access, not the setup-time ReadAll bypass.
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
            currentUser.UserId = headUserId;

            ICommandHandler<AssignProjectTaskCommand> assign = sp.GetRequiredService<ICommandHandler<AssignProjectTaskCommand>>();
            Assert.True((await assign.HandleAsync(new AssignProjectTaskCommand(projectId, taskId, headUserId, headEmployeeId))).IsSuccess);

            ICommandHandler<StartProjectTaskCommand> start = sp.GetRequiredService<ICommandHandler<StartProjectTaskCommand>>();
            Assert.True((await start.HandleAsync(new StartProjectTaskCommand(projectId, taskId, headUserId))).IsSuccess);

            ICommandHandler<CompleteProjectSubTaskCommand> completeSubTask = sp.GetRequiredService<ICommandHandler<CompleteProjectSubTaskCommand>>();
            Assert.True((await completeSubTask.HandleAsync(new CompleteProjectSubTaskCommand(projectId, subTaskId, headUserId))).IsSuccess);

            ICommandHandler<CompleteProjectTaskCommand> completeTask = sp.GetRequiredService<ICommandHandler<CompleteProjectTaskCommand>>();
            Assert.True((await completeTask.HandleAsync(new CompleteProjectTaskCommand(projectId, taskId, headUserId))).IsSuccess);

            ICommandHandler<CompleteProjectStageCommand> completeStage = sp.GetRequiredService<ICommandHandler<CompleteProjectStageCommand>>();
            Assert.True((await completeStage.HandleAsync(new CompleteProjectStageCommand(projectId, stageId, headUserId))).IsSuccess);

            ProjectsDbContext db = sp.GetRequiredService<ProjectsDbContext>();
            List<string> outboxTypes = await db.Set<OutboxMessage>().AsNoTracking()
                .Select(m => m.Type)
                .ToListAsync();

            Assert.Contains(outboxTypes, t => t.Contains(nameof(ProjectTaskAssignedIntegrationEvent)));
            Assert.Contains(outboxTypes, t => t.Contains(nameof(ProjectTaskStartedIntegrationEvent)));
            Assert.Contains(outboxTypes, t => t.Contains(nameof(ProjectTaskCompletedIntegrationEvent)));
            Assert.Contains(outboxTypes, t => t.Contains(nameof(ProjectStageCompletedIntegrationEvent)));
        }
    }

    [Fact]
    public async Task ProjectScopeDecorator_denies_non_member_and_allows_member()
    {
        Guid projectId;
        string memberUserId, outsiderUserId;

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();

            (string? ownerUserId, Guid _) = await RegisterEmployeeAsync(sp, "scope-owner");
            (memberUserId, Guid memberEmployeeId) = await RegisterEmployeeAsync(sp, "scope-member");
            (outsiderUserId, _) = await RegisterEmployeeAsync(sp, "scope-outsider");

            currentUser.UserId = ownerUserId;
            currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

            Guid departmentId = await CreateDepartmentWithHeadAsync(sp, memberEmployeeId);
            (Guid templateId, Guid _) = await CreateTemplateAsync(sp, departmentId);
            Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);

            ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
            Guid projectId2 = (await createProject.HandleAsync(new CreateProjectCommand(
                $"P-{Guid.NewGuid():N}", "Scope Test", "1 Test Rd", WorkflowType.Stage, clientId,
                StageTemplateId: templateId))).Value;
            projectId = projectId2;

            ProjectsDbContext db = sp.GetRequiredService<ProjectsDbContext>();
            Domain.StageTeam stageTeam = await db.StageTeams.AsNoTracking()
                .Join(db.ProjectStages.AsNoTracking().Where(s => s.ProjectId == projectId), t => t.ProjectStageId, s => s.Id, (t, _) => t)
                .SingleAsync();

            ICommandHandler<AddStageTeamMemberCommand, Guid> addMember = sp.GetRequiredService<ICommandHandler<AddStageTeamMemberCommand, Guid>>();
            Result<Guid> addMemberResult = await addMember.HandleAsync(new AddStageTeamMemberCommand(
                projectId, stageTeam.Id, ownerUserId, memberEmployeeId, StageTeamMemberRole.Responsible));
            Assert.True(addMemberResult.IsSuccess);
        }

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
            currentUser.UserId = outsiderUserId;

            IQueryHandler<GetProjectQuery, ProjectResponse> getProject = sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>();
            Result<ProjectResponse> result = await getProject.HandleAsync(new GetProjectQuery(projectId, outsiderUserId));

            Assert.True(result.IsFailure);
            Assert.Equal("projects.NotFound", result.Error.Code);
        }

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
            currentUser.UserId = memberUserId;

            IQueryHandler<GetProjectQuery, ProjectResponse> getProject = sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>();
            Result<ProjectResponse> result = await getProject.HandleAsync(new GetProjectQuery(projectId, memberUserId));

            Assert.True(result.IsSuccess);
            Assert.Equal(projectId, result.Value!.Id);
        }
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);

        return (appUserId, employeeId);
    }

    private static async Task<Guid> CreateDepartmentWithHeadAsync(IServiceProvider sp, Guid headEmployeeId)
    {
        ICommandHandler<CreateDepartmentCommand, Guid> createDepartment = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        Guid departmentId = (await createDepartment.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        ICommandHandler<AssignDepartmentHeadCommand> assignHead = sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>();
        Result assignResult = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, headEmployeeId));
        Assert.True(assignResult.IsSuccess);

        return departmentId;
    }

    /// <summary>One stage, one task, one subtask, one department reference - the minimal skeleton
    /// <c>CreateProjectCommandHandler</c>'s instantiation logic needs to exercise every materialized
    /// entity type once.</summary>
    private static async Task<(Guid TemplateId, Guid StageId)> CreateTemplateAsync(IServiceProvider sp, Guid departmentId)
    {
        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        Guid templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Build-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        ICommandHandler<CreateStageCommand, Guid> createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        Result<Guid> stageResult = await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 100m));
        Assert.True(stageResult.IsSuccess);
        Guid templateStageId = stageResult.Value;

        ICommandHandler<CreateStageTaskCommand, Guid> createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        Guid stageTaskId = (await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 1))).Value;

        ICommandHandler<CreateStageSubTaskCommand, Guid> createStageSubTask = sp.GetRequiredService<ICommandHandler<CreateStageSubTaskCommand, Guid>>();
        Assert.True((await createStageSubTask.HandleAsync(new CreateStageSubTaskCommand(stageTaskId, "Sign-off", 1))).IsSuccess);

        ICommandHandler<AddStageDepartmentCommand> addStageDepartment = sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>();
        Assert.True((await addStageDepartment.HandleAsync(new AddStageDepartmentCommand(templateStageId, departmentId))).IsSuccess);

        return (templateId, templateStageId);
    }
}
