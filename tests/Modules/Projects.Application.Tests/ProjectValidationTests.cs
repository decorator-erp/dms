using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Teams;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>Sites/Projects review, phase 5 (P7/NP4): out-of-range input is a 400 from the validators, and the enum
/// columns refuse an out-of-range value in the database too.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectValidationTests(ProjectsApplicationFixture fixture)
{
    private static readonly string Long2001 = new('x', 2001);

    /// <summary>P7: lengths and enums on project create and update, the reject reasons, team roles, gate
    /// trigger/type/approval type, the status change, and the list filters.</summary>
    [Fact]
    public async Task P7_OutOfRangeInput_IsAValidationError()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "p7", WorkflowType.FreeStyle);
        Guid id = p.ProjectId;
        string u = p.OfficeUserId;
        var any = Guid.NewGuid();

        List<(string Case, Result Result)> results =
        [
            ("create description", await RunAsync<CreateProjectCommand, Guid>(p, Create(description: Long2001))),
            ("create notes", await RunAsync<CreateProjectCommand, Guid>(p, Create(notes: Long2001))),
            ("create address", await RunAsync<CreateProjectCommand, Guid>(p, Create(address: new string('a', 501)))),
            ("create workflow", await RunAsync<CreateProjectCommand, Guid>(p, Create(workflow: (WorkflowType)9))),
            ("update empty name", await RunAsync(p, new UpdateProjectCommand(id, u, Name: ""))),
            ("update long name", await RunAsync(p, new UpdateProjectCommand(id, u, Name: new string('n', 201)))),
            ("update address", await RunAsync(p, new UpdateProjectCommand(id, u, Address: new string('a', 501)))),
            ("update description", await RunAsync(p, new UpdateProjectCommand(id, u, Description: Long2001))),
            ("update notes", await RunAsync(p, new UpdateProjectCommand(id, u, Notes: Long2001))),
            ("status", await RunAsync(p, new ChangeProjectStatusCommand(id, u, (ProjectStatus)9))),
            ("task reject reason", await RunAsync(p, new RejectProjectTaskCommand(id, any, u, Long2001))),
            ("stage reject reason", await RunAsync(p, new RejectProjectStageCommand(id, any, u, Long2001))),
            ("team member role", await RunAsync<AddStageTeamMemberCommand, Guid>(p, new AddStageTeamMemberCommand(id, any, u, any, (StageTeamMemberRole)9))),
            ("team role change", await RunAsync(p, new ChangeStageTeamMemberRoleCommand(id, any, u, (StageTeamMemberRole)9))),
            ("stage gate trigger", await RunAsync<CreateStageGateConditionCommand, Guid>(p, new CreateStageGateConditionCommand(
                id, any, u, ConditionType.ManualCheck, (GateTrigger)9, "Gate"))),
            ("stage gate type", await RunAsync<CreateStageGateConditionCommand, Guid>(p, new CreateStageGateConditionCommand(
                id, any, u, (ConditionType)99, GateTrigger.Complete, "Gate"))),
            ("stage gate approval type", await RunAsync<CreateStageGateConditionCommand, Guid>(p, new CreateStageGateConditionCommand(
                id, any, u, ConditionType.Approval, GateTrigger.Complete, "Gate", ApprovalType: (ApprovalType)9))),
            ("task gate trigger", await RunAsync<CreateTaskGateConditionCommand, Guid>(p, new CreateTaskGateConditionCommand(
                id, any, u, ConditionType.ManualCheck, (GateTrigger)9, "Gate"))),
            ("task gate approval type", await RunAsync<CreateTaskGateConditionCommand, Guid>(p, new CreateTaskGateConditionCommand(
                id, any, u, ConditionType.Approval, GateTrigger.Complete, "Gate", ApprovalType: (ApprovalType)9))),
            ("name filter", await QueryAsync<GetProjectsQuery, PagedResult<ProjectResponse>>(p, new GetProjectsQuery(1, 20, u, NameFilter: new string('f', 201)))),
            ("task status filter", await QueryAsync<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>(
                p, new GetProjectTasksByProjectQuery(id, u, Status: (ProjectTaskStatus)99))),
        ];

        List<string> notRejected = [.. results.Where(r => r.Result.IsSuccess || r.Result.Error.Status != 400)
            .Select(r => $"{r.Case}: {(r.Result.IsSuccess ? "ok" : $"{r.Result.Error.Status} {r.Result.Error.Code}")}")];
        Assert.True(notRejected.Count == 0, "expected 400 for: " + string.Join("; ", notRejected));
    }

    /// <summary>R1-12: <c>stage_teams."HeadId"</c> is read on every access check (a team head is Manager on the
    /// project) and by its foreign key's checks, so it leads an index.</summary>
    [Fact]
    public async Task R1_12_StageTeamHeadId_IsIndexed()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'projects' AND tablename = 'stage_teams' AND indexdef LIKE '%("HeadId")%'
            """, connection);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    /// <summary>NP4: every enum column has a CHECK constraint naming its range, and it bites.</summary>
    [Fact]
    public async Task NP4_EveryEnumColumn_HasACheckConstraint_ThatRefusesAnOutOfRangeValue()
    {
        string[] expected =
        [
            "CK_gate_conditions_ApprovalType", "CK_gate_conditions_ConditionType", "CK_gate_conditions_Status",
            "CK_gate_conditions_Trigger", "CK_project_members_Access", "CK_project_stages_Status",
            "CK_project_tasks_Priority", "CK_project_tasks_Status", "CK_projects_Status", "CK_projects_WorkflowType",
            "CK_scopes_of_work_Status", "CK_stage_team_members_Role", "CK_stage_templates_Type",
        ];

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        List<string> names = [];
        await using (var list = new NpgsqlCommand(
            "SELECT conname FROM pg_constraint WHERE contype = 'c' AND connamespace = 'projects'::regnamespace", connection))
        await using (NpgsqlDataReader reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }

        Assert.Empty(expected.Except(names));

        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "np4", WorkflowType.FreeStyle);
        await using var bad = new NpgsqlCommand($"""UPDATE projects.projects SET "Status" = 9 WHERE "Id" = '{p.ProjectId}'""", connection);
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => bad.ExecuteNonQueryAsync());
        Assert.Equal((PostgresErrorCodes.CheckViolation, "CK_projects_Status"), (refused.SqlState, refused.ConstraintName));
    }

    private static CreateProjectCommand Create(
        string? description = null, string? notes = null, string address = "1 Valid St", WorkflowType workflow = WorkflowType.FreeStyle) =>
        new($"P7-{Guid.NewGuid():N}"[..20], "Valid", address, workflow, Guid.NewGuid(), description, notes);

    private async Task<Result> RunAsync<TCommand>(ScopeTestProject p, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Result> RunAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command) where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task<Result> QueryAsync<TQuery, TResponse>(ScopeTestProject p, TQuery query) where TQuery : IQuery<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>().HandleAsync(query);
    }
}
