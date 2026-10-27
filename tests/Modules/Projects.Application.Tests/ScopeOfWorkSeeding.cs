using BuildingBlocks.Messaging;
using Hr.Contracts.Departments;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Templates;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>One office user (a real employee, granted <see cref="ProjectsPermissions.Projects.ReadAll"/>, as
/// admin and manager are) and a project with no stages, ready for a scope of work.</summary>
internal sealed record ScopeTestProject(Guid ProjectId, string OfficeUserId, Guid OfficeEmployeeId);

/// <summary>A template stage created for a test: its id, name and the default fee it was given.</summary>
internal sealed record SeededStage(Guid Id, string Name, decimal? DefaultFee);

internal static class ScopeOfWorkSeeding
{
    public static async Task<ScopeTestProject> CreateProjectAsync(
        ProjectsApplicationFixture fixture, string label, WorkflowType workflowType = WorkflowType.Stage,
        Guid? stageTemplateId = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string userId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{Guid.NewGuid():N}@example.com", lastName: label);
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = userId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        Result<Guid> project = await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(
                $"SOW-{Guid.NewGuid():N}", $"Scope {label}", "1 Scope St", workflowType, clientId,
                StageTemplateId: stageTemplateId));
        Assert.True(project.IsSuccess, project.IsFailure ? project.Error.ToString() : null);
        return new ScopeTestProject(project.Value, userId, employeeId);
    }

    /// <summary>A template whose stages have the given default fees (null = none), in order.</summary>
    public static async Task<(Guid TemplateId, IReadOnlyList<SeededStage> Stages)> CreateTemplateAsync(
        ProjectsApplicationFixture fixture, string label, params decimal?[] fees)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid templateId = (await sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>()
            .HandleAsync(new CreateStageTemplateCommand($"{label}-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        var stages = new List<SeededStage>();
        for (int i = 0; i < fees.Length; i++)
        {
            string name = $"{label} stage {i + 1}";
            Result<Guid> stage = await sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>()
                .HandleAsync(new CreateStageCommand(templateId, name, i + 1, 10m, $"{name} description", fees[i]));
            Assert.True(stage.IsSuccess, stage.IsFailure ? stage.Error.ToString() : null);
            stages.Add(new SeededStage(stage.Value, name, fees[i]));
        }

        return (templateId, stages);
    }

    /// <summary>Adds <paramref name="taskCount"/> tasks to a template stage, each with one subtask, and links
    /// a department whose head is a fresh employee. Returns the department id.</summary>
    public static async Task<Guid> AddTasksAndDepartmentAsync(ProjectsApplicationFixture fixture, Guid stageId, int taskCount)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        for (int i = 1; i <= taskCount; i++)
        {
            Guid taskId = (await sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>()
                .HandleAsync(new CreateStageTaskCommand(stageId, $"Task {i}", i))).Value;
            Assert.True((await sp.GetRequiredService<ICommandHandler<CreateStageSubTaskCommand, Guid>>()
                .HandleAsync(new CreateStageSubTaskCommand(taskId, $"Subtask {i}", 1))).IsSuccess);
        }

        (Guid headId, _) = await HrTestSeeding.HireEmployeeAsync(sp, $"head-{Guid.NewGuid():N}@example.com", lastName: "Head");
        Guid departmentId = await CreateDepartmentAsync(sp, headId);
        Assert.True((await sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>()
            .HandleAsync(new AddStageDepartmentCommand(stageId, departmentId))).IsSuccess);
        return departmentId;
    }

    /// <summary>Links a department that has no head to a template stage.</summary>
    public static async Task AddHeadlessDepartmentAsync(ProjectsApplicationFixture fixture, Guid stageId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid departmentId = (await sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>()
            .HandleAsync(new CreateDepartmentCommand($"Headless-{Guid.NewGuid():N}"))).Value;
        Assert.True((await sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>()
            .HandleAsync(new AddStageDepartmentCommand(stageId, departmentId))).IsSuccess);
    }

    /// <summary>An office-side scope with the given user, granted project-wide read as admin and manager are.</summary>
    public static AsyncServiceScope OfficeScope(ProjectsApplicationFixture fixture, ScopeTestProject p)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OfficeUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        return scope;
    }

    private static async Task<Guid> CreateDepartmentAsync(IServiceProvider sp, Guid headEmployeeId)
    {
        Guid departmentId = (await sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>()
            .HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;
        Assert.True((await sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>()
            .HandleAsync(new AssignDepartmentHeadCommand(departmentId, headEmployeeId))).IsSuccess);
        return departmentId;
    }
}
