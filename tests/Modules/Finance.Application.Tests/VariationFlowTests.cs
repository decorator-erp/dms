using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts;
using Finance.Contracts.BudgetLines;
using Finance.Contracts.CostCategories;
using Finance.Contracts.Variations;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

/// <summary>
/// The variation surface driven through its real handlers — the state machine's unit tests
/// (<c>Finance.UnitTests.VariationStateMachineTests</c>) already enumerate every legal and illegal
/// transition on the entity, so these cover what only the handler can: persistence, the two kinds'
/// asymmetric apply, and the reads.
/// </summary>
[Collection(FinanceApplicationCollection.Name)]
public sealed class VariationFlowTests(FinanceApplicationFixture fixture)
{
    private async Task<Guid> BudgetLineAsync(Guid projectId, decimal allocated, Guid? stageId, Guid? taskId)
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var categoryId = (await sp.GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(new CreateCostCategoryRequest(
                $"VC{Guid.NewGuid().ToString("N")[..8]}", "Materials")))).Value;

        var created = await sp.GetRequiredService<ICommandHandler<CreateBudgetLineCommand, Guid>>()
            .HandleAsync(new CreateBudgetLineCommand(new CreateBudgetLineRequest(
                "VB", projectId, categoryId, "Variation budget", allocated, stageId, taskId)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private async Task<Result<Guid>> RaiseAsync(RaiseVariationRequest request, Guid requestedById)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>()
            .HandleAsync(new RaiseVariationCommand(request, requestedById));
    }

    private async Task<VariationResponse> ReadAsync(Guid id)
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetVariationByIdQuery, VariationResponse>>()
            .HandleAsync(new GetVariationByIdQuery(id));

        Assert.True(read.IsSuccess, read.IsFailure ? read.Error.ToString() : null);
        return read.Value!;
    }

    [Fact]
    public async Task Raise_AClientDifference_DerivesTheAmountFromTheTwoSuppliedFigures()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var raised = await RaiseAsync(new RaiseVariationRequest(
            VariationKind.ClientDifference, scope.ProjectId, "Upgraded finish",
            ProjectTaskId: scope.TaskId, ProjectStageId: scope.StageId,
            BudgetedAmount: 100m, ChosenAmount: 175m), owner.EmployeeId);

        Assert.True(raised.IsSuccess, raised.IsFailure ? raised.Error.ToString() : null);

        var variation = await ReadAsync(raised.Value);

        // Amount is DERIVED and cannot be supplied -- chosen minus budgeted.
        Assert.Equal(75m, variation.Amount);
        Assert.Equal(100m, variation.BudgetedAmount);
        Assert.Equal(175m, variation.ChosenAmount);
        Assert.Equal("Draft", variation.Status);
        Assert.Null(variation.SentToClientAt);
    }

    [Fact]
    public async Task Raise_AClientDifferenceWhereChosenDoesNotExceedBudgeted_Fails()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var raised = await RaiseAsync(new RaiseVariationRequest(
            VariationKind.ClientDifference, scope.ProjectId, "Not a variation",
            ProjectTaskId: scope.TaskId, ProjectStageId: scope.StageId,
            BudgetedAmount: 100m, ChosenAmount: 100m), owner.EmployeeId);

        Assert.True(raised.IsFailure);
    }

    /// <summary>DIVERGENCE, same shape as the other three creation paths: the backend never validates
    /// the project, so an unknown id becomes a foreign-key violation rather than a 404.</summary>
    [Fact]
    public async Task Raise_ForAnUnknownProject_FailsWithProjectNotFound()
    {
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var raised = await RaiseAsync(new RaiseVariationRequest(
            VariationKind.ClientDifference, Guid.NewGuid(), "Ghost",
            ProjectTaskId: Guid.NewGuid(), BudgetedAmount: 1m, ChosenAmount: 2m), owner.EmployeeId);

        Assert.True(raised.IsFailure);
        Assert.Equal("variations.ProjectNotFound", raised.Error.Code);
    }

    /// <summary>Only a client difference is sent to the client on submit; an internal change never
    /// leaves the office and its <c>SentToClientAt</c> stays null.</summary>
    [Fact]
    public async Task Submit_StampsSentToClient_ForAClientDifferenceOnly()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 900m, scope.StageId, scope.TaskId);

        var difference = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.ClientDifference, scope.ProjectId, "Client choice",
            ProjectTaskId: scope.TaskId, ProjectStageId: scope.StageId,
            BudgetedAmount: 10m, ChosenAmount: 20m), owner.EmployeeId)).Value;

        var internalChange = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.InternalChange, scope.ProjectId, "Team raise",
            ProjectStageId: scope.OtherStageId, BudgetLineId: lineId, Amount: 30m), owner.EmployeeId)).Value;

        await using (var dbScope = fixture.CreateScope())
        {
            var submit = dbScope.ServiceProvider.GetRequiredService<ICommandHandler<SubmitVariationCommand>>();
            await submit.HandleAsync(new SubmitVariationCommand(difference));
            await submit.HandleAsync(new SubmitVariationCommand(internalChange));
        }

        Assert.NotNull((await ReadAsync(difference)).SentToClientAt);
        Assert.Null((await ReadAsync(internalChange)).SentToClientAt);
    }

    [Fact]
    public async Task Approve_AnInternalChangeWithSignatureFields_FailsWithSignatureFieldsNotAllowed()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 900m, scope.StageId, scope.TaskId);

        var id = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.InternalChange, scope.ProjectId, "Team raise",
            ProjectStageId: scope.StageId, ProjectTaskId: scope.TaskId,
            BudgetLineId: lineId, Amount: 40m), owner.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;

        await sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>()
            .HandleAsync(new SubmitVariationCommand(id));

        var approved = await sp.GetRequiredService<ICommandHandler<ApproveVariationCommand>>()
            .HandleAsync(new ApproveVariationCommand(
                id, owner.EmployeeId, SignedByClientId: owner.ClientId));

        Assert.True(approved.IsFailure);
        Assert.Equal("variations.SignatureFieldsNotAllowed", approved.Error.Code);
    }

    [Fact]
    public async Task Approve_AClientDifferenceWithoutASignature_FailsWithSignatureRequired()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var id = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.ClientDifference, scope.ProjectId, "Needs a signature",
            ProjectTaskId: scope.TaskId, ProjectStageId: scope.StageId,
            BudgetedAmount: 10m, ChosenAmount: 30m), owner.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;

        await sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>()
            .HandleAsync(new SubmitVariationCommand(id));

        var approved = await sp.GetRequiredService<ICommandHandler<ApproveVariationCommand>>()
            .HandleAsync(new ApproveVariationCommand(id, owner.EmployeeId));

        Assert.True(approved.IsFailure);
        Assert.Equal("variations.SignatureRequired", approved.Error.Code);
    }

    /// <summary>
    /// <b>The asymmetric apply, both halves in one test.</b> An InternalChange pushes its amount into
    /// the budget line's <c>AdjustmentAllocation</c>, raising the effective cap; a ClientDifference
    /// writes nothing. Making them symmetrical is the single most attractive wrong "fix" in this module
    /// and would double-count every client difference.
    /// </summary>
    [Fact]
    public async Task Apply_RaisesTheBudgetLineForAnInternalChange_AndWritesNothingForAClientDifference()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 1000m, scope.StageId, scope.TaskId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;
        var submit = sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>();
        var approve = sp.GetRequiredService<ICommandHandler<ApproveVariationCommand>>();
        var apply = sp.GetRequiredService<ICommandHandler<ApplyVariationCommand>>();
        var readLine = sp.GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>();

        // ── ClientDifference: applies, writes nothing to the line ──
        //
        // It carries a BudgetLineId ON PURPOSE. Without one, "writes nothing to the line" would be true
        // because there is no line to write to, and the assertion would pass even if the handler had
        // dropped its Kind check entirely -- which is exactly what an injection proved before this
        // argument was added. With the line present, only the Kind check stands between this variation
        // and a 150 adjustment.
        var difference = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.ClientDifference, scope.ProjectId, "Signed difference",
            ProjectTaskId: scope.TaskId, ProjectStageId: scope.StageId,
            BudgetLineId: lineId, BudgetedAmount: 100m, ChosenAmount: 250m), owner.EmployeeId)).Value;

        await submit.HandleAsync(new SubmitVariationCommand(difference));
        await approve.HandleAsync(new ApproveVariationCommand(
            difference, owner.EmployeeId, owner.ClientId, "/signed.pdf"));
        Assert.True((await apply.HandleAsync(new ApplyVariationCommand(difference, owner.EmployeeId))).IsSuccess);

        Assert.Equal(0m, (await readLine.HandleAsync(new GetBudgetLineByIdQuery(lineId))).Value!.AdjustmentAllocation);

        // ── InternalChange: applies, raises the line ──
        var internalChange = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.InternalChange, scope.ProjectId, "Team raise",
            ProjectStageId: scope.StageId, ProjectTaskId: scope.TaskId,
            BudgetLineId: lineId, Amount: 300m), owner.EmployeeId)).Value;

        await submit.HandleAsync(new SubmitVariationCommand(internalChange));
        await approve.HandleAsync(new ApproveVariationCommand(internalChange, owner.EmployeeId));
        Assert.True((await apply.HandleAsync(new ApplyVariationCommand(internalChange, owner.EmployeeId))).IsSuccess);

        var line = (await readLine.HandleAsync(new GetBudgetLineByIdQuery(lineId))).Value!;

        Assert.Equal(300m, line.AdjustmentAllocation);
        Assert.Equal(1000m, line.AllocatedAmount);   // the original estimate is preserved
        Assert.Equal(1300m, line.EffectiveAllocation);
    }

    /// <summary>Applying twice fails — the budget-line adjustment it drives must happen exactly once,
    /// and a second call would raise the cap again for the same approved money.</summary>
    [Fact]
    public async Task Apply_Twice_FailsAndDoesNotRaiseTheLineASecondTime()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 500m, scope.StageId, scope.TaskId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;

        var id = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.InternalChange, scope.ProjectId, "Once only",
            ProjectStageId: scope.StageId, ProjectTaskId: scope.TaskId,
            BudgetLineId: lineId, Amount: 60m), owner.EmployeeId)).Value;

        await sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>()
            .HandleAsync(new SubmitVariationCommand(id));
        await sp.GetRequiredService<ICommandHandler<ApproveVariationCommand>>()
            .HandleAsync(new ApproveVariationCommand(id, owner.EmployeeId));

        var apply = sp.GetRequiredService<ICommandHandler<ApplyVariationCommand>>();

        Assert.True((await apply.HandleAsync(new ApplyVariationCommand(id, owner.EmployeeId))).IsSuccess);

        var second = await apply.HandleAsync(new ApplyVariationCommand(id, owner.EmployeeId));
        Assert.True(second.IsFailure);
        Assert.Equal("variations.NotApproved", second.Error.Code);

        var line = await sp.GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>()
            .HandleAsync(new GetBudgetLineByIdQuery(lineId));

        Assert.Equal(60m, line.Value!.AdjustmentAllocation);
    }

    [Fact]
    public async Task Apply_AnUnapprovedVariation_FailsWithNotApproved()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 500m, scope.StageId, scope.TaskId);

        var id = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.InternalChange, scope.ProjectId, "Still draft",
            ProjectStageId: scope.StageId, BudgetLineId: lineId, Amount: 10m), owner.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var applied = await dbScope.ServiceProvider
            .GetRequiredService<ICommandHandler<ApplyVariationCommand>>()
            .HandleAsync(new ApplyVariationCommand(id, owner.EmployeeId));

        Assert.True(applied.IsFailure);
        Assert.Equal("variations.NotApproved", applied.Error.Code);
    }

    [Fact]
    public async Task Cancel_AnApprovedVariation_FailsWithInvalidStatusTransition()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 500m, scope.StageId, scope.TaskId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;

        var id = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.InternalChange, scope.ProjectId, "Approved already",
            ProjectStageId: scope.StageId, BudgetLineId: lineId, Amount: 10m), owner.EmployeeId)).Value;

        await sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>()
            .HandleAsync(new SubmitVariationCommand(id));
        await sp.GetRequiredService<ICommandHandler<ApproveVariationCommand>>()
            .HandleAsync(new ApproveVariationCommand(id, owner.EmployeeId));

        var cancelled = await sp.GetRequiredService<ICommandHandler<CancelVariationCommand>>()
            .HandleAsync(new CancelVariationCommand(id, "Too late"));

        Assert.True(cancelled.IsFailure);
        Assert.Equal("variations.InvalidStatusTransition", cancelled.Error.Code);
    }

    [Fact]
    public async Task Reads_ScopeByProjectStageAndTask()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 900m, scope.OtherStageId, scope.OtherTaskId);

        var taskScoped = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.ClientDifference, scope.ProjectId, "Task scoped",
            ProjectTaskId: scope.TaskId, ProjectStageId: scope.StageId,
            BudgetedAmount: 10m, ChosenAmount: 40m), owner.EmployeeId)).Value;

        var otherScoped = (await RaiseAsync(new RaiseVariationRequest(
            VariationKind.InternalChange, scope.ProjectId, "Other stage",
            ProjectStageId: scope.OtherStageId, ProjectTaskId: scope.OtherTaskId,
            BudgetLineId: lineId, Amount: 15m), owner.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;

        var byProject = await sp.GetRequiredService<
                IQueryHandler<GetVariationsByProjectQuery, PagedResult<VariationListItemResponse>>>()
            .HandleAsync(new GetVariationsByProjectQuery(scope.ProjectId));

        Assert.Equal(2, byProject.Value!.TotalCount);

        var byKind = await sp.GetRequiredService<
                IQueryHandler<GetVariationsByProjectQuery, PagedResult<VariationListItemResponse>>>()
            .HandleAsync(new GetVariationsByProjectQuery(
                scope.ProjectId, Kind: VariationKind.ClientDifference));

        Assert.Equal(taskScoped, Assert.Single(byKind.Value!.Items).Id);

        var byStage = await sp.GetRequiredService<
                IQueryHandler<GetVariationsByStageQuery, IReadOnlyList<VariationListItemResponse>>>()
            .HandleAsync(new GetVariationsByStageQuery(scope.StageId));

        Assert.Contains(byStage.Value!, v => v.Id == taskScoped);
        Assert.DoesNotContain(byStage.Value!, v => v.Id == otherScoped);

        var byTask = await sp.GetRequiredService<
                IQueryHandler<GetVariationsByTaskQuery, IReadOnlyList<VariationListItemResponse>>>()
            .HandleAsync(new GetVariationsByTaskQuery(scope.OtherTaskId));

        Assert.Contains(byTask.Value!, v => v.Id == otherScoped);
        Assert.DoesNotContain(byTask.Value!, v => v.Id == taskScoped);
    }

    [Fact]
    public async Task GetById_ForAnUnknownVariation_FailsWithNotFound()
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetVariationByIdQuery, VariationResponse>>()
            .HandleAsync(new GetVariationByIdQuery(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("variations.NotFound", read.Error.Code);
    }
}
