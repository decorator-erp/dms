using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using Projects.Contracts.Projects;

namespace Finance.Application.Tests;

/// <summary>
/// The cross-module rows Finance's foreign keys demand, created once per fixture.
///
/// <para><b>The project, client and employee go through the REAL commands of the modules that own
/// them</b> — Identity's <c>IUserProvisioning</c>, CRM's <c>CreateClientCommand</c>, HR's
/// <c>CreateEmployeeCommand</c>, Projects' <c>CreateProjectCommand</c> — exactly as
/// <c>SiteTestData</c> does. Hand-writing an <c>identity.users</c> row means reproducing a dozen NOT
/// NULL columns of ASP.NET Identity's own table, which is not this module's business and would rot the
/// moment Identity changes. The chain that forces this is the FK graph working as designed:
/// <c>finance.budget_lines → projects.projects → crm.clients → identity.users</c>.</para>
///
/// <para><b>Stages and tasks are raw SQL</b>, unlike the above, for the same reason
/// <c>SiteTestData</c> writes documents that way: creating a task through Projects' real surface pulls
/// in stage templates, workflow rules and gate machinery, none of which Finance touches. A budget line
/// needs the row to exist and to name the right project and stage — that is all, and the two columns
/// this module actually reads (<c>ProjectId</c>, <c>ProjectStageId</c>) are visible right here rather
/// than being a consequence of five commands.</para>
///
/// <para>Shared per fixture rather than per test because <c>hr.employees</c> has a UNIQUE index on
/// <c>AppUserId</c>, and because every finance test needs the same scaffolding.</para>
/// </summary>
internal static class FinanceTestData
{
    internal sealed record Scope(
        Guid ProjectId, Guid StageId, Guid TaskId, Guid OtherTaskId, Guid EmployeeId, Guid ClientId,
        string AppUserId);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Scope? _scope;

    public static async Task<Scope> ScopeAsync(FinanceApplicationFixture fixture)
    {
        await Gate.WaitAsync();
        try
        {
            if (_scope is { } existing)
            {
                return existing;
            }

            await using AsyncServiceScope dbScope = fixture.CreateScope();
            IServiceProvider sp = dbScope.ServiceProvider;
            string suffix = Guid.NewGuid().ToString("N");

            (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
                sp, $"fin-client-{suffix}@example.com", address: "1 Finance Way");

            (Guid employeeId, string ownerUserId) = await HrTestSeeding.HireEmployeeAsync(
                sp, $"fin-owner-{suffix}@example.com", jobTitle: "Accountant", firstName: "Owner");

            sp.GetRequiredService<TestCurrentUser>().UserId = ownerUserId;

            Guid projectId = (await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
                .HandleAsync(new CreateProjectCommand(
                    $"F-{suffix}"[..12], "Finance Project", "12 Nasr Road", WorkflowType.Stage, clientId))).Value;

            var stageId = Guid.CreateVersion7();
            var taskId = Guid.CreateVersion7();
            var otherTaskId = Guid.CreateVersion7();

            await ExecuteAsync(fixture, $"""
                INSERT INTO projects.project_stages
                    ("Id","ProjectId","Name","Order","Status","ProgressPercent","CompletedTasks","TotalTasks",
                     "IsActive","CreatedAtUtc")
                VALUES ('{stageId}','{projectId}','Fit-out',1,1,0,0,2,true, now() AT TIME ZONE 'utc');

                INSERT INTO projects.project_tasks
                    ("Id","ProjectId","ProjectStageId","Name","Order","Status","Priority","Weight",
                     "TotalSubTasks","CompletedSubTasks","IsActive","CreatedAtUtc")
                VALUES ('{taskId}','{projectId}','{stageId}','Joinery',1,1,1,1,0,0,true, now() AT TIME ZONE 'utc');

                INSERT INTO projects.project_tasks
                    ("Id","ProjectId","ProjectStageId","Name","Order","Status","Priority","Weight",
                     "TotalSubTasks","CompletedSubTasks","IsActive","CreatedAtUtc")
                VALUES ('{otherTaskId}','{projectId}','{stageId}','Flooring',2,1,1,1,0,0,true,
                        now() AT TIME ZONE 'utc');
                """);

            _scope = new Scope(projectId, stageId, taskId, otherTaskId, employeeId, clientId, ownerUserId);
            return _scope;
        }
        finally
        {
            Gate.Release();
        }
    }

    internal sealed record Isolated(
        Guid ProjectId, Guid StageId, Guid TaskId, Guid OtherStageId, Guid OtherTaskId);

    /// <summary>
    /// A fresh project with two stages and one task each, created per call. Serves two purposes that
    /// turn out to be the same need:
    ///
    /// <para><b>For budget-line tests, it is a FOREIGN project</b> — the case a foreign key cannot
    /// catch, and the entire reason <c>CreateBudgetLineCommandHandler</c> asks <c>IProjectDirectory</c>
    /// at all. Every id is real and every F1-05 constraint is satisfied; only the relationship between
    /// them is wrong.</para>
    ///
    /// <para><b>For BOQ tests, it is an ISOLATED project</b>, and that is not optional. A BOQ snapshot
    /// aggregates every budget line, vendor invoice and signed difference on its project, so two tests
    /// sharing one project see each other's rows in their totals. The shared
    /// <see cref="ScopeAsync"/> scaffolding is fine for tests that assert on ids they created; it is
    /// wrong for anything that asserts on a SUM.</para>
    /// </summary>
    public static async Task<Isolated> IsolatedProjectAsync(FinanceApplicationFixture fixture)
    {
        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;
        string suffix = Guid.NewGuid().ToString("N");

        sp.GetRequiredService<TestCurrentUser>().UserId = (await ScopeAsync(fixture)).AppUserId;

        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
            sp, $"fin-other-{suffix}@example.com", address: "2 Other Way", firstName: "Other");

        Guid projectId = (await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(
                $"O-{suffix}"[..12], "Other Project", "9 Other St", WorkflowType.Stage, clientId))).Value;

        var stageId = Guid.CreateVersion7();
        var taskId = Guid.CreateVersion7();
        var otherStageId = Guid.CreateVersion7();
        var otherTaskId = Guid.CreateVersion7();

        await ExecuteAsync(fixture, $"""
            INSERT INTO projects.project_stages
                ("Id","ProjectId","Name","Order","Status","ProgressPercent","CompletedTasks","TotalTasks",
                 "IsActive","CreatedAtUtc")
            VALUES ('{stageId}','{projectId}','First stage',1,1,0,0,1,true, now() AT TIME ZONE 'utc'),
                   ('{otherStageId}','{projectId}','Second stage',2,1,0,0,1,true, now() AT TIME ZONE 'utc');

            INSERT INTO projects.project_tasks
                ("Id","ProjectId","ProjectStageId","Name","Order","Status","Priority","Weight",
                 "TotalSubTasks","CompletedSubTasks","IsActive","CreatedAtUtc")
            VALUES ('{taskId}','{projectId}','{stageId}','First task',1,1,1,1,0,0,true,
                    now() AT TIME ZONE 'utc'),
                   ('{otherTaskId}','{projectId}','{otherStageId}','Second task',2,1,1,1,0,0,true,
                    now() AT TIME ZONE 'utc');
            """);

        return new Isolated(projectId, stageId, taskId, otherStageId, otherTaskId);
    }

    /// <summary>Posts a ledger ActualCost directly. F1-07 owns the real posting handler; the BOQ and
    /// recalculate paths need actual costs to exist before then, and writing the row here keeps this
    /// task from depending on code written in the next one.</summary>
    public static Task PostActualCostAsync(
        FinanceApplicationFixture fixture,
        Guid projectId, Guid? projectTaskId, Guid? budgetLineId, decimal amount, Guid recordedById,
        bool inbound = false) =>
        ExecuteAsync(fixture, $"""
            INSERT INTO finance.ledger_entries
                ("Id","ProjectId","ProjectTaskId","BudgetLineId","Direction","Type","Amount","OccurredOn",
                 "RecordedById","IsActive","CreatedAtUtc")
            VALUES (gen_random_uuid(),'{projectId}',
                    {Sql(projectTaskId)},{Sql(budgetLineId)},{(inbound ? 1 : 2)},2,{amount},CURRENT_DATE,
                    '{recordedById}',true, now() AT TIME ZONE 'utc');
            """);

    /// <summary>Posts a ledger Commitment directly, for the same reason as
    /// <see cref="PostActualCostAsync"/>.</summary>
    public static Task PostCommitmentAsync(
        FinanceApplicationFixture fixture,
        Guid projectId, Guid? budgetLineId, decimal amount, Guid recordedById, bool inbound = false) =>
        ExecuteAsync(fixture, $"""
            INSERT INTO finance.ledger_entries
                ("Id","ProjectId","BudgetLineId","Direction","Type","Amount","OccurredOn",
                 "RecordedById","IsActive","CreatedAtUtc")
            VALUES (gen_random_uuid(),'{projectId}',{Sql(budgetLineId)},{(inbound ? 1 : 2)},1,{amount},
                    CURRENT_DATE,'{recordedById}',true, now() AT TIME ZONE 'utc');
            """);

    private static string Sql(Guid? id) => id is { } value ? $"'{value}'" : "NULL";

    private static async Task ExecuteAsync(FinanceApplicationFixture fixture, string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
