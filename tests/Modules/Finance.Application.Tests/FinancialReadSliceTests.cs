using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts;
using Finance.Contracts.BudgetLines;
using Finance.Contracts.CostCategories;
using Finance.Contracts.Financial;
using Finance.Contracts.Invoices;
using Finance.Contracts.LedgerEntries;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// The two InvoiceListItemResponse records -- one per namespace, different members -- collide by simple
// name here. Aliased rather than fully qualified, so the collision the Contracts doc comments warn about
// is visible at the top of the file instead of buried in two type arguments.
using FinancialInvoiceRow = Finance.Contracts.Financial.InvoiceListItemResponse;

namespace Finance.Application.Tests;

/// <summary>
/// The project/stage/task financial read slice — fifteen queries whose handlers are Finance's and whose
/// routes will live under Projects' URL space at F1-09.
/// </summary>
[Collection(FinanceApplicationCollection.Name)]
public sealed class FinancialReadSliceTests(FinanceApplicationFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<Guid> CategoryAsync(string name = "Materials")
    {
        await using var scope = fixture.CreateScope();
        return (await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(
                new CreateCostCategoryRequest($"FR{Guid.NewGuid().ToString("N")[..8]}", name)))).Value;
    }

    private async Task<Guid> BudgetLineAsync(
        Guid projectId, Guid categoryId, decimal allocated, Guid? stageId = null, Guid? taskId = null)
    {
        await using var scope = fixture.CreateScope();
        var created = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateBudgetLineCommand, Guid>>()
            .HandleAsync(new CreateBudgetLineCommand(new CreateBudgetLineRequest(
                "FR", projectId, categoryId, "Read slice budget", allocated, stageId, taskId)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private async Task<Guid> PostAsync(PostLedgerEntryRequest request, Guid actorId)
    {
        await using var scope = fixture.CreateScope();
        var posted = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<PostLedgerEntryCommand, Guid>>()
            .HandleAsync(new PostLedgerEntryCommand(request, actorId));

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.ToString() : null);
        return posted.Value;
    }

    private async Task<Guid> InvoiceAsync(
        Guid projectId, Guid clientId, decimal amount, Guid? stageId = null, Guid? taskId = null,
        InvoiceType type = InvoiceType.ClientInvoice)
    {
        await using var scope = fixture.CreateScope();
        var created = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>()
            .HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                $"FR-{Guid.NewGuid().ToString("N")[..10]}", projectId, type, Today,
                [new InvoiceLineDto("Work", 1m, amount, ProjectTaskId: taskId)],
                ProjectStageId: stageId,
                ClientId: type == InvoiceType.ClientInvoice ? clientId : null,
                VendorName: type == InvoiceType.VendorInvoice ? "Acme" : null)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    // ── Project summary ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectSummary_ForAnUnknownProject_FailsWithProjectNotFound()
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectFinancialSummaryQuery, ProjectFinancialSummaryResponse>>()
            .HandleAsync(new GetProjectFinancialSummaryQuery(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("finance_dashboard.ProjectNotFound", read.Error.Code);
    }

    /// <summary>
    /// The project block, asserted against its own definitions rather than against literals twice, plus
    /// the two percentages measured against the EFFECTIVE cap.
    /// </summary>
    [Fact]
    public async Task ProjectSummary_TotalsAgreeWithTheReconstructionModel()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, await CategoryAsync(), 1000m);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 300m, LedgerDirection.Out, LedgerEntryType.Commitment, Today,
            BudgetLineId: lineId), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 200m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            BudgetLineId: lineId), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 400m, LedgerDirection.In, LedgerEntryType.ClientPayment, Today,
            ClientId: owner.ClientId), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var read = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectFinancialSummaryQuery, ProjectFinancialSummaryResponse>>()
            .HandleAsync(new GetProjectFinancialSummaryQuery(scope.ProjectId));

        var t = read.Value!.Totals;

        Assert.Equal("EGP", t.Currency);
        Assert.Equal(1000m, t.EstimatedBudget);
        Assert.Equal(t.EstimatedBudget + t.ApprovedAdjustments, t.EffectiveCap);
        Assert.Equal(100m, t.CommittedAmount);
        Assert.Equal(200m, t.ActualAmount);
        Assert.Equal(t.EffectiveCap - t.CommittedAmount - t.ActualAmount, t.AvailableAmount);
        Assert.Equal(400m, t.ClientPaidAmount);
        Assert.Equal(200m, t.VendorPaidAmount);
        Assert.Equal(t.ClientPaidAmount - t.VendorPaidAmount, t.Earnings);
        Assert.Equal(40m, t.CollectionPercent);
        Assert.Equal(20m, t.CostPercent);
    }

    /// <summary>
    /// <b>The effective-stage fallback chain, which is the part of the stage rollup that is easy to get
    /// wrong.</b> A posting made against a task-scoped budget line names neither a stage nor a task;
    /// without the budget-line fallback it vanishes from every stage row while still counting in the
    /// project total, and the two silently disagree. This asserts they agree.
    /// </summary>
    [Fact]
    public async Task ProjectSummary_AttributesABudgetLineOnlyPostingToItsEffectiveStage()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var lineId = await BudgetLineAsync(
            scope.ProjectId, await CategoryAsync(), 500m, scope.StageId, scope.TaskId);

        // Names the budget line only -- no ProjectStageId, no ProjectTaskId.
        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 120m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            BudgetLineId: lineId), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var read = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectFinancialSummaryQuery, ProjectFinancialSummaryResponse>>()
            .HandleAsync(new GetProjectFinancialSummaryQuery(scope.ProjectId));

        var stage = Assert.Single(read.Value!.Stages, s => s.ProjectStageId == scope.StageId);

        Assert.Equal("First stage", stage.Name);
        Assert.Equal(500m, stage.EffectiveCap);
        Assert.Equal(120m, stage.ActualAmount);

        // And the stage rows sum to the project total, which is the property the fallback protects.
        Assert.Equal(read.Value.Totals.ActualAmount, read.Value.Stages.Sum(s => s.ActualAmount));
        Assert.Equal(read.Value.Totals.EffectiveCap, read.Value.Stages.Sum(s => s.EffectiveCap));
    }

    // ── Project movements ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectMovements_FilterAndPage_AndResolveTheBudgetLineName()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, await CategoryAsync(), 900m);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 100m, LedgerDirection.Out, LedgerEntryType.Commitment, Today,
            BudgetLineId: lineId), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 50m, LedgerDirection.In, LedgerEntryType.ClientPayment,
            new DateOnly(2021, 5, 5), ClientId: owner.ClientId), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var list = dbScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetProjectFinancialMovementsQuery, PagedResult<LedgerMovementResponse>>>();

        var all = await list.HandleAsync(new GetProjectFinancialMovementsQuery(scope.ProjectId));
        Assert.Equal(2, all.Value!.TotalCount);

        var commitment = Assert.Single(all.Value.Items, m => m.Type == LedgerEntryType.Commitment);
        Assert.Equal("Read slice budget", commitment.BudgetLineName);

        // A movement with no budget line must project a NULL name, not an empty string or a dropped row.
        var payment = Assert.Single(all.Value.Items, m => m.Type == LedgerEntryType.ClientPayment);
        Assert.Null(payment.BudgetLineName);

        var byBudgetLine = await list.HandleAsync(
            new GetProjectFinancialMovementsQuery(scope.ProjectId, BudgetLineId: lineId));
        Assert.Single(byBudgetLine.Value!.Items);

        var windowed = await list.HandleAsync(new GetProjectFinancialMovementsQuery(
            scope.ProjectId, FromDate: new DateOnly(2026, 1, 1)));
        Assert.Single(windowed.Value!.Items);
        Assert.Equal(LedgerEntryType.Commitment, windowed.Value.Items[0].Type);
    }

    // ── Cost category breakdown ────────────────────────────────────────────────

    /// <summary>
    /// Categories split allocation and spend, with uncategorised lines rolled into one bucket rather
    /// than dropped — dropping them would make the rows silently fail to sum to the project allocation,
    /// which is what the final assertion here checks.
    /// </summary>
    [Fact]
    public async Task CostCategoryBreakdown_BucketsByCategoryAndKeepsUncategorisedLines()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var joineryId = await CategoryAsync("Joinery");
        var joineryLine = await BudgetLineAsync(scope.ProjectId, joineryId, 600m);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 150m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            BudgetLineId: joineryLine), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var rows = (await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectCostCategoryBreakdownQuery, IReadOnlyList<CostCategoryRow>>>()
            .HandleAsync(new GetProjectCostCategoryBreakdownQuery(scope.ProjectId))).Value!;

        var joinery = Assert.Single(rows, r => r.CostCategoryId == joineryId);
        Assert.Equal("Joinery", joinery.CategoryName);
        Assert.Equal(600m, joinery.AllocatedAmount);
        Assert.Equal(150m, joinery.ActualAmount);
        Assert.Equal(-150m, joinery.CommittedAmount);

        // Allocation across categories must reconstruct the project's effective cap.
        var summary = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectFinancialSummaryQuery, ProjectFinancialSummaryResponse>>()
            .HandleAsync(new GetProjectFinancialSummaryQuery(scope.ProjectId));

        Assert.Equal(summary.Value!.Totals.EffectiveCap, rows.Sum(r => r.AllocatedAmount));
    }

    [Fact]
    public async Task CostCategoryBreakdown_ForAProjectWithNoBudgetLines_IsEmpty()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);

        await using var dbScope = fixture.CreateScope();
        var rows = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetProjectCostCategoryBreakdownQuery, IReadOnlyList<CostCategoryRow>>>()
            .HandleAsync(new GetProjectCostCategoryBreakdownQuery(scope.ProjectId));

        Assert.Empty(rows.Value!);
    }

    // ── Cashflow ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Cashflow is the one aggregate here that is UNSIGNED</b>, and the reversal case is what shows
    /// it. A chart wants two bars, so an inflow and its reversal appear as one inflow and one outflow
    /// rather than cancelling to nothing — which is the correct depiction, because the money really did
    /// move twice.
    /// </summary>
    [Fact]
    public async Task Cashflow_ReportsInflowAndOutflowSeparately_AndDoesNotNetReversals()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var paymentId = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 500m, LedgerDirection.In, LedgerEntryType.ClientPayment, Today,
            ClientId: owner.ClientId), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;
        var cashflow = sp.GetRequiredService<
            IQueryHandler<GetProjectCashflowQuery, IReadOnlyList<MonthlyCashRow>>>();

        var before = (await cashflow.HandleAsync(new GetProjectCashflowQuery(scope.ProjectId))).Value!;
        var beforeRow = Assert.Single(before);
        Assert.Equal(500m, beforeRow.Inflow);
        Assert.Equal(0m, beforeRow.Outflow);

        await sp.GetRequiredService<ICommandHandler<ReverseLedgerEntryCommand, Guid>>()
            .HandleAsync(new ReverseLedgerEntryCommand(paymentId, owner.EmployeeId));

        var after = (await cashflow.HandleAsync(new GetProjectCashflowQuery(scope.ProjectId))).Value!;
        var afterRow = Assert.Single(after);

        Assert.Equal(500m, afterRow.Inflow);
        Assert.Equal(500m, afterRow.Outflow);
    }

    /// <summary>The window defaults to the trailing twelve months, so an old movement is excluded
    /// unless it is asked for — and asking for it brings it back, which is the liveness half.</summary>
    [Fact]
    public async Task Cashflow_DefaultsToTheTrailingTwelveMonths()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 40m, LedgerDirection.Out, LedgerEntryType.ActualCost,
            new DateOnly(2019, 2, 2)), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var cashflow = dbScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetProjectCashflowQuery, IReadOnlyList<MonthlyCashRow>>>();

        var defaulted = await cashflow.HandleAsync(new GetProjectCashflowQuery(scope.ProjectId));
        Assert.Empty(defaulted.Value!);

        var widened = await cashflow.HandleAsync(new GetProjectCashflowQuery(
            scope.ProjectId, FromDate: new DateOnly(2019, 1, 1), ToDate: Today));

        var row = Assert.Single(widened.Value!);
        Assert.Equal(2019, row.Year);
        Assert.Equal(2, row.Month);
        Assert.Equal(40m, row.Outflow);
    }

    // ── Project invoices / variations / snapshots ──────────────────────────────

    [Fact]
    public async Task ProjectInvoices_FilterByTypeAndStage_AndCountTheirLines()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var clientId = await InvoiceAsync(scope.ProjectId, owner.ClientId, 100m, scope.StageId);
        var vendorId = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, 60m, type: InvoiceType.VendorInvoice);

        await using var dbScope = fixture.CreateScope();
        var list = dbScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetProjectInvoicesQuery, PagedResult<FinancialInvoiceRow>>>();

        var all = await list.HandleAsync(new GetProjectInvoicesQuery(scope.ProjectId));
        Assert.Equal(2, all.Value!.TotalCount);
        Assert.All(all.Value.Items, i => Assert.Equal(1, i.LinesCount));

        var vendors = await list.HandleAsync(
            new GetProjectInvoicesQuery(scope.ProjectId, Type: InvoiceType.VendorInvoice));
        Assert.Equal(vendorId, Assert.Single(vendors.Value!.Items).Id);

        var staged = await list.HandleAsync(
            new GetProjectInvoicesQuery(scope.ProjectId, ProjectStageId: scope.StageId));
        Assert.Equal(clientId, Assert.Single(staged.Value!.Items).Id);
    }

    [Fact]
    public async Task ProjectBoqSnapshots_ReportTheirLineCount_AndFilterByVersion()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        await BudgetLineAsync(scope.ProjectId, await CategoryAsync(), 300m, scope.StageId, scope.TaskId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;
        var generate = sp.GetRequiredService<
            ICommandHandler<Contracts.BoqSnapshots.GenerateBoqSnapshotCommand, Guid>>();

        await generate.HandleAsync(new Contracts.BoqSnapshots.GenerateBoqSnapshotCommand(
            new Contracts.BoqSnapshots.GenerateBoqSnapshotRequest(
                scope.ProjectId, BoqSnapshotType.Manual), owner.EmployeeId));

        await generate.HandleAsync(new Contracts.BoqSnapshots.GenerateBoqSnapshotCommand(
            new Contracts.BoqSnapshots.GenerateBoqSnapshotRequest(
                scope.ProjectId, BoqSnapshotType.Manual), owner.EmployeeId));

        var list = sp.GetRequiredService<
            IQueryHandler<GetProjectBoqSnapshotsQuery, IReadOnlyList<BoqSnapshotSummaryResponse>>>();

        var all = (await list.HandleAsync(new GetProjectBoqSnapshotsQuery(scope.ProjectId))).Value!;
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all[0].Version);
        Assert.Equal(1, all[0].LinesCount);

        var v1 = (await list.HandleAsync(new GetProjectBoqSnapshotsQuery(scope.ProjectId, Version: 1))).Value!;
        Assert.Equal(1, Assert.Single(v1).Version);
    }

    // ── Stage scope ────────────────────────────────────────────────────────────

    [Fact]
    public async Task StageMovements_ForAnUnknownStage_FailWithStageNotFound()
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetStageFinancialMovementsQuery, IReadOnlyList<LedgerMovementResponse>>>()
            .HandleAsync(new GetStageFinancialMovementsQuery(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("financial.StageNotFound", read.Error.Code);
    }

    /// <summary>
    /// <b>The stage scope is a union of three routes.</b> This posts one entry by each and asserts all
    /// three arrive: named directly on the stage, named on a task OF the stage, and named on a budget
    /// line under the stage. Collapsing the scope to the direct column would return one of the three.
    /// </summary>
    [Fact]
    public async Task StageMovements_ReachEntriesByStage_ByTaskOfTheStage_AndByBudgetLine()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var lineId = await BudgetLineAsync(
            scope.ProjectId, await CategoryAsync(), 900m, scope.StageId, scope.TaskId);

        var direct = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 10m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            ProjectStageId: scope.StageId), owner.EmployeeId);

        var viaTask = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 20m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            ProjectTaskId: scope.TaskId), owner.EmployeeId);

        var viaBudgetLine = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 30m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            BudgetLineId: lineId), owner.EmployeeId);

        // A movement on the OTHER stage of the same project, to prove the scope narrows at all.
        var otherStage = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 40m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            ProjectStageId: scope.OtherStageId), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var movements = (await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetStageFinancialMovementsQuery, IReadOnlyList<LedgerMovementResponse>>>()
            .HandleAsync(new GetStageFinancialMovementsQuery(scope.StageId))).Value!;

        Assert.Contains(movements, m => m.Id == direct);
        Assert.Contains(movements, m => m.Id == viaTask);
        Assert.Contains(movements, m => m.Id == viaBudgetLine);
        Assert.DoesNotContain(movements, m => m.Id == otherStage);
    }

    [Fact]
    public async Task StageInvoicesVariationsAndSnapshots_AreScopedToTheStage()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var mine = await InvoiceAsync(scope.ProjectId, owner.ClientId, 70m, scope.StageId);
        var theirs = await InvoiceAsync(scope.ProjectId, owner.ClientId, 80m, scope.OtherStageId);

        await using var dbScope = fixture.CreateScope();
        var invoices = (await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetStageInvoicesQuery, IReadOnlyList<FinancialInvoiceRow>>>()
            .HandleAsync(new GetStageInvoicesQuery(scope.StageId))).Value!;

        Assert.Equal(mine, Assert.Single(invoices).Id);
        Assert.DoesNotContain(invoices, i => i.Id == theirs);

        // Variations and snapshots have none here; both must answer with an empty list, not an error.
        var variations = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetStageVariationsQuery, IReadOnlyList<VariationListItemResponse>>>()
            .HandleAsync(new GetStageVariationsQuery(scope.StageId));
        Assert.Empty(variations.Value!);

        var snapshots = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetStageBoqSnapshotsQuery, IReadOnlyList<BoqSnapshotSummaryResponse>>>()
            .HandleAsync(new GetStageBoqSnapshotsQuery(scope.StageId));
        Assert.Empty(snapshots.Value!);
    }

    // ── Task scope ─────────────────────────────────────────────────────────────

    /// <summary>Both routes into a task, same shape as the stage test: named directly, and reached
    /// through a budget line the task owns.</summary>
    [Fact]
    public async Task TaskMovements_ReachEntriesByTaskAndByItsBudgetLine()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var lineId = await BudgetLineAsync(
            scope.ProjectId, await CategoryAsync(), 900m, scope.StageId, scope.TaskId);

        var direct = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 11m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            ProjectTaskId: scope.TaskId), owner.EmployeeId);

        var viaLine = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 22m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            BudgetLineId: lineId), owner.EmployeeId);

        var elsewhere = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 33m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            ProjectTaskId: scope.OtherTaskId), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var movements = (await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetTaskFinancialMovementsQuery, IReadOnlyList<LedgerMovementResponse>>>()
            .HandleAsync(new GetTaskFinancialMovementsQuery(scope.TaskId))).Value!;

        Assert.Contains(movements, m => m.Id == direct);
        Assert.Contains(movements, m => m.Id == viaLine);
        Assert.DoesNotContain(movements, m => m.Id == elsewhere);
    }

    [Fact]
    public async Task TaskInvoiceLines_ReachLinesByTaskAndByItsBudgetLine()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var lineId = await BudgetLineAsync(
            scope.ProjectId, await CategoryAsync(), 900m, scope.StageId, scope.TaskId);

        await InvoiceAsync(scope.ProjectId, owner.ClientId, 45m, taskId: scope.TaskId);

        await using (var invoiceScope = fixture.CreateScope())
        {
            await invoiceScope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>()
                .HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                    $"FB-{Guid.NewGuid().ToString("N")[..10]}", scope.ProjectId,
                    InvoiceType.VendorInvoice, Today,
                    [new InvoiceLineDto("Via budget line", 1m, 55m, BudgetLineId: lineId)],
                    VendorName: "Acme")));
        }

        await using var dbScope = fixture.CreateScope();
        var lines = (await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetTaskInvoiceLinesQuery, IReadOnlyList<TaskInvoiceLineResponse>>>()
            .HandleAsync(new GetTaskInvoiceLinesQuery(scope.TaskId))).Value!;

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.LineTotal == 45m && l.BudgetLineId is null);
        Assert.Contains(lines, l => l.LineTotal == 55m && l.BudgetLineId == lineId);

        // The invoice header travels with each line -- the whole point of this drilldown.
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.InvoiceNumber)));
    }

    /// <summary>
    /// The default is the latest snapshot of each TYPE. Two Manual generations must yield the task's
    /// lines once, from version 2 — not twice.
    /// </summary>
    [Fact]
    public async Task TaskBoqLines_DefaultToTheLatestSnapshotOfEachType()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        await BudgetLineAsync(scope.ProjectId, await CategoryAsync(), 250m, scope.StageId, scope.TaskId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;
        var generate = sp.GetRequiredService<
            ICommandHandler<Contracts.BoqSnapshots.GenerateBoqSnapshotCommand, Guid>>();

        for (var i = 0; i < 2; i++)
        {
            await generate.HandleAsync(new Contracts.BoqSnapshots.GenerateBoqSnapshotCommand(
                new Contracts.BoqSnapshots.GenerateBoqSnapshotRequest(
                    scope.ProjectId, BoqSnapshotType.Manual), owner.EmployeeId));
        }

        var boqLines = sp.GetRequiredService<
            IQueryHandler<GetTaskBoqLinesQuery, IReadOnlyList<TaskBoqLineResponse>>>();

        var latest = (await boqLines.HandleAsync(new GetTaskBoqLinesQuery(scope.TaskId))).Value!;
        var only = Assert.Single(latest);
        Assert.Equal(2, only.SnapshotVersion);
        Assert.Equal(250m, only.BudgetedAmount);

        // Naming a version overrides the default and returns that one instead.
        var pinned = (await boqLines.HandleAsync(new GetTaskBoqLinesQuery(scope.TaskId, 1))).Value!;
        Assert.Equal(1, Assert.Single(pinned).SnapshotVersion);
    }

    [Fact]
    public async Task TaskBoqLines_ForAnUnknownTask_AreEmptyRatherThanAnError()
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetTaskBoqLinesQuery, IReadOnlyList<TaskBoqLineResponse>>>()
            .HandleAsync(new GetTaskBoqLinesQuery(Guid.NewGuid()));

        Assert.True(read.IsSuccess);
        Assert.Empty(read.Value!);
    }

    [Fact]
    public async Task TaskVariations_AreScopedToTheTask()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);

        await using var dbScope = fixture.CreateScope();
        var variations = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetTaskVariationsQuery, IReadOnlyList<VariationListItemResponse>>>()
            .HandleAsync(new GetTaskVariationsQuery(scope.TaskId));

        // No variations exist until F1-08 builds their handlers; an empty list is the correct answer and
        // this asserts the query runs rather than that it finds anything.
        Assert.True(variations.IsSuccess);
        Assert.Empty(variations.Value!);
    }
}
