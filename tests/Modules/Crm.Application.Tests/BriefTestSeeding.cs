using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Templates;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A real client, a real employee owner and a real project instantiated from a one-stage,
/// one-task template — everything a stage-owned design brief needs, created through each module's own
/// commands.</summary>
internal sealed record BriefTestProject(
    Guid ProjectId, Guid StageId, Guid TaskId, Guid EmployeeId, string OwnerAppUserId,
    Guid ClientId, string ClientAppUserId);

internal static class BriefTestSeeding
{
    /// <summary>Leaves the scope's <see cref="TestCurrentUser"/> as the project owner, holding
    /// <c>project.read_all</c> (the stage/task reads below are project-scoped).</summary>
    public static async Task<BriefTestProject> CreateProjectWithStageAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid clientId, string clientAppUserId) = await CrmClientSeeding.CreateClientAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);
        (Guid employeeId, string ownerAppUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"owner-{suffix}@example.com", jobTitle: "PM", firstName: "Owner");

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = ownerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        Guid templateId = (await sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>()
            .HandleAsync(new CreateStageTemplateCommand($"Brief-{suffix}", StageTemplateType.Custom))).Value;
        Guid templateStageId = (await sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>()
            .HandleAsync(new CreateStageCommand(templateId, "Design", 1, 100m))).Value;
        Assert.True((await sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>()
            .HandleAsync(new CreateStageTaskCommand(templateStageId, "Concept", 1))).IsSuccess);

        Result<Guid> projectResult = await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(
                $"P-{suffix}", "Brief Project", "123 Main St", WorkflowType.Stage, clientId,
                StageTemplateId: templateId));
        Assert.True(projectResult.IsSuccess);
        Guid projectId = projectResult.Value;

        ProjectStageResponse stage = Assert.Single((await sp
            .GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>()
            .HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerAppUserId))).Value!);
        ProjectTaskResponse task = Assert.Single((await sp
            .GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>()
            .HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerAppUserId))).Value!);

        return new BriefTestProject(projectId, stage.Id, task.Id, employeeId, ownerAppUserId, clientId, clientAppUserId);
    }
}
