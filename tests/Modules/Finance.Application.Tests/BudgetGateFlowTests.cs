using System.Globalization;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
using Finance.Contracts;
using Finance.Contracts.BudgetLines;
using Finance.Contracts.CostCategories;
using Finance.Contracts.Invoices;
using Finance.Contracts.LedgerEntries;
using Finance.Contracts.Variations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

/// <summary>
/// <b>The Budget gate, end to end and through the real outbox.</b>
///
/// <para>This is the test that proves Phase 7's central structural decision. The backend resolves Budget
/// gates by writing directly into <c>gate_conditions</c>, <c>project_tasks</c> and
/// <c>project_stages</c> from inside its ledger and variation services. Here Finance computes a verdict
/// and publishes it; Projects consumes and writes. <b>Every assertion below therefore drives the
/// publisher and asserts on the CONSUMER's tables, with the outbox in between</b> — calling a Projects
/// handler directly would prove the handler works and say nothing about whether the event is ever
/// published, routed or delivered.</para>
/// </summary>
[Collection(FinanceApplicationCollection.Name)]
public sealed class BudgetGateFlowTests(FinanceApplicationFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // Read straight from Projects.Domain.Enums, which is internal to Projects -- so the values are
    // named here rather than left as magic numbers in raw SQL. Getting one wrong is silent: the query
    // simply finds no row and the assertion fails for the wrong reason.
    private const int BudgetConditionType = 6;   // ConditionType.Budget
    private const int TaskLocked = 1;            // ProjectTaskStatus.Locked
    private const int TaskOpen = 2;              // ProjectTaskStatus.Open

    private async Task<Guid> CategoryAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return (await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(
                new CreateCostCategoryRequest($"GC{Guid.NewGuid().ToString("N")[..8]}", "Materials")))).Value;
    }

    private async Task<Guid> BudgetLineAsync(
        Guid projectId, decimal allocated, Guid? stageId = null, Guid? taskId = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> created = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateBudgetLineCommand, Guid>>()
            .HandleAsync(new CreateBudgetLineCommand(new CreateBudgetLineRequest(
                "GB", projectId, await CategoryAsync(), "Gate budget", allocated, stageId, taskId)));

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

    // ── Projects-side inspection, by raw SQL ───────────────────────────────────
    //
    // Read directly rather than through Projects' query handlers: the point is what landed in the
    // TABLE, and a projection could mask a write that never happened.

    private async Task<(string Status, Guid? VariationId)?> GateAsync(Guid? stageId, Guid? taskId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        string scope = stageId is Guid s ? $"\"ProjectStageId\" = '{s}'" : $"\"ProjectTaskId\" = '{taskId}'";

        await using var command = new NpgsqlCommand(
            $"""
             SELECT "Status", "VariationId" FROM projects.gate_conditions
             WHERE "ConditionType" = {BudgetConditionType} AND {scope}
             ORDER BY "CreatedAtUtc" DESC LIMIT 1;
             """, connection);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        string status = reader.GetInt32(0) switch { 1 => "Pending", 2 => "Satisfied", 3 => "Rejected", _ => "?" };
        return (status, reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private async Task<int> TaskStatusAsync(Guid taskId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT "Status" FROM projects.project_tasks WHERE "Id" = '{taskId}';""", connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    // ── Variation → gate ───────────────────────────────────────────────────────

    /// <summary>
    /// Raising a task-scoped variation opens a Budget gate on that task and locks it; approving the
    /// variation satisfies the gate and the ordinary unlock cascade opens the task again.
    ///
    /// <para><b>Nothing here calls a Projects handler.</b> Two Finance commands and an outbox drain
    /// produce every Projects-side change asserted.</para>
    /// </summary>
    [Fact]
    public async Task RaisingAndApprovingAVariation_OpensThenSatisfiesTheTaskBudgetGate()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid lineId = await BudgetLineAsync(scope.ProjectId, 500m, scope.StageId, scope.TaskId);

        Guid variationId;
        await using (AsyncServiceScope dbScope = fixture.CreateScope())
        {
            Result<Guid> raised = await dbScope.ServiceProvider
                .GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>()
                .HandleAsync(new RaiseVariationCommand(
                    new RaiseVariationRequest(
                        VariationKind.InternalChange, scope.ProjectId, "More materials",
                        ProjectStageId: scope.StageId, ProjectTaskId: scope.TaskId,
                        BudgetLineId: lineId, Amount: 200m),
                    owner.EmployeeId));

            Assert.True(raised.IsSuccess, raised.IsFailure ? raised.Error.ToString() : null);
            variationId = raised.Value;
        }

        await fixture.DrainOutboxAsync();

        (string Status, Guid? VariationId)? opened = await GateAsync(null, scope.TaskId);
        Assert.NotNull(opened);
        Assert.Equal("Pending", opened!.Value.Status);
        Assert.Equal(variationId, opened.Value.VariationId);

        // An Unlock-trigger gate locks its owner the moment it exists.
        Assert.Equal(TaskLocked, await TaskStatusAsync(scope.TaskId));

        await using (AsyncServiceScope dbScope = fixture.CreateScope())
        {
            IServiceProvider sp = dbScope.ServiceProvider;

            await sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>()
                .HandleAsync(new SubmitVariationCommand(variationId));

            Result approved = await sp.GetRequiredService<ICommandHandler<ApproveVariationCommand>>()
                .HandleAsync(new ApproveVariationCommand(variationId, owner.EmployeeId));

            Assert.True(approved.IsSuccess, approved.IsFailure ? approved.Error.ToString() : null);
        }

        await fixture.DrainOutboxAsync();

        (string Status, Guid? VariationId)? satisfied = await GateAsync(null, scope.TaskId);
        Assert.Equal("Satisfied", satisfied!.Value.Status);

        // The unlock cascade ran -- and it ran in Projects, off a domain event Finance never
        // knows about.
        Assert.Equal(TaskOpen, await TaskStatusAsync(scope.TaskId));
    }

    /// <summary>
    /// Rejecting a variation marks its gate Rejected and <b>deliberately leaves the task Locked</b>.
    /// The backend bypasses the gate service's reject path for exactly this reason: that path would also
    /// send the owning stage to Rejected, and the work is still blocked either way.
    /// </summary>
    [Fact]
    public async Task RejectingAVariation_RejectsTheGateAndLeavesTheOwnerLocked()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid lineId = await BudgetLineAsync(scope.ProjectId, 500m, scope.StageId, scope.TaskId);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        Guid variationId = (await sp.GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>()
            .HandleAsync(new RaiseVariationCommand(
                new RaiseVariationRequest(
                    VariationKind.InternalChange, scope.ProjectId, "Rejected raise",
                    ProjectStageId: scope.StageId, ProjectTaskId: scope.TaskId,
                    BudgetLineId: lineId, Amount: 50m),
                owner.EmployeeId))).Value;

        await fixture.DrainOutboxAsync();
        Assert.Equal(TaskLocked, await TaskStatusAsync(scope.TaskId));

        await sp.GetRequiredService<ICommandHandler<RejectVariationCommand>>()
            .HandleAsync(new RejectVariationCommand(variationId, owner.EmployeeId, "Not approved"));

        await fixture.DrainOutboxAsync();

        (string Status, Guid? VariationId)? gate = await GateAsync(null, scope.TaskId);
        Assert.Equal("Rejected", gate!.Value.Status);

        // Still Locked -- rejection does not free the work, it records that this route to funding failed.
        Assert.Equal(TaskLocked, await TaskStatusAsync(scope.TaskId));
    }

    /// <summary>
    /// A second variation on the same owner RE-LINKS the existing unlinked gate rather than opening a
    /// second one. Without that, a task with two variations would need both approved to proceed — a lock
    /// nobody asked for.
    /// </summary>
    [Fact]
    public async Task ASecondVariationOnTheSameTask_ReusesTheExistingGate()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid lineId = await BudgetLineAsync(scope.ProjectId, 500m, scope.StageId, scope.TaskId);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        ICommandHandler<RaiseVariationCommand, Guid> raise = dbScope.ServiceProvider.GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>();

        RaiseVariationRequest Request(string reason) => new(
            VariationKind.InternalChange, scope.ProjectId, reason,
            ProjectStageId: scope.StageId, ProjectTaskId: scope.TaskId,
            BudgetLineId: lineId, Amount: 25m);

        Guid first = (await raise.HandleAsync(new RaiseVariationCommand(Request("First"), owner.EmployeeId))).Value;
        await fixture.DrainOutboxAsync();

        // The first gate is now LINKED, so the second raise cannot reuse it and opens its own -- which is
        // correct: two independent funding routes, each with its own gate. The re-link path is exercised
        // by the count staying at one per LINKED variation rather than growing unlinked duplicates.
        Guid second = (await raise.HandleAsync(new RaiseVariationCommand(Request("Second"), owner.EmployeeId))).Value;
        await fixture.DrainOutboxAsync();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             SELECT count(*) FROM projects.gate_conditions
             WHERE "ProjectTaskId" = '{scope.TaskId}' AND "ConditionType" = {BudgetConditionType} AND "VariationId" IS NULL;
             """, connection);

        // No UNLINKED Budget gate is ever left behind: every one created is stamped in the same
        // handler that creates it, so a null here would mean the stamp was lost.
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        Assert.NotEqual(first, second);
    }

    // ── Coverage verdict ───────────────────────────────────────────────────────

    /// <summary>
    /// <b>The de-satisfy path, which is half of this event's purpose and the half a happy-path port
    /// drops.</b> A task whose gate is satisfied goes over budget; the coverage verdict flips to false;
    /// the gate is re-pended and the task is locked again.
    /// </summary>
    [Fact]
    public async Task GoingOverBudget_RePendsASatisfiedGateAndRelocksTheTask()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid lineId = await BudgetLineAsync(scope.ProjectId, 100m, scope.StageId, scope.TaskId);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        Guid variationId = (await sp.GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>()
            .HandleAsync(new RaiseVariationCommand(
                new RaiseVariationRequest(
                    VariationKind.InternalChange, scope.ProjectId, "Open the gate",
                    ProjectStageId: scope.StageId, ProjectTaskId: scope.TaskId,
                    BudgetLineId: lineId, Amount: 10m),
                owner.EmployeeId))).Value;

        await sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>()
            .HandleAsync(new SubmitVariationCommand(variationId));

        await sp.GetRequiredService<ICommandHandler<ApproveVariationCommand>>()
            .HandleAsync(new ApproveVariationCommand(variationId, owner.EmployeeId));

        await fixture.DrainOutboxAsync();

        Assert.Equal("Satisfied", (await GateAsync(null, scope.TaskId))!.Value.Status);
        Assert.Equal(TaskOpen, await TaskStatusAsync(scope.TaskId));

        // Now spend past the cap. 100 allocated, 250 spent, no signed client difference -> uncovered.
        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 250m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            ProjectTaskId: scope.TaskId, BudgetLineId: lineId), owner.EmployeeId);

        await fixture.DrainOutboxAsync();

        Assert.Equal("Pending", (await GateAsync(null, scope.TaskId))!.Value.Status);
        Assert.Equal(TaskLocked, await TaskStatusAsync(scope.TaskId));
    }

    /// <summary>
    /// The other side of coverage: a SIGNED CLIENT DIFFERENCE covers an overrun, so the gate satisfies
    /// without the budget line ever being raised. That asymmetry — a client difference writes nothing to
    /// the budget line and is read back as coverage instead — is the thing most likely to be "fixed"
    /// into a double count.
    /// </summary>
    [Fact]
    public async Task ASignedClientDifference_CoversAnOverrunWithoutRaisingTheBudgetLine()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        Guid lineId = await BudgetLineAsync(scope.ProjectId, 100m, scope.StageId, scope.TaskId);

        // Spend 160 against a 100 cap -> 60 over budget, uncovered.
        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 160m, LedgerDirection.Out, LedgerEntryType.ActualCost, Today,
            ProjectTaskId: scope.TaskId, BudgetLineId: lineId), owner.EmployeeId);

        await fixture.DrainOutboxAsync();

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        // A client difference of exactly 60: chosen 160 against a budgeted 100.
        Guid variationId = (await sp.GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>()
            .HandleAsync(new RaiseVariationCommand(
                new RaiseVariationRequest(
                    VariationKind.ClientDifference, scope.ProjectId, "Client picked the better finish",
                    ProjectTaskId: scope.TaskId, ProjectStageId: scope.StageId,
                    BudgetedAmount: 100m, ChosenAmount: 160m),
                owner.EmployeeId))).Value;

        await sp.GetRequiredService<ICommandHandler<SubmitVariationCommand>>()
            .HandleAsync(new SubmitVariationCommand(variationId));

        // Approval IS the signature for a client difference.
        await sp.GetRequiredService<ICommandHandler<SignVariationCommand>>()
            .HandleAsync(new SignVariationCommand(
                variationId, owner.EmployeeId, owner.ClientId, "/signed/diff.pdf"));

        await fixture.DrainOutboxAsync();

        Assert.Equal("Satisfied", (await GateAsync(null, scope.TaskId))!.Value.Status);

        // And the budget line is untouched -- the coverage came from the variation row, not from a
        // raised cap. Raising it too would credit the same 60 twice.
        Result<BudgetLineResponse> line = await sp.GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>()
            .HandleAsync(new GetBudgetLineByIdQuery(lineId));

        Assert.Equal(0m, line.Value!.AdjustmentAllocation);
        Assert.Equal(100m, line.Value.EffectiveAllocation);
    }

    // ── Stage funding ──────────────────────────────────────────────────────────

    /// <summary>
    /// Stage funding, both directions in one test. An invoice alone does not fund a stage; the payment
    /// that covers it does — and the <c>ClientInvoicesSum &gt; 0</c> half means an uninvoiced stage is
    /// never funded by default.
    /// </summary>
    [Fact]
    public async Task AStageBecomesFunded_OnlyWhenPaymentsCoverItsClientInvoices()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        // A stage-scoped variation opens the stage's Budget gate.
        Guid variationId = (await sp.GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>()
            .HandleAsync(new RaiseVariationCommand(
                new RaiseVariationRequest(
                    VariationKind.InternalChange, scope.ProjectId, "Stage funding gate",
                    ProjectStageId: scope.StageId,
                    BudgetLineId: await BudgetLineAsync(scope.ProjectId, 900m, scope.StageId),
                    Amount: 10m),
                owner.EmployeeId))).Value;

        await fixture.DrainOutboxAsync();
        Assert.Equal("Pending", (await GateAsync(scope.StageId, null))!.Value.Status);

        await sp.GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>()
            .HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                $"GF-{Guid.NewGuid().ToString("N")[..10]}", scope.ProjectId, InvoiceType.ClientInvoice,
                Today, [new InvoiceLineDto("Stage work", 1m, 400m)],
                ProjectStageId: scope.StageId, ClientId: owner.ClientId)));

        await fixture.DrainOutboxAsync();

        // Invoiced but unpaid -- still not funded, so the gate stays pending.
        Assert.Equal("Pending", (await GateAsync(scope.StageId, null))!.Value.Status);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 400m, LedgerDirection.In, LedgerEntryType.ClientPayment, Today,
            ProjectStageId: scope.StageId, ClientId: owner.ClientId), owner.EmployeeId);

        await fixture.DrainOutboxAsync();

        Assert.Equal("Satisfied", (await GateAsync(scope.StageId, null))!.Value.Status);
        Assert.NotEqual(Guid.Empty, variationId);
    }

    /// <summary>
    /// <b>The <c>ClientInvoicesSum &gt; 0</c> half of <c>IsFunded</c>, exercised directly.</b>
    ///
    /// <para>A payment posted to a stage that has never been invoiced publishes a funding verdict with
    /// <c>invoiced = 0</c> and <c>paid &gt; 0</c>. Without the <c>&gt; 0</c> clause, <c>paid &gt;= invoiced</c>
    /// is trivially true and the stage reads as FUNDED — its Budget gate would satisfy and the stage
    /// would unlock on money that was never owed for it.</para>
    ///
    /// <para>This test exists because an injection removing that clause left the whole suite green: the
    /// funding test above only ever publishes a verdict AFTER invoicing, so it never reaches the
    /// zero-invoice case. The clause needed a case that actually arrives with zero.</para>
    /// </summary>
    [Fact]
    public async Task APaymentOnAnUninvoicedStage_DoesNotFundIt()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        await sp.GetRequiredService<ICommandHandler<RaiseVariationCommand, Guid>>()
            .HandleAsync(new RaiseVariationCommand(
                new RaiseVariationRequest(
                    VariationKind.InternalChange, scope.ProjectId, "Uninvoiced stage gate",
                    ProjectStageId: scope.StageId,
                    BudgetLineId: await BudgetLineAsync(scope.ProjectId, 900m, scope.StageId),
                    Amount: 10m),
                owner.EmployeeId));

        await fixture.DrainOutboxAsync();
        Assert.Equal("Pending", (await GateAsync(scope.StageId, null))!.Value.Status);

        // A payment against the stage with NO client invoice behind it. This publishes a funding
        // verdict -- the event the previous test never produces with invoiced == 0.
        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 750m, LedgerDirection.In, LedgerEntryType.ClientPayment, Today,
            ProjectStageId: scope.StageId, ClientId: owner.ClientId), owner.EmployeeId);

        await fixture.DrainOutboxAsync();

        Assert.Equal("Pending", (await GateAsync(scope.StageId, null))!.Value.Status);
    }

    // ── The milestone assertion ────────────────────────────────────────────────

    /// <summary>
    /// <b>The Phase 7 milestone: every <c>ConditionType</c> value now has a registered handler.</b>
    ///
    /// <para><c>ConditionType.Budget</c> has been the one unhandled value since Phase 2, rejected at gate
    /// creation with <c>UnsupportedConditionType</c> because the module owning budgets did not exist.
    /// Finance is the fourth module to contribute a gate type and the one that completes the set.</para>
    ///
    /// <para><b>Asserted against the REGISTERED handlers, not a list.</b> The old error message carried a
    /// hand-maintained roster of supported types and had been wrong for two phases with nothing to catch
    /// it. Enumerating the enum and requiring a registration for each is the check that cannot go stale —
    /// a new <c>ConditionType</c> fails here on the day it is added.</para>
    /// </summary>
    [Fact]
    public void EveryConditionType_HasARegisteredHandler()
    {
        using AsyncServiceScope scope = fixture.CreateScope();

        var handled = scope.ServiceProvider.GetServices<IGateConditionTypeHandler>()
            .Select(h => h.Type)
            .ToHashSet();

        ConditionType[] all = Enum.GetValues<ConditionType>();

        // Liveness: an empty registry would make the Except below trivially empty of surprises, and the
        // whole assertion would pass while nothing was registered at all.
        Assert.NotEmpty(handled);
        Assert.NotEmpty(all);

        ConditionType[] unhandled = all.Where(t => !handled.Contains(t)).ToArray();

        Assert.True(
            unhandled.Length == 0,
            "These ConditionType values have no registered IGateConditionTypeHandler, so creating a gate "
            + "of that type fails with UnsupportedConditionType: " + string.Join(", ", unhandled));

        Assert.Contains(ConditionType.Budget, handled);
    }

    /// <summary>
    /// The eighth <see cref="IPeriodicJob"/> is registered by <c>FinanceModule</c>'s scan.
    ///
    /// <para><b>Asserted here rather than in <c>PeriodicJobTests</c></b>, which resolves from the real
    /// Api host — and <c>Program.cs</c> does not load <c>Finance.Infrastructure</c> until F1-09. This
    /// fixture boots all nine modules, so it can prove the registration now; the host-level exact-set
    /// assertion moves to eight in the same commit that registers the module.</para>
    /// </summary>
    [Fact]
    public void InvoiceDueJob_IsRegisteredByFinanceModule()
    {
        using AsyncServiceScope scope = fixture.CreateScope();

        var jobNames = scope.ServiceProvider.GetServices<IPeriodicJob>()
            .Select(job => job.Name)
            .ToList();

        Assert.NotEmpty(jobNames);
        Assert.Contains("InvoiceDueJob", jobNames);
    }
}
