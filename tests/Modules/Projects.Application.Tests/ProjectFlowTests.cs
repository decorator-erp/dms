using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Teams;
using Projects.Contracts.Templates;
using Projects.Infrastructure.Persistence;
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

        await using (var scope = fixture.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var currentUser = sp.GetRequiredService<TestCurrentUser>();

            (ownerUserId, _) = await RegisterEmployeeAsync(sp, "flow-owner");
            (headUserId, headEmployeeId) = await RegisterEmployeeAsync(sp, "flow-head");

            currentUser.UserId = ownerUserId;
            currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

            var departmentId = await CreateDepartmentWithHeadAsync(sp, headEmployeeId);
            var (templateId, _) = await CreateTemplateAsync(sp, departmentId);
            var clientId = await CrmTestSeeding.SeedClientAsync(sp, ownerUserId);

            var createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
            var projectResult = await createProject.HandleAsync(new CreateProjectCommand(
                $"P-{Guid.NewGuid():N}", "New Build", "123 Main St", WorkflowType.Stage, clientId,
                StageTemplateId: templateId));
            Assert.True(projectResult.IsSuccess);
            projectId = projectResult.Value;

            var getStages = sp.GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>();
            var stages = await getStages.HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerUserId));
            Assert.True(stages.IsSuccess);
            var projectStage = Assert.Single(stages.Value!);
            stageId = projectStage.Id;
            Assert.Equal(1, projectStage.TotalTasks);

            var getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
            var tasks = await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId));
            var projectTask = Assert.Single(tasks.Value!);
            taskId = projectTask.Id;
            Assert.Equal(ProjectTaskStatus.Open, projectTask.Status);
            Assert.Equal(TaskPriority.Medium, projectTask.Priority);

            var getSubTasks = sp.GetRequiredService<IQueryHandler<GetProjectSubTasksByTaskQuery, IReadOnlyList<ProjectSubTaskResponse>>>();
            var subTasks = await getSubTasks.HandleAsync(new GetProjectSubTasksByTaskQuery(projectId, taskId, ownerUserId));
            subTaskId = Assert.Single(subTasks.Value!).Id;

            var db = sp.GetRequiredService<ProjectsDbContext>();
            var stageTeam = await db.StageTeams.AsNoTracking().SingleAsync(t => t.ProjectStageId == stageId);
            stageTeamId = stageTeam.Id;
            Assert.Equal(headEmployeeId, stageTeam.HeadId);
            Assert.Equal(departmentId, stageTeam.DepartmentId);

            var addMember = sp.GetRequiredService<ICommandHandler<AddStageTeamMemberCommand, Guid>>();
            var addMemberResult = await addMember.HandleAsync(new AddStageTeamMemberCommand(
                projectId, stageTeamId, ownerUserId, headEmployeeId, StageTeamMemberRole.Responsible));
            Assert.True(addMemberResult.IsSuccess);
        }

        // Fresh scope, as the now-team-member head - proves the task lifecycle runs under the head's
        // OWN access, not the setup-time ReadAll bypass.
        await using (var scope = fixture.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var currentUser = sp.GetRequiredService<TestCurrentUser>();
            currentUser.UserId = headUserId;

            var assign = sp.GetRequiredService<ICommandHandler<AssignProjectTaskCommand>>();
            Assert.True((await assign.HandleAsync(new AssignProjectTaskCommand(projectId, taskId, headUserId, headEmployeeId))).IsSuccess);

            var start = sp.GetRequiredService<ICommandHandler<StartProjectTaskCommand>>();
            Assert.True((await start.HandleAsync(new StartProjectTaskCommand(projectId, taskId, headUserId))).IsSuccess);

            var completeSubTask = sp.GetRequiredService<ICommandHandler<CompleteProjectSubTaskCommand>>();
            Assert.True((await completeSubTask.HandleAsync(new CompleteProjectSubTaskCommand(projectId, subTaskId, headUserId))).IsSuccess);

            var completeTask = sp.GetRequiredService<ICommandHandler<CompleteProjectTaskCommand>>();
            Assert.True((await completeTask.HandleAsync(new CompleteProjectTaskCommand(projectId, taskId, headUserId))).IsSuccess);

            var completeStage = sp.GetRequiredService<ICommandHandler<CompleteProjectStageCommand>>();
            Assert.True((await completeStage.HandleAsync(new CompleteProjectStageCommand(projectId, stageId, headUserId))).IsSuccess);

            var db = sp.GetRequiredService<ProjectsDbContext>();
            var outboxTypes = await db.Set<OutboxMessage>().AsNoTracking()
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

        await using (var scope = fixture.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var currentUser = sp.GetRequiredService<TestCurrentUser>();

            var (ownerUserId, _) = await RegisterEmployeeAsync(sp, "scope-owner");
            (memberUserId, var memberEmployeeId) = await RegisterEmployeeAsync(sp, "scope-member");
            (outsiderUserId, _) = await RegisterEmployeeAsync(sp, "scope-outsider");

            currentUser.UserId = ownerUserId;
            currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

            var departmentId = await CreateDepartmentWithHeadAsync(sp, memberEmployeeId);
            var (templateId, _) = await CreateTemplateAsync(sp, departmentId);
            var clientId = await CrmTestSeeding.SeedClientAsync(sp, ownerUserId);

            var createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
            var projectId2 = (await createProject.HandleAsync(new CreateProjectCommand(
                $"P-{Guid.NewGuid():N}", "Scope Test", "1 Test Rd", WorkflowType.Stage, clientId,
                StageTemplateId: templateId))).Value;
            projectId = projectId2;

            var db = sp.GetRequiredService<ProjectsDbContext>();
            var stageTeam = await db.StageTeams.AsNoTracking()
                .Join(db.ProjectStages.AsNoTracking().Where(s => s.ProjectId == projectId), t => t.ProjectStageId, s => s.Id, (t, _) => t)
                .SingleAsync();

            var addMember = sp.GetRequiredService<ICommandHandler<AddStageTeamMemberCommand, Guid>>();
            var addMemberResult = await addMember.HandleAsync(new AddStageTeamMemberCommand(
                projectId, stageTeam.Id, ownerUserId, memberEmployeeId, StageTeamMemberRole.Responsible));
            Assert.True(addMemberResult.IsSuccess);
        }

        await using (var scope = fixture.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var currentUser = sp.GetRequiredService<TestCurrentUser>();
            currentUser.UserId = outsiderUserId;

            var getProject = sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>();
            var result = await getProject.HandleAsync(new GetProjectQuery(projectId, outsiderUserId));

            Assert.True(result.IsFailure);
            Assert.Equal("projects.NotFound", result.Error.Code);
        }

        await using (var scope = fixture.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var currentUser = sp.GetRequiredService<TestCurrentUser>();
            currentUser.UserId = memberUserId;

            var getProject = sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>();
            var result = await getProject.HandleAsync(new GetProjectQuery(projectId, memberUserId));

            Assert.True(result.IsSuccess);
            Assert.Equal(projectId, result.Value!.Id);
        }
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"{label}-{suffix}@example.com", "P@ssw0rd123!", "Test", label, null, "employee")));
        Assert.True(registerResult.IsSuccess);
        var appUserId = registerResult.Value!;

        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var createResult = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(createResult.IsSuccess);

        return (appUserId, createResult.Value);
    }

    private static async Task<Guid> CreateDepartmentWithHeadAsync(IServiceProvider sp, Guid headEmployeeId)
    {
        var createDepartment = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        var departmentId = (await createDepartment.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        var assignHead = sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>();
        var assignResult = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, headEmployeeId));
        Assert.True(assignResult.IsSuccess);

        return departmentId;
    }

    /// <summary>One stage, one task, one subtask, one department reference - the minimal skeleton
    /// <c>CreateProjectCommandHandler</c>'s instantiation logic needs to exercise every materialized
    /// entity type once.</summary>
    private static async Task<(Guid TemplateId, Guid StageId)> CreateTemplateAsync(IServiceProvider sp, Guid departmentId)
    {
        var createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        var templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Build-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        var createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        var stageResult = await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 100m));
        Assert.True(stageResult.IsSuccess);
        var templateStageId = stageResult.Value;

        var createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        var stageTaskId = (await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 1))).Value;

        var createStageSubTask = sp.GetRequiredService<ICommandHandler<CreateStageSubTaskCommand, Guid>>();
        Assert.True((await createStageSubTask.HandleAsync(new CreateStageSubTaskCommand(stageTaskId, "Sign-off", 1))).IsSuccess);

        var addStageDepartment = sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>();
        Assert.True((await addStageDepartment.HandleAsync(new AddStageDepartmentCommand(templateStageId, departmentId))).IsSuccess);

        return (templateId, templateStageId);
    }
}
