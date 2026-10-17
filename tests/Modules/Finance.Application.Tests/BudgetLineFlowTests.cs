using BuildingBlocks.Messaging;
using Finance.Contracts.BudgetLines;
using Finance.Contracts.CostCategories;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

[Collection(FinanceApplicationCollection.Name)]
public sealed class BudgetLineFlowTests(FinanceApplicationFixture fixture)
{
    private async Task<Guid> CategoryAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(
                new CreateCostCategoryRequest($"BC{Guid.NewGuid().ToString("N")[..8]}", "Materials")));

        return result.Value;
    }

    private async Task<Result<Guid>> CreateAsync(CreateBudgetLineRequest request)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateBudgetLineCommand, Guid>>()
            .HandleAsync(new CreateBudgetLineCommand(request));
    }

    [Fact]
    public async Task Create_ProjectScoped_RoundTripsAndStartsWithZeroedAggregates()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Result<Guid> created = await CreateAsync(new CreateBudgetLineRequest(
            "BL-1", scope.ProjectId, categoryId, "Joinery budget", 1000m));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        Result<BudgetLineResponse> read = await dbScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>()
            .HandleAsync(new GetBudgetLineByIdQuery(created.Value));

        BudgetLineResponse line = read.Value!;
        Assert.Equal(1000m, line.AllocatedAmount);
        Assert.Equal(0m, line.AdjustmentAllocation);
        Assert.Equal(0m, line.CommittedAmount);
        Assert.Equal(0m, line.ActualAmount);

        // The two derived columns, asserted against their definitions rather than against 1000 twice --
        // a projection that returned AllocatedAmount for both would pass a bare equality check here.
        Assert.Equal(line.AllocatedAmount + line.AdjustmentAllocation, line.EffectiveAllocation);
        Assert.Equal(
            line.EffectiveAllocation - line.CommittedAmount - line.ActualAmount, line.AvailableAmount);
    }

    [Fact]
    public async Task Create_TaskScoped_Succeeds_WhenTheTaskReallyBelongsToTheStageAndProject()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Result<Guid> created = await CreateAsync(new CreateBudgetLineRequest(
            "BL-T", scope.ProjectId, categoryId, "Task budget", 500m, scope.StageId, scope.TaskId));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
    }

    /// <summary>
    /// <b>The case no foreign key can catch, and the entire reason this handler reads
    /// <c>IProjectDirectory</c>.</b> Every id here is real and every constraint added at F1-05 is
    /// satisfied — the task exists, the project exists, the stage exists. Only the RELATIONSHIP between
    /// them is wrong: the task belongs to a different project. Without the scope check this row inserts
    /// cleanly and the task's budget is filed against a project it has nothing to do with.
    /// </summary>
    [Fact]
    public async Task Create_TaskScoped_FailsWithTaskStageMismatch_WhenTheTaskBelongsToAnotherProject()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        FinanceTestData.Isolated foreign = await FinanceTestData.IsolatedProjectAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Result<Guid> created = await CreateAsync(new CreateBudgetLineRequest(
            "BL-X", scope.ProjectId, categoryId, "Wrong project", 500m, foreign.StageId, foreign.TaskId));

        Assert.True(created.IsFailure);
        Assert.Equal("budget_lines.TaskStageMismatch", created.Error.Code);
    }

    [Fact]
    public async Task Create_TaskScoped_FailsWithTaskStageMismatch_WhenTheStageIsNotTheTasksStage()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        FinanceTestData.Isolated foreign = await FinanceTestData.IsolatedProjectAsync(fixture);
        Guid categoryId = await CategoryAsync();

        // Right project, right task, WRONG stage -- the second half of the scope rule, which a test
        // using only the wrong-project case would leave unexercised.
        Result<Guid> created = await CreateAsync(new CreateBudgetLineRequest(
            "BL-S", scope.ProjectId, categoryId, "Wrong stage", 500m, foreign.StageId, scope.TaskId));

        Assert.True(created.IsFailure);
        Assert.Equal("budget_lines.TaskStageMismatch", created.Error.Code);
    }

    /// <summary>DIVERGENCE from the backend, which never validates the project on its own — an unknown
    /// id reaches the database and returns a foreign-key violation instead of a 404. Sites' defect B1,
    /// same shape.</summary>
    [Fact]
    public async Task Create_ForAnUnknownProject_FailsWithProjectNotFound_NotAForeignKeyViolation()
    {
        Guid categoryId = await CategoryAsync();

        Result<Guid> created = await CreateAsync(new CreateBudgetLineRequest(
            "BL-P", Guid.NewGuid(), categoryId, "Ghost project", 100m));

        Assert.True(created.IsFailure);
        Assert.Equal("budget_lines.ProjectNotFound", created.Error.Code);
    }

    [Fact]
    public async Task Create_WithAnInactiveCostCategory_FailsWithCostCategoryNotFound()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        await using (AsyncServiceScope deactivateScope = fixture.CreateScope())
        {
            await deactivateScope.ServiceProvider
                .GetRequiredService<ICommandHandler<DeactivateCostCategoryCommand>>()
                .HandleAsync(new DeactivateCostCategoryCommand(categoryId));
        }

        Result<Guid> created = await CreateAsync(new CreateBudgetLineRequest(
            "BL-C", scope.ProjectId, categoryId, "Withdrawn category", 100m));

        Assert.True(created.IsFailure);
        Assert.Equal("budget_lines.CostCategoryNotFound", created.Error.Code);
    }

    /// <summary>
    /// Updating the allocation must NOT disturb <c>AdjustmentAllocation</c>. That separation is the
    /// whole reason two columns exist rather than one: a variation already applied to this line has to
    /// survive an allocation correction, and <c>EffectiveAllocation</c> is their sum.
    /// </summary>
    [Fact]
    public async Task UpdateAllocation_ChangesTheOriginalEstimateAndLeavesAdjustmentsIntact()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Guid id = (await CreateAsync(new CreateBudgetLineRequest(
            "BL-U", scope.ProjectId, categoryId, "Adjustable", 1000m))).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        Result updated = await sp.GetRequiredService<ICommandHandler<UpdateBudgetLineAllocationCommand>>()
            .HandleAsync(new UpdateBudgetLineAllocationCommand(id, 2500m));
        Assert.True(updated.IsSuccess);

        Result<BudgetLineResponse> read = await sp.GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>()
            .HandleAsync(new GetBudgetLineByIdQuery(id));

        Assert.Equal(2500m, read.Value!.AllocatedAmount);
        Assert.Equal(0m, read.Value.AdjustmentAllocation);
        Assert.Equal(2500m, read.Value.EffectiveAllocation);
    }

    [Fact]
    public async Task UpdateAllocation_ForAnUnknownLine_FailsWithNotFound()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result updated = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<UpdateBudgetLineAllocationCommand>>()
            .HandleAsync(new UpdateBudgetLineAllocationCommand(Guid.NewGuid(), 10m));

        Assert.True(updated.IsFailure);
        Assert.Equal("budget_lines.NotFound", updated.Error.Code);
    }

    /// <summary>
    /// <b>The commitment-to-actual handoff, re-derived from scratch.</b> Two commitments of 300 and an
    /// actual cost of 200 must leave Committed at 400 and Actual at 200 — not Committed 600, which is
    /// what dropping the subtraction in the recalculation gives, and which would restate a spent
    /// commitment as still outstanding.
    /// </summary>
    [Fact]
    public async Task Recalculate_DerivesCommittedNetOfActuals()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Guid id = (await CreateAsync(new CreateBudgetLineRequest(
            "BL-R", scope.ProjectId, categoryId, "Recalculated", 1000m))).Value;

        await FinanceTestData.PostCommitmentAsync(fixture, scope.ProjectId, id, 300m, scope.EmployeeId);
        await FinanceTestData.PostCommitmentAsync(fixture, scope.ProjectId, id, 300m, scope.EmployeeId);
        await FinanceTestData.PostActualCostAsync(
            fixture, scope.ProjectId, null, id, 200m, scope.EmployeeId);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        Result recalculated = await sp.GetRequiredService<ICommandHandler<RecalculateBudgetLineCommand>>()
            .HandleAsync(new RecalculateBudgetLineCommand(id));
        Assert.True(recalculated.IsSuccess);

        Result<BudgetLineResponse> read = await sp.GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>()
            .HandleAsync(new GetBudgetLineByIdQuery(id));

        Assert.Equal(400m, read.Value!.CommittedAmount);
        Assert.Equal(200m, read.Value.ActualAmount);
        Assert.Equal(400m, read.Value.AvailableAmount);
    }

    /// <summary>
    /// Reversals cancel because both sums are signed by direction, not because anything filters them
    /// out. An inbound (In) mirror of the same magnitude nets the pair to zero — which is what lets the
    /// recalculation stay a single grouped query with no knowledge of which rows are reversals.
    /// </summary>
    [Fact]
    public async Task Recalculate_NetsAReversedPairToZero()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Guid id = (await CreateAsync(new CreateBudgetLineRequest(
            "BL-V", scope.ProjectId, categoryId, "Reversed", 1000m))).Value;

        await FinanceTestData.PostCommitmentAsync(fixture, scope.ProjectId, id, 750m, scope.EmployeeId);
        await FinanceTestData.PostCommitmentAsync(
            fixture, scope.ProjectId, id, 750m, scope.EmployeeId, inbound: true);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        await sp.GetRequiredService<ICommandHandler<RecalculateBudgetLineCommand>>()
            .HandleAsync(new RecalculateBudgetLineCommand(id));

        Result<BudgetLineResponse> read = await sp.GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>()
            .HandleAsync(new GetBudgetLineByIdQuery(id));

        Assert.Equal(0m, read.Value!.CommittedAmount);
        Assert.Equal(0m, read.Value.ActualAmount);
    }

    [Fact]
    public async Task GetByProject_FiltersByStageAndTask_AndProjectsTheDerivedColumns()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        Guid categoryId = await CategoryAsync();

        Guid projectScoped = (await CreateAsync(new CreateBudgetLineRequest(
            "BL-A", scope.ProjectId, categoryId, "Project wide", 100m))).Value;

        Guid taskScoped = (await CreateAsync(new CreateBudgetLineRequest(
            "BL-B", scope.ProjectId, categoryId, "Task scoped", 200m, scope.StageId, scope.TaskId))).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IQueryHandler<GetBudgetLinesByProjectQuery, IReadOnlyList<BudgetLineListItemResponse>> list = dbScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetBudgetLinesByProjectQuery, IReadOnlyList<BudgetLineListItemResponse>>>();

        Result<IReadOnlyList<BudgetLineListItemResponse>> all = await list.HandleAsync(new GetBudgetLinesByProjectQuery(scope.ProjectId));
        Assert.Contains(all.Value!, b => b.Id == projectScoped);
        Assert.Contains(all.Value!, b => b.Id == taskScoped);

        Result<IReadOnlyList<BudgetLineListItemResponse>> byTask = await list.HandleAsync(
            new GetBudgetLinesByProjectQuery(scope.ProjectId, ProjectTaskId: scope.TaskId));

        // Liveness before the exclusion: the filtered query must actually return the row it should keep,
        // or "does not contain the project-scoped line" is true of an empty list for the wrong reason.
        Assert.Contains(byTask.Value!, b => b.Id == taskScoped);
        Assert.DoesNotContain(byTask.Value!, b => b.Id == projectScoped);

        BudgetLineListItemResponse item = byTask.Value!.Single(b => b.Id == taskScoped);
        Assert.Equal(item.AllocatedAmount + item.AdjustmentAllocation, item.EffectiveAllocation);
        Assert.Equal(
            item.EffectiveAllocation - item.CommittedAmount - item.ActualAmount, item.AvailableAmount);
    }
}
