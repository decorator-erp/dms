using BuildingBlocks.Messaging;
using Hr.Contracts.Departments;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Teams;
using Projects.Contracts.Templates;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A real client, a real employee owner and a real project instantiated from a template with
/// one task per stage — everything a stage-owned design brief needs, created through each module's own
/// commands. <see cref="StageId"/>/<see cref="TaskId"/> are the first stage's; <see cref="StageIds"/>/
/// <see cref="TaskIds"/> are all of them, in stage order (task i belongs to stage i).</summary>
internal sealed record BriefTestProject(
    Guid ProjectId, Guid StageId, Guid TaskId, Guid EmployeeId, string OwnerAppUserId,
    Guid ClientId, string ClientAppUserId, IReadOnlyList<Guid> StageIds, IReadOnlyList<Guid> TaskIds);

internal static class BriefTestSeeding
{
    /// <summary>Leaves the scope's <see cref="TestCurrentUser"/> as the project owner, holding
    /// <c>project.read_all</c> (the stage/task reads below are project-scoped). With
    /// <paramref name="departmentHeadEmployeeId"/>, every template stage gets a department headed by
    /// that employee, so the project's stages get a stage team (see <see cref="AddStageTeamMemberAsync"/>).</summary>
    public static async Task<BriefTestProject> CreateProjectWithStageAsync(
        IServiceProvider sp, string label, int stageCount = 1, Guid? departmentHeadEmployeeId = null)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid clientId, string clientAppUserId) = await CrmClientSeeding.CreateClientAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);
        (Guid employeeId, string ownerAppUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"owner-{suffix}@example.com", jobTitle: "PM", firstName: "Owner");

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = ownerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        Guid? departmentId = null;
        if (departmentHeadEmployeeId is { } headId)
        {
            departmentId = (await sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>()
                .HandleAsync(new CreateDepartmentCommand($"Dept-{suffix}"))).Value;
            Assert.True((await sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>()
                .HandleAsync(new AssignDepartmentHeadCommand(departmentId.Value, headId))).IsSuccess);
        }

        Guid templateId = (await sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>()
            .HandleAsync(new CreateStageTemplateCommand($"Brief-{suffix}", StageTemplateType.Custom))).Value;
        for (int order = 1; order <= stageCount; order++)
        {
            Guid templateStageId = (await sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>()
                .HandleAsync(new CreateStageCommand(templateId, $"Stage {order}", order, 100m / stageCount))).Value;
            Assert.True((await sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>()
                .HandleAsync(new CreateStageTaskCommand(templateStageId, $"Task {order}", 1))).IsSuccess);
            if (departmentId is { } dept)
            {
                Assert.True((await sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>()
                    .HandleAsync(new AddStageDepartmentCommand(templateStageId, dept))).IsSuccess);
            }
        }

        Result<Guid> projectResult = await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(
                $"P-{suffix}", "Brief Project", "123 Main St", WorkflowType.Stage, clientId,
                StageTemplateId: templateId));
        Assert.True(projectResult.IsSuccess, projectResult.Error?.Description);
        Guid projectId = projectResult.Value;

        List<ProjectStageResponse> stages = [.. (await sp
            .GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>()
            .HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerAppUserId))).Value!.OrderBy(s => s.Order)];
        IReadOnlyList<ProjectTaskResponse> tasks = (await sp
            .GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>()
            .HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerAppUserId))).Value!;
        Assert.Equal(stageCount, stages.Count);
        List<Guid> taskIds = [.. stages.Select(s => tasks.Single(t => t.ProjectStageId == s.Id).Id)];

        return new BriefTestProject(
            projectId, stages[0].Id, taskIds[0], employeeId, ownerAppUserId, clientId, clientAppUserId,
            [.. stages.Select(s => s.Id)], taskIds);
    }

    /// <summary>Puts <paramref name="memberEmployeeId"/> on the stage team of <paramref name="stageId"/>
    /// (created at instantiation because the template stage carried a department). The team id has no
    /// Contracts lookup by stage, so it is read straight from <c>projects.stage_teams</c>.</summary>
    public static async Task AddStageTeamMemberAsync(
        IServiceProvider sp, string connectionString, BriefTestProject p, Guid stageId, Guid memberEmployeeId)
    {
        Guid stageTeamId;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """SELECT "Id" FROM projects.stage_teams WHERE "ProjectStageId" = @stageId""";
            command.Parameters.AddWithValue("stageId", stageId);
            stageTeamId = (Guid)(await command.ExecuteScalarAsync())!;
        }

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OwnerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        Result<Guid> added = await sp.GetRequiredService<ICommandHandler<AddStageTeamMemberCommand, Guid>>()
            .HandleAsync(new AddStageTeamMemberCommand(
                p.ProjectId, stageTeamId, p.OwnerAppUserId, memberEmployeeId, StageTeamMemberRole.Responsible));
        Assert.True(added.IsSuccess, added.Error?.Description);
    }
}
