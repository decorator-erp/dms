using BuildingBlocks.Messaging;
using Finance.Contracts;
using Finance.Contracts.BudgetLines;
using Finance.Contracts.CostCategories;
using Finance.Contracts.Dashboard;
using Finance.Contracts.Invoices;
using Finance.Contracts.LedgerEntries;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

[Collection(FinanceApplicationCollection.Name)]
public sealed class FinancialDashboardTests(FinanceApplicationFixture fixture)
{
    private async Task<ProjectFinancialDashboardResponse> DashboardAsync(Guid projectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<ProjectFinancialDashboardResponse> read = await scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectFinancialDashboardQuery, ProjectFinancialDashboardResponse>>()
            .HandleAsync(new GetProjectFinancialDashboardQuery(projectId));

        Assert.True(read.IsSuccess, read.IsFailure ? read.Error.ToString() : null);
        return read.Value!;
    }

    private async Task<Guid> BudgetLineAsync(
        Guid projectId, decimal allocated, Guid? stageId = null, Guid? taskId = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Guid categoryId = (await sp.GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(new CreateCostCategoryRequest(
                $"DC{Guid.NewGuid().ToString("N")[..8]}", "Materials")))).Value;

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateBudgetLineCommand, Guid>>()
            .HandleAsync(new CreateBudgetLineCommand(new CreateBudgetLineRequest(
                "DB", projectId, categoryId, "Dashboard budget", allocated, stageId, taskId)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private async Task PostAsync(PostLedgerEntryRequest request, Guid actorId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> posted = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<PostLedgerEntryCommand, Guid>>()
            .HandleAsync(new PostLedgerEntryCommand(request, actorId));

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.ToString() : null);
    }

    [Fact]
    public async Task Dashboard_ForAnUnknownProject_FailsWithProjectNotFound()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<ProjectFinancialDashboardResponse> read = await scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectFinancialDashboardQuery, ProjectFinancialDashboardResponse>>()
            .HandleAsync(new GetProjectFinancialDashboardQuery(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("finance_dashboard.ProjectNotFound", read.Error.Code);
    }

    /// <summary>An empty project is a valid dashboard, not an error — every total zero, every list
    /// empty, and no BOQ. Worth pinning because the natural instinct is to guard against "nothing to
    /// report", which would turn a legitimate zero into a failure.</summary>
    [Fact]
    public async Task Dashboard_ForAProjectWithNoFinance_IsAllZeros()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);

        ProjectFinancialDashboardResponse dashboard = await DashboardAsync(scope.ProjectId);

        Assert.Equal("EGP", dashboard.Currency);
        Assert.Equal(0m, dashboard.Budget.EffectiveCap);
        Assert.Equal(0m, dashboard.Budget.TotalAvailable);
        Assert.Equal(0m, dashboard.Collection.TotalClientPayments);
        Assert.Empty(dashboard.BudgetLines);
        Assert.Empty(dashboard.OverBudgetTasks);
        Assert.Null(dashboard.Boq);
        Assert.Empty(dashboard.RecentActivity);
        Assert.Equal(0, dashboard.Invoices.TotalCount);
        Assert.Equal(0, dashboard.Variations.TotalCount);
    }

    /// <summary>
    /// The budget block is the reconstruction model stated in one place: EffectiveCap is allocation plus
    /// adjustments, Available is that less committed and actual. Each is asserted against its definition
    /// rather than a literal, so a projection returning the wrong column cannot pass by coincidence.
    /// </summary>
    [Fact]
    public async Task Dashboard_BudgetBlockAgreesWithItsOwnDefinition()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid lineId = await BudgetLineAsync(scope.ProjectId, 1000m);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 300m, LedgerDirection.Out, LedgerEntryType.Commitment, today,
            BudgetLineId: lineId), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 120m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            BudgetLineId: lineId), owner.EmployeeId);

        ProjectFinancialDashboardResponse d = await DashboardAsync(scope.ProjectId);
        BudgetSummary b = d.Budget;

        Assert.Equal(1000m, b.EstimatedBudget);
        Assert.Equal(0m, b.ApprovedAdjustments);
        Assert.Equal(b.EstimatedBudget + b.ApprovedAdjustments, b.EffectiveCap);
        Assert.Equal(180m, b.TotalCommitted);
        Assert.Equal(120m, b.TotalActual);
        Assert.Equal(b.EffectiveCap - b.TotalCommitted - b.TotalActual, b.TotalAvailable);
        Assert.Equal(700m, b.TotalAvailable);

        // ActualBudget stays null: the column it once projected is gone, and the field is kept on the
        // wire rather than removed from a published contract.
        Assert.Null(b.ActualBudget);
    }

    /// <summary>
    /// <b>Stage funding, including the half that is easy to drop.</b> <c>IsFunded</c> requires
    /// <c>ClientInvoicesSum &gt; 0</c> as well as payments covering it — without that, a stage nobody has
    /// invoiced reads as funded, because zero payments trivially cover zero invoices. That is the same
    /// expression the Budget gate uses at F1-08, so getting it wrong here would unlock stages.
    /// </summary>
    [Fact]
    public async Task Dashboard_StageIsFundedOnlyWhenInvoicedAndCovered()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // A stage that appears in finance only through a budget line: invoiced 0, paid 0.
        await BudgetLineAsync(scope.ProjectId, 500m, scope.OtherStageId);

        await using (AsyncServiceScope dbScope = fixture.CreateScope())
        {
            await dbScope.ServiceProvider.GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>()
                .HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                    $"DS-{Guid.NewGuid().ToString("N")[..10]}", scope.ProjectId,
                    InvoiceType.ClientInvoice, today, [new InvoiceLineDto("Stage work", 1m, 400m)],
                    ProjectStageId: scope.StageId, ClientId: owner.ClientId)));
        }

        ProjectFinancialDashboardResponse partlyPaid = await DashboardAsync(scope.ProjectId);

        StageFundingRow invoicedStage = partlyPaid.Collection.Stages.Single(s => s.StageId == scope.StageId);
        Assert.Equal(400m, invoicedStage.RequiredBudget);
        Assert.Equal(0m, invoicedStage.ClientPaidAmount);
        Assert.False(invoicedStage.IsFunded);

        // The uninvoiced stage must NOT read as funded, which is the assertion the `> 0` half carries.
        StageFundingRow uninvoicedStage = partlyPaid.Collection.Stages.Single(s => s.StageId == scope.OtherStageId);
        Assert.Equal(0m, uninvoicedStage.RequiredBudget);
        Assert.False(uninvoicedStage.IsFunded);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 400m, LedgerDirection.In, LedgerEntryType.ClientPayment, today,
            ProjectStageId: scope.StageId, ClientId: owner.ClientId), owner.EmployeeId);

        ProjectFinancialDashboardResponse funded = await DashboardAsync(scope.ProjectId);
        StageFundingRow after = funded.Collection.Stages.Single(s => s.StageId == scope.StageId);

        Assert.Equal(400m, after.ClientPaidAmount);
        Assert.True(after.IsFunded);
    }

    /// <summary>Stage names come from Projects by id. A blank name would mean the batched lookup
    /// silently returned nothing and every row fell back to the empty default.</summary>
    [Fact]
    public async Task Dashboard_ResolvesStageNamesFromProjects()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);

        await BudgetLineAsync(scope.ProjectId, 100m, scope.StageId);

        ProjectFinancialDashboardResponse dashboard = await DashboardAsync(scope.ProjectId);

        Assert.Equal("First stage", dashboard.Collection.Stages.Single().Name);
    }

    /// <summary>
    /// Over budget is actual beyond the effective cap, floored implicitly by the filter: an under-budget
    /// task is absent rather than present with a negative overrun. The liveness half is that the
    /// over-budget task IS listed, so the absence of the other one means excluded.
    /// </summary>
    [Fact]
    public async Task Dashboard_ListsOnlyOverBudgetTasks_WithTheirNamesAndOverrun()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid overLine = await BudgetLineAsync(scope.ProjectId, 100m, scope.StageId, scope.TaskId);
        Guid underLine = await BudgetLineAsync(scope.ProjectId, 900m, scope.OtherStageId, scope.OtherTaskId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 175m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            ProjectTaskId: scope.TaskId, BudgetLineId: overLine), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 50m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            ProjectTaskId: scope.OtherTaskId, BudgetLineId: underLine), owner.EmployeeId);

        ProjectFinancialDashboardResponse dashboard = await DashboardAsync(scope.ProjectId);

        OverBudgetTaskRow over = Assert.Single(dashboard.OverBudgetTasks);
        Assert.Equal(scope.TaskId, over.TaskId);
        Assert.Equal("First task", over.Name);
        Assert.Equal(100m, over.Allocated);
        Assert.Equal(175m, over.Actual);
        Assert.Equal(75m, over.Overrun);

        Assert.DoesNotContain(dashboard.OverBudgetTasks, t => t.TaskId == scope.OtherTaskId);
    }

    [Fact]
    public async Task Dashboard_BucketsInvoicesByStatusAndTotalsTheOutstanding()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (AsyncServiceScope dbScope = fixture.CreateScope())
        {
            ICommandHandler<CreateInvoiceCommand, Guid> create = dbScope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>();

            await create.HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                $"DA-{Guid.NewGuid().ToString("N")[..10]}", scope.ProjectId, InvoiceType.ClientInvoice,
                today, [new InvoiceLineDto("A", 1m, 100m)], ClientId: owner.ClientId)));

            Guid cancelledId = (await create.HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                $"DB-{Guid.NewGuid().ToString("N")[..10]}", scope.ProjectId, InvoiceType.ClientInvoice,
                today, [new InvoiceLineDto("B", 1m, 60m)], ClientId: owner.ClientId)))).Value;

            await dbScope.ServiceProvider.GetRequiredService<ICommandHandler<CancelInvoiceCommand>>()
                .HandleAsync(new CancelInvoiceCommand(cancelledId));
        }

        ProjectFinancialDashboardResponse dashboard = await DashboardAsync(scope.ProjectId);

        Assert.Equal(2, dashboard.Invoices.TotalCount);
        Assert.Equal(dashboard.Invoices.ByStatus.Sum(b => b.Count), dashboard.Invoices.TotalCount);

        // Outstanding counts only OPEN balances -- the cancelled 60 must not appear.
        Assert.Equal(100m, dashboard.Invoices.TotalOutstanding);
    }

    [Fact]
    public async Task Dashboard_ReportsEarningsAsSignedLedgerTotals()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await BudgetLineAsync(scope.ProjectId, 1000m);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 700m, LedgerDirection.In, LedgerEntryType.ClientPayment, today,
            ClientId: owner.ClientId), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 250m, LedgerDirection.Out, LedgerEntryType.ActualCost, today),
            owner.EmployeeId);

        ProjectFinancialDashboardResponse d = await DashboardAsync(scope.ProjectId);

        Assert.Equal(700m, d.Earnings.TotalIn);
        Assert.Equal(250m, d.Earnings.TotalOut);
        Assert.Equal(d.Earnings.TotalIn - d.Earnings.TotalOut, d.Earnings.Net);
        Assert.Equal(450m, d.Earnings.Net);
        Assert.Equal(45m, d.Earnings.MarginPercent);
    }

    /// <summary>A percentage over a zero cap is 0, not a divide-by-zero. Both percentage fields guard
    /// the same way and both are exercised, because an empty project is a real state.</summary>
    [Fact]
    public async Task Dashboard_PercentagesAreZeroWhenThereIsNoBudget()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 500m, LedgerDirection.In, LedgerEntryType.ClientPayment,
            DateOnly.FromDateTime(DateTime.UtcNow), ClientId: owner.ClientId), owner.EmployeeId);

        ProjectFinancialDashboardResponse d = await DashboardAsync(scope.ProjectId);

        Assert.Equal(0m, d.Budget.EffectiveCap);
        Assert.Equal(0m, d.Earnings.MarginPercent);
        Assert.Equal(0m, d.Collection.CollectionPercentOfEstimatedBudget);
    }

    [Fact]
    public async Task Dashboard_ProjectsBudgetLineRowsWithTheirCostCategoryName()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);

        await BudgetLineAsync(scope.ProjectId, 250m);

        ProjectFinancialDashboardResponse d = await DashboardAsync(scope.ProjectId);
        BudgetLineRow row = Assert.Single(d.BudgetLines);

        Assert.Equal("Materials", row.CostCategoryName);
        Assert.Equal(250m, row.Allocated);
        Assert.Equal(row.Allocated + row.Adjustment - row.Committed - row.Actual, row.Available);
    }

    /// <summary>Recent activity is newest-first and capped by the requested count.</summary>
    [Fact]
    public async Task Dashboard_ReturnsRecentActivityNewestFirst()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 10m, LedgerDirection.Out, LedgerEntryType.ActualCost,
            new DateOnly(2021, 1, 1)), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 20m, LedgerDirection.Out, LedgerEntryType.ActualCost,
            new DateOnly(2026, 6, 1)), owner.EmployeeId);

        ProjectFinancialDashboardResponse d = await DashboardAsync(scope.ProjectId);

        Assert.Equal(2, d.RecentActivity.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), d.RecentActivity[0].OccurredOn);
        Assert.Equal(new DateOnly(2021, 1, 1), d.RecentActivity[1].OccurredOn);
    }
}
