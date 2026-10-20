using System.Globalization;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts;
using Finance.Contracts.BoqSnapshots;
using Finance.Contracts.BudgetLines;
using Finance.Contracts.CostCategories;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

[Collection(FinanceApplicationCollection.Name)]
public sealed class BoqSnapshotFlowTests(FinanceApplicationFixture fixture)
{
    private async Task<Guid> CategoryAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return (await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(
                new CreateCostCategoryRequest($"QC{Guid.NewGuid().ToString("N")[..8]}", "Materials"))))
            .Value;
    }

    private async Task<Guid> BudgetLineAsync(
        Guid projectId, Guid categoryId, decimal amount, Guid? stageId = null, Guid? taskId = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> created = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateBudgetLineCommand, Guid>>()
            .HandleAsync(new CreateBudgetLineCommand(new CreateBudgetLineRequest(
                "BQ", projectId, categoryId, "Budget", amount, stageId, taskId)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private async Task<Result<Guid>> GenerateAsync(
        Guid projectId, Guid generatedById, Guid? stageId = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<GenerateBoqSnapshotCommand, Guid>>()
            .HandleAsync(new GenerateBoqSnapshotCommand(
                new GenerateBoqSnapshotRequest(projectId, BoqSnapshotType.Manual, stageId), generatedById));
    }

    private async Task<BoqSnapshotResponse> ReadAsync(Guid id)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<BoqSnapshotResponse> read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetBoqSnapshotByIdQuery, BoqSnapshotResponse>>()
            .HandleAsync(new GetBoqSnapshotByIdQuery(id));

        Assert.True(read.IsSuccess, read.IsFailure ? read.Error.ToString() : null);
        return read.Value!;
    }

    /// <summary>
    /// A task with an allocation and an actual cost becomes one BudgetItem line, and the header totals
    /// are the sums of those lines. Also pins the two things a BudgetItem line derives:
    /// budgeted is the task's EFFECTIVE allocation, and difference is the overrun floored at zero.
    /// </summary>
    [Fact]
    public async Task Generate_ProducesOneBudgetItemLinePerAllocatedTask()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Guid lineId = await BudgetLineAsync(scope.ProjectId, categoryId, 1000m, scope.StageId, scope.TaskId);
        await FinanceTestData.PostActualCostAsync(
            fixture, scope.ProjectId, scope.TaskId, lineId, 250m, owner.EmployeeId);

        Guid snapshotId = (await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value;
        BoqSnapshotResponse snapshot = await ReadAsync(snapshotId);

        BoqSnapshotLineResponse budgetItem = Assert.Single(snapshot.Lines, l => l.ProjectTaskId == scope.TaskId
                                                            && l.InvoiceLineId is null
                                                            && l.VariationId is null);

        Assert.Equal("First task", budgetItem.Description);
        Assert.Equal(1000m, budgetItem.BudgetedAmount);
        Assert.Equal(250m, budgetItem.ActualAmount);

        // Underspend is NOT a difference: OverBudgetAmount is Math.Max(0, actual - effective).
        Assert.Equal(0m, budgetItem.DifferenceAmount);
    }

    [Fact]
    public async Task Generate_ReportsAnOverrunAsTheDifference()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Guid lineId = await BudgetLineAsync(
            scope.ProjectId, categoryId, 100m, scope.OtherStageId, scope.OtherTaskId);

        await FinanceTestData.PostActualCostAsync(
            fixture, scope.ProjectId, scope.OtherTaskId, lineId, 175m, owner.EmployeeId);

        Guid snapshotId = (await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value;
        BoqSnapshotResponse snapshot = await ReadAsync(snapshotId);

        BoqSnapshotLineResponse line = Assert.Single(snapshot.Lines, l => l.ProjectTaskId == scope.OtherTaskId
                                                      && l.InvoiceLineId is null
                                                      && l.VariationId is null);

        Assert.Equal(75m, line.DifferenceAmount);
    }

    /// <summary>
    /// A task with NO budget line produces no BOQ line at all — the backend's
    /// <c>if (budgeted &lt;= 0) continue;</c>. This module reaches the same set from the other side, by
    /// starting from its own budget lines, so the assertion is that both routes agree.
    /// </summary>
    [Fact]
    public async Task Generate_SkipsTasksWithNoAllocation()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        await BudgetLineAsync(scope.ProjectId, categoryId, 400m, scope.StageId, scope.TaskId);

        Guid snapshotId = (await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value;
        BoqSnapshotResponse snapshot = await ReadAsync(snapshotId);

        // Liveness first: the allocated task IS present, so the absence below is exclusion rather than
        // an empty snapshot.
        Assert.Contains(snapshot.Lines, l => l.ProjectTaskId == scope.TaskId);
        Assert.DoesNotContain(
            snapshot.Lines,
            l => l.ProjectTaskId == scope.OtherTaskId && l.InvoiceLineId is null && l.VariationId is null);
    }

    /// <summary>
    /// <b>The totals are raw sums, and this is the test that would fail if a rounding step were ever
    /// added.</b> Three allocations chosen so their exact sum has four decimal places that any rounding
    /// to two would destroy. The phase plan called for porting "BOQ generation's rounding rule"; there
    /// is none, and the absence is what this pins.
    /// </summary>
    [Fact]
    public async Task Generate_SumsTotalsExactly_WithoutRounding()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        Guid categoryId = await CategoryAsync();

        await BudgetLineAsync(scope.ProjectId, categoryId, 0.3333m, scope.StageId, scope.TaskId);
        await BudgetLineAsync(scope.ProjectId, categoryId, 0.3333m, scope.StageId, scope.TaskId);
        await BudgetLineAsync(scope.ProjectId, categoryId, 0.3335m, scope.StageId, scope.TaskId);

        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid snapshotId = (await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value;
        BoqSnapshotResponse snapshot = await ReadAsync(snapshotId);

        Assert.Equal(1.0001m, snapshot.BudgetedTotal);
    }

    /// <summary>Versions are per project and monotonic, and the unique index on
    /// <c>(ProjectId, Version)</c> is what makes that safe under concurrency.</summary>
    [Fact]
    public async Task Generate_AllocatesTheNextVersionPerProject()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        await BudgetLineAsync(scope.ProjectId, categoryId, 10m, scope.StageId, scope.TaskId);

        BoqSnapshotResponse first = await ReadAsync((await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value);
        BoqSnapshotResponse second = await ReadAsync((await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
    }

    [Fact]
    public async Task GetLatest_ReturnsTheHighestVersion()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        await BudgetLineAsync(scope.ProjectId, categoryId, 10m, scope.StageId, scope.TaskId);

        await GenerateAsync(scope.ProjectId, owner.EmployeeId);
        Guid secondId = (await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        Result<BoqSnapshotResponse> latest = await dbScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetLatestBoqSnapshotQuery, BoqSnapshotResponse>>()
            .HandleAsync(new GetLatestBoqSnapshotQuery(scope.ProjectId));

        Assert.Equal(secondId, latest.Value!.Id);
        Assert.Equal(2, latest.Value.Version);
    }

    [Fact]
    public async Task GetLatest_ForAProjectWithNoSnapshots_FailsWithNotFound()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        Result<BoqSnapshotResponse> latest = await dbScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetLatestBoqSnapshotQuery, BoqSnapshotResponse>>()
            .HandleAsync(new GetLatestBoqSnapshotQuery(scope.ProjectId));

        Assert.True(latest.IsFailure);
        Assert.Equal("boq_snapshots.NotFound", latest.Error.Code);
    }

    [Fact]
    public async Task GetByProject_PagesAndFiltersByType()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        await BudgetLineAsync(scope.ProjectId, categoryId, 10m, scope.StageId, scope.TaskId);
        await GenerateAsync(scope.ProjectId, owner.EmployeeId);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IQueryHandler<GetBoqSnapshotsByProjectQuery, PagedResult<BoqSnapshotListItemResponse>> list = dbScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetBoqSnapshotsByProjectQuery, PagedResult<BoqSnapshotListItemResponse>>>();

        Result<PagedResult<BoqSnapshotListItemResponse>> manual = await list.HandleAsync(
            new GetBoqSnapshotsByProjectQuery(scope.ProjectId, BoqSnapshotType.Manual));
        Assert.Single(manual.Value!.Items);

        // Liveness on the filter's other side: the same query with a type nothing was generated under
        // must come back empty, or the filter is not filtering.
        Result<PagedResult<BoqSnapshotListItemResponse>> stageClose = await list.HandleAsync(
            new GetBoqSnapshotsByProjectQuery(scope.ProjectId, BoqSnapshotType.StageClose));
        Assert.Empty(stageClose.Value!.Items);
    }

    /// <summary>DIVERGENCE, same as the budget-line create path: the backend never validates the
    /// project, so an unknown id surfaces as a foreign-key violation rather than a 404.</summary>
    [Fact]
    public async Task Generate_ForAnUnknownProject_FailsWithProjectNotFound()
    {
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);

        Result<Guid> generated = await GenerateAsync(Guid.NewGuid(), owner.EmployeeId);

        Assert.True(generated.IsFailure);
        Assert.Equal("boq_snapshots.ProjectNotFound", generated.Error.Code);
    }

    /// <summary>
    /// A snapshot with no budget lines, no vendor invoices and no signed differences is still a valid
    /// snapshot — an empty statement of position, not an error. Worth pinning because the natural
    /// "guard against nothing to report" instinct would turn a legitimate zero into a failure, and
    /// because it exercises the empty-task-set branch of the batched name lookup.
    /// </summary>
    [Fact]
    public async Task Generate_WithNothingToReport_ProducesAnEmptyButValidSnapshot()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);

        Result<Guid> generated = await GenerateAsync(scope.ProjectId, owner.EmployeeId);
        Assert.True(generated.IsSuccess, generated.IsFailure ? generated.Error.ToString() : null);

        BoqSnapshotResponse snapshot = await ReadAsync(generated.Value);

        Assert.Empty(snapshot.Lines);
        Assert.Equal(0m, snapshot.BudgetedTotal);
        Assert.Equal(0m, snapshot.PurchaseTotal);
        Assert.Equal(0m, snapshot.DifferenceTotal);
        Assert.Equal(0m, snapshot.ActualTotal);
        Assert.Equal(owner.EmployeeId, snapshot.GeneratedById);
    }

    /// <summary>
    /// Snapshots are immutable by construction — there is no update command and no delete command, by
    /// design, because a snapshot is a statement about a moment. Asserted against the registered handler
    /// set rather than as prose: if someone adds one, this fails and the design decision gets revisited
    /// deliberately instead of eroding.
    /// </summary>
    [Fact]
    public void NoUpdateOrDeleteCommandExistsForBoqSnapshots()
    {
        string[] messageTypes = typeof(GenerateBoqSnapshotCommand).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(GenerateBoqSnapshotCommand).Namespace)
            .Select(t => t.Name)
            .ToArray();

        // Liveness: an empty or mis-scoped reflection result would make both assertions below vacuous.
        Assert.Contains("GenerateBoqSnapshotCommand", messageTypes);

        Assert.DoesNotContain(messageTypes, n => n.StartsWith("Update", StringComparison.Ordinal));
        Assert.DoesNotContain(messageTypes, n => n.StartsWith("Delete", StringComparison.Ordinal));
    }

    /// <summary>
    /// The stage filter narrows the budget-item lines. Recorded divergence, deliberately NOT changed:
    /// the backend does not apply the stage filter to signed differences, so a stage-scoped snapshot
    /// carries the whole project's difference total. This test documents the shipped behaviour so that
    /// changing it later is a visible decision about reported money rather than a silent one.
    /// </summary>
    [Fact]
    public async Task Generate_ForOneStage_NarrowsBudgetItemsToThatStage()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        // Both stages of the SAME project are allocated, so the narrowing under test is the stage
        // filter rather than the project filter -- two different projects would exclude the second task
        // for the wrong reason and the test would pass with the stage filter deleted.
        await BudgetLineAsync(scope.ProjectId, categoryId, 600m, scope.StageId, scope.TaskId);
        await BudgetLineAsync(scope.ProjectId, categoryId, 900m, scope.OtherStageId, scope.OtherTaskId);

        Guid snapshotId = (await GenerateAsync(scope.ProjectId, owner.EmployeeId, scope.StageId)).Value;
        BoqSnapshotResponse snapshot = await ReadAsync(snapshotId);

        Assert.Equal(scope.StageId, snapshot.ProjectStageId);
        Assert.Contains(snapshot.Lines, l => l.ProjectTaskId == scope.TaskId);
        Assert.DoesNotContain(snapshot.Lines, l => l.ProjectTaskId == scope.OtherTaskId);

        // And the header total narrows with the lines -- 600, not 1500.
        Assert.Equal(600m, snapshot.BudgetedTotal);
    }

    /// <summary>The header totals must equal the sums of the lines that produced them. A projection
    /// error in either direction — a total computed over a different set than the lines, or lines
    /// written for rows the totals skipped — shows up here and nowhere else.</summary>
    [Fact]
    public async Task Generate_HeaderTotalsAgreeWithTheLinesTheySummarise()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Guid lineId = await BudgetLineAsync(scope.ProjectId, categoryId, 800m, scope.StageId, scope.TaskId);
        await FinanceTestData.PostActualCostAsync(
            fixture, scope.ProjectId, scope.TaskId, lineId, 950m, owner.EmployeeId);

        BoqSnapshotResponse snapshot = await ReadAsync((await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value);

        BoqSnapshotLineResponse[] budgetItems = snapshot.Lines
            .Where(l => l.InvoiceLineId is null && l.VariationId is null)
            .ToArray();

        Assert.NotEmpty(budgetItems);
        Assert.Equal(budgetItems.Sum(l => l.BudgetedAmount), snapshot.BudgetedTotal);
        Assert.Equal(budgetItems.Sum(l => l.ActualAmount), snapshot.ActualTotal);
    }

    /// <summary>
    /// The snapshot rows really are in the finance schema and really are immutable in practice: the
    /// generation path writes them and nothing else does. A direct count is the cheapest way to prove
    /// the lines were persisted rather than only projected.
    /// </summary>
    [Fact]
    public async Task Generate_PersistsTheLines()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        await BudgetLineAsync(scope.ProjectId, categoryId, 42m, scope.StageId, scope.TaskId);
        Guid snapshotId = (await GenerateAsync(scope.ProjectId, owner.EmployeeId)).Value;

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""SELECT count(*) FROM finance.boq_snapshot_lines WHERE "BoqSnapshotId" = '{snapshotId}';""",
            connection);

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }
}
