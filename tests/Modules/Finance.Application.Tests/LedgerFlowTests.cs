using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts;
using Finance.Contracts.BudgetLines;
using Finance.Contracts.CostCategories;
using Finance.Contracts.Invoices;
using Finance.Contracts.LedgerEntries;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

[Collection(FinanceApplicationCollection.Name)]
public sealed class LedgerFlowTests(FinanceApplicationFixture fixture)
{
    private async Task<Guid> CategoryAsync()
    {
        await using var scope = fixture.CreateScope();
        return (await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(
                new CreateCostCategoryRequest($"LC{Guid.NewGuid().ToString("N")[..8]}", "Materials"))))
            .Value;
    }

    private async Task<Guid> BudgetLineAsync(Guid projectId, decimal allocated)
    {
        var categoryId = await CategoryAsync();
        await using var scope = fixture.CreateScope();
        var created = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateBudgetLineCommand, Guid>>()
            .HandleAsync(new CreateBudgetLineCommand(new CreateBudgetLineRequest(
                "LB", projectId, categoryId, "Ledger budget", allocated)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private async Task<Result<Guid>> PostAsync(PostLedgerEntryRequest request, Guid recordedById)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<PostLedgerEntryCommand, Guid>>()
            .HandleAsync(new PostLedgerEntryCommand(request, recordedById));
    }

    private async Task<BudgetLineResponse> ReadLineAsync(Guid id)
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>>()
            .HandleAsync(new GetBudgetLineByIdQuery(id));

        Assert.True(read.IsSuccess, read.IsFailure ? read.Error.ToString() : null);
        return read.Value!;
    }

    // ── Posting ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ACommitment_RaisesCommittedAndLowersAvailable()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 1000m);

        var posted = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 400m, LedgerDirection.Out, LedgerEntryType.Commitment,
            DateOnly.FromDateTime(DateTime.UtcNow), BudgetLineId: lineId), scope.EmployeeId);

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.ToString() : null);

        var line = await ReadLineAsync(lineId);
        Assert.Equal(400m, line.CommittedAmount);
        Assert.Equal(0m, line.ActualAmount);
        Assert.Equal(600m, line.AvailableAmount);
    }

    /// <summary>
    /// <b>The commitment-to-actual handoff, through the real posting path.</b> An actual cost raises
    /// Actual AND consumes the commitment that funded it, so Available is unchanged when the actual
    /// exactly matches its commitment. Doing only the Actual half — the classic mis-port — would leave
    /// Available at 600 here instead of 1000-400 = ... it would show 200, double-counting the spend.
    /// </summary>
    [Fact]
    public async Task Post_AnActualCost_MovesCommittedIntoActual_LeavingAvailableUnchanged()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 1000m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 400m, LedgerDirection.Out, LedgerEntryType.Commitment, today,
            BudgetLineId: lineId), scope.EmployeeId);

        var afterCommitment = await ReadLineAsync(lineId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 400m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            BudgetLineId: lineId), scope.EmployeeId);

        var afterActual = await ReadLineAsync(lineId);

        Assert.Equal(0m, afterActual.CommittedAmount);
        Assert.Equal(400m, afterActual.ActualAmount);
        Assert.Equal(afterCommitment.AvailableAmount, afterActual.AvailableAmount);
        Assert.Equal(600m, afterActual.AvailableAmount);
    }

    /// <summary>
    /// The budget gate. A commitment beyond what the line still has available is refused — this is what
    /// makes <c>AvailableAmount</c> a limit rather than a number that drifts negative.
    /// </summary>
    [Fact]
    public async Task Post_ACommitmentBeyondAvailable_FailsWithInsufficientBudget()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 100m);

        var posted = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 100.01m, LedgerDirection.Out, LedgerEntryType.Commitment,
            DateOnly.FromDateTime(DateTime.UtcNow), BudgetLineId: lineId), scope.EmployeeId);

        Assert.True(posted.IsFailure);
        Assert.Equal("ledger_entries.InsufficientBudget", posted.Error.Code);

        // And nothing moved -- a refused posting must leave the line exactly as it was.
        var line = await ReadLineAsync(lineId);
        Assert.Equal(0m, line.CommittedAmount);
        Assert.Equal(100m, line.AvailableAmount);
    }

    /// <summary>Exactly available is allowed. The bound is "greater than", not "greater than or equal",
    /// and spending a budget line down to zero is the normal case rather than an overflow.</summary>
    [Fact]
    public async Task Post_ACommitmentExactlyAtAvailable_Succeeds()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 100m);

        var posted = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 100m, LedgerDirection.Out, LedgerEntryType.Commitment,
            DateOnly.FromDateTime(DateTime.UtcNow), BudgetLineId: lineId), scope.EmployeeId);

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.ToString() : null);
        Assert.Equal(0m, (await ReadLineAsync(lineId)).AvailableAmount);
    }

    /// <summary>
    /// An ACTUAL cost is not gated: it records money that has already moved, and refusing to record it
    /// would make the ledger disagree with reality. Only commitments are refused.
    ///
    /// <para><b>And the surprising half, which this test exists to pin.</b> An uncommitted actual cost
    /// does NOT drive <c>AvailableAmount</c> negative — it does not change Available at all.
    /// <c>ApplyActualCost</c> moves Committed and Actual by the same delta in opposite directions, so
    /// <c>Effective − Committed − Actual</c> is invariant under it; the overspend surfaces as a NEGATIVE
    /// <c>CommittedAmount</c> instead. That is exactly why every over-budget check in this module —
    /// the BOQ difference, the dashboard's over-budget list, the Budget gate's coverage rule — is
    /// written as <c>Actual &gt; EffectiveCap</c> and never as <c>Available &lt; 0</c>. The second form
    /// looks equivalent and would report nothing.</para>
    /// </summary>
    [Fact]
    public async Task Post_AnActualCostBeyondAvailable_IsAllowed_AndLeavesAvailableUnchanged()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 100m);

        var before = await ReadLineAsync(lineId);

        var posted = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 250m, LedgerDirection.Out, LedgerEntryType.ActualCost,
            DateOnly.FromDateTime(DateTime.UtcNow), BudgetLineId: lineId), scope.EmployeeId);

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.ToString() : null);

        var line = await ReadLineAsync(lineId);
        Assert.Equal(250m, line.ActualAmount);
        Assert.Equal(-250m, line.CommittedAmount);

        Assert.Equal(before.AvailableAmount, line.AvailableAmount);
        Assert.Equal(100m, line.AvailableAmount);

        // The overrun is visible against the cap, which is the comparison every consumer actually uses.
        Assert.True(line.ActualAmount > line.EffectiveAllocation);
    }

    [Fact]
    public async Task Post_AClientPaymentWithoutAClient_FailsWithInvalidPartyForType()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);

        var posted = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 50m, LedgerDirection.In, LedgerEntryType.ClientPayment,
            DateOnly.FromDateTime(DateTime.UtcNow)), scope.EmployeeId);

        Assert.True(posted.IsFailure);
        Assert.Equal("ledger_entries.InvalidPartyForType", posted.Error.Code);
    }

    [Fact]
    public async Task Post_ACommitmentInbound_FailsWithInvalidDirection()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);

        var posted = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 50m, LedgerDirection.In, LedgerEntryType.Commitment,
            DateOnly.FromDateTime(DateTime.UtcNow)), scope.EmployeeId);

        Assert.True(posted.IsFailure);
        Assert.Equal("ledger_entries.InvalidDirection", posted.Error.Code);
    }

    // ── Dedupe ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_TheSameReferenceTwiceInOneProject_FailsWithDuplicatePosting()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var reference = $"REF-{Guid.NewGuid().ToString("N")[..8]}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 10m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            Reference: reference), scope.EmployeeId);
        Assert.True(first.IsSuccess);

        var second = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 10m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            Reference: reference), scope.EmployeeId);

        Assert.True(second.IsFailure);
        Assert.Equal("ledger_entries.DuplicatePosting", second.Error.Code);
    }

    /// <summary>
    /// DIVERGENCE, and the reason for it. The backend's dedupe is global, so two projects both posting
    /// a reference of <c>"MILESTONE-1"</c> collide and the second is rejected. A reference is free text
    /// whose meaning is local to its project. Scoped here — and this test is the one that fails if
    /// someone "simplifies" the project predicate back out.
    /// </summary>
    [Fact]
    public async Task Post_TheSameReferenceInADifferentProject_Succeeds()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var other = await FinanceTestData.IsolatedProjectAsync(fixture);
        var reference = $"MILESTONE-{Guid.NewGuid().ToString("N")[..8]}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 10m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            Reference: reference), scope.EmployeeId);
        Assert.True(first.IsSuccess);

        var second = await PostAsync(new PostLedgerEntryRequest(
            other.ProjectId, 10m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            Reference: reference), scope.EmployeeId);

        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.ToString() : null);
    }

    /// <summary>An entry naming neither a variation nor a reference has no source to dedupe on, so
    /// identical postings are legitimately distinct movements.</summary>
    [Fact]
    public async Task Post_TwoIdenticalEntriesWithNoSource_BothSucceed()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var request = new PostLedgerEntryRequest(
            scope.ProjectId, 25m, LedgerDirection.Out, LedgerEntryType.ActualCost, today);

        Assert.True((await PostAsync(request, scope.EmployeeId)).IsSuccess);
        Assert.True((await PostAsync(request, scope.EmployeeId)).IsSuccess);
    }

    // ── Reversal ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A reversal replays the original's effects with the sign negated, so the budget line returns to
    /// exactly where it started. That the SAME method computes both directions is what guarantees it —
    /// a hand-written inverse gets the commitment-to-actual handoff wrong in one direction eventually.
    /// </summary>
    [Fact]
    public async Task Reverse_AnActualCost_RestoresTheBudgetLineExactly()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 1000m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 300m, LedgerDirection.Out, LedgerEntryType.Commitment, today,
            BudgetLineId: lineId), scope.EmployeeId);

        var before = await ReadLineAsync(lineId);

        var actualId = (await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 120m, LedgerDirection.Out, LedgerEntryType.ActualCost, today,
            BudgetLineId: lineId), scope.EmployeeId)).Value;

        await using (var dbScope = fixture.CreateScope())
        {
            var reversed = await dbScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReverseLedgerEntryCommand, Guid>>()
                .HandleAsync(new ReverseLedgerEntryCommand(actualId, scope.EmployeeId, "mistake"));

            Assert.True(reversed.IsSuccess, reversed.IsFailure ? reversed.Error.ToString() : null);
        }

        var after = await ReadLineAsync(lineId);

        Assert.Equal(before.CommittedAmount, after.CommittedAmount);
        Assert.Equal(before.ActualAmount, after.ActualAmount);
        Assert.Equal(before.AvailableAmount, after.AvailableAmount);
    }

    [Fact]
    public async Task Reverse_Twice_FailsWithAlreadyReversed()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var entryId = (await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 60m, LedgerDirection.Out, LedgerEntryType.ActualCost, today),
            scope.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var reverse = dbScope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReverseLedgerEntryCommand, Guid>>();

        Assert.True((await reverse.HandleAsync(
            new ReverseLedgerEntryCommand(entryId, scope.EmployeeId))).IsSuccess);

        var second = await reverse.HandleAsync(new ReverseLedgerEntryCommand(entryId, scope.EmployeeId));

        Assert.True(second.IsFailure);
        Assert.Equal("ledger_entries.AlreadyReversed", second.Error.Code);
    }

    /// <summary>
    /// The reversal is dated TODAY, not the original's date. Corrected at F1-07 after reading the
    /// backend's own reversal path — F1-02 carried the original date, which backdates a correction into
    /// a period that may already have been reported.
    /// </summary>
    [Fact]
    public async Task Reverse_StampsTheReversalWithTodaysDate_NotTheOriginals()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var longAgo = new DateOnly(2020, 3, 4);

        var entryId = (await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 75m, LedgerDirection.Out, LedgerEntryType.ActualCost, longAgo),
            scope.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var mirrorId = (await dbScope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReverseLedgerEntryCommand, Guid>>()
            .HandleAsync(new ReverseLedgerEntryCommand(entryId, scope.EmployeeId))).Value;

        var read = dbScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetLedgerEntryByIdQuery, LedgerEntryResponse>>();

        var original = (await read.HandleAsync(new GetLedgerEntryByIdQuery(entryId))).Value!;
        var mirror = (await read.HandleAsync(new GetLedgerEntryByIdQuery(mirrorId))).Value!;

        Assert.Equal(longAgo, original.OccurredOn);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), mirror.OccurredOn);
        Assert.Equal(entryId, mirror.ReversalOfId);
        Assert.Equal("In", mirror.Direction);
        Assert.Equal("Out", original.Direction);
    }

    /// <summary>Reversing after a reversal is legitimate re-instatement, and the direction flips back.
    /// This is why <c>AlreadyReversed</c> keys on "has a mirror" rather than "is a reversal".</summary>
    [Fact]
    public async Task Reverse_AReversal_IsAllowed_AndFlipsTheDirectionBack()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var entryId = (await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 33m, LedgerDirection.Out, LedgerEntryType.ActualCost, today),
            scope.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var reverse = dbScope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReverseLedgerEntryCommand, Guid>>();

        var mirrorId = (await reverse.HandleAsync(
            new ReverseLedgerEntryCommand(entryId, scope.EmployeeId))).Value;

        var reInstated = await reverse.HandleAsync(
            new ReverseLedgerEntryCommand(mirrorId, scope.EmployeeId));

        Assert.True(reInstated.IsSuccess, reInstated.IsFailure ? reInstated.Error.ToString() : null);

        var read = await dbScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetLedgerEntryByIdQuery, LedgerEntryResponse>>()
            .HandleAsync(new GetLedgerEntryByIdQuery(reInstated.Value));

        Assert.Equal("Out", read.Value!.Direction);
    }

    // ── Allocation ─────────────────────────────────────────────────────────────

    private async Task<Guid> ClientInvoiceAsync(Guid projectId, Guid clientId, decimal amount)
    {
        await using var scope = fixture.CreateScope();
        var created = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>()
            .HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                $"INV-{Guid.NewGuid().ToString("N")[..10]}", projectId, InvoiceType.ClientInvoice,
                DateOnly.FromDateTime(DateTime.UtcNow),
                [new InvoiceLineDto("Work", 1m, amount)],
                ClientId: clientId)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private async Task<Guid> ClientPaymentAsync(Guid projectId, Guid clientId, decimal amount, Guid actorId)
    {
        var posted = await PostAsync(new PostLedgerEntryRequest(
            projectId, amount, LedgerDirection.In, LedgerEntryType.ClientPayment,
            DateOnly.FromDateTime(DateTime.UtcNow), ClientId: clientId), actorId);

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.ToString() : null);
        return posted.Value;
    }

    [Fact]
    public async Task Allocate_ReducesTheInvoiceBalance_AndFlipsToPaidWhenItReachesZero()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var invoiceId = await ClientInvoiceAsync(scope.ProjectId, scope.ClientId, 500m);
        var paymentId = await ClientPaymentAsync(scope.ProjectId, scope.ClientId, 500m, scope.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;

        var allocated = await sp.GetRequiredService<ICommandHandler<AllocateLedgerEntryCommand>>()
            .HandleAsync(new AllocateLedgerEntryCommand(paymentId, invoiceId, 500m, scope.EmployeeId));

        Assert.True(allocated.IsSuccess, allocated.IsFailure ? allocated.Error.ToString() : null);

        var invoice = await sp.GetRequiredService<IQueryHandler<GetInvoiceByIdQuery, InvoiceResponse>>()
            .HandleAsync(new GetInvoiceByIdQuery(invoiceId));

        Assert.Equal(500m, invoice.Value!.PaidAmount);
        Assert.Equal(0m, invoice.Value.Balance);
        Assert.Equal("Paid", invoice.Value.Status);
    }

    /// <summary>
    /// <b>The payment-side bound.</b> One payment cannot be spread across invoices beyond its own
    /// amount. This is the complement of the invoice-side bound below, and neither implies the other.
    /// </summary>
    [Fact]
    public async Task Allocate_BeyondThePaymentsUnallocatedAmount_FailsWithExceedsUnallocated()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var firstInvoice = await ClientInvoiceAsync(scope.ProjectId, scope.ClientId, 300m);
        var secondInvoice = await ClientInvoiceAsync(scope.ProjectId, scope.ClientId, 300m);
        var paymentId = await ClientPaymentAsync(scope.ProjectId, scope.ClientId, 400m, scope.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var allocate = dbScope.ServiceProvider
            .GetRequiredService<ICommandHandler<AllocateLedgerEntryCommand>>();

        Assert.True((await allocate.HandleAsync(
            new AllocateLedgerEntryCommand(paymentId, firstInvoice, 300m, scope.EmployeeId))).IsSuccess);

        // 300 already allocated of a 400 payment; 150 more would exceed it.
        var second = await allocate.HandleAsync(
            new AllocateLedgerEntryCommand(paymentId, secondInvoice, 150m, scope.EmployeeId));

        Assert.True(second.IsFailure);
        Assert.Equal("ledger_allocations.ExceedsUnallocated", second.Error.Code);
    }

    /// <summary>
    /// <b>The invoice-side bound.</b> Several payments cannot over-pay one invoice. Bounded from the
    /// other direction than the test above, and a port keeping only one of the two leaves the other kind
    /// of double-count possible.
    /// </summary>
    [Fact]
    public async Task Allocate_BeyondTheInvoiceBalance_FailsWithOverAllocation()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var invoiceId = await ClientInvoiceAsync(scope.ProjectId, scope.ClientId, 200m);
        var firstPayment = await ClientPaymentAsync(scope.ProjectId, scope.ClientId, 150m, scope.EmployeeId);
        var secondPayment = await ClientPaymentAsync(scope.ProjectId, scope.ClientId, 150m, scope.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var allocate = dbScope.ServiceProvider
            .GetRequiredService<ICommandHandler<AllocateLedgerEntryCommand>>();

        Assert.True((await allocate.HandleAsync(
            new AllocateLedgerEntryCommand(firstPayment, invoiceId, 150m, scope.EmployeeId))).IsSuccess);

        // The second payment has 150 unallocated, so the payment-side bound permits this; only the
        // invoice's remaining 50 balance refuses it.
        var second = await allocate.HandleAsync(
            new AllocateLedgerEntryCommand(secondPayment, invoiceId, 150m, scope.EmployeeId));

        Assert.True(second.IsFailure);
        Assert.Equal("invoices.OverAllocation", second.Error.Code);
    }

    /// <summary>DIVERGENCE: the backend defines <c>NotAPayment</c> and never raises it, so a COMMITMENT
    /// can be allocated against an invoice and mark it paid with money that has not moved.</summary>
    [Fact]
    public async Task Allocate_ACommitment_FailsWithNotAPayment()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var lineId = await BudgetLineAsync(scope.ProjectId, 1000m);
        var invoiceId = await ClientInvoiceAsync(scope.ProjectId, scope.ClientId, 100m);

        var commitmentId = (await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 100m, LedgerDirection.Out, LedgerEntryType.Commitment,
            DateOnly.FromDateTime(DateTime.UtcNow), BudgetLineId: lineId), scope.EmployeeId)).Value;

        await using var dbScope = fixture.CreateScope();
        var allocated = await dbScope.ServiceProvider
            .GetRequiredService<ICommandHandler<AllocateLedgerEntryCommand>>()
            .HandleAsync(new AllocateLedgerEntryCommand(commitmentId, invoiceId, 100m, scope.EmployeeId));

        Assert.True(allocated.IsFailure);
        Assert.Equal("ledger_entries.NotAPayment", allocated.Error.Code);
    }

    /// <summary>Partial allocation is the normal case: one payment settling part of one invoice, with
    /// both sides left with a remainder that further allocations can use.</summary>
    [Fact]
    public async Task Allocate_PartiallyLeavesBothSidesOpen()
    {
        var scope = await FinanceTestData.ScopeAsync(fixture);
        var invoiceId = await ClientInvoiceAsync(scope.ProjectId, scope.ClientId, 800m);
        var paymentId = await ClientPaymentAsync(scope.ProjectId, scope.ClientId, 500m, scope.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;

        await sp.GetRequiredService<ICommandHandler<AllocateLedgerEntryCommand>>()
            .HandleAsync(new AllocateLedgerEntryCommand(paymentId, invoiceId, 300m, scope.EmployeeId));

        var invoice = (await sp.GetRequiredService<IQueryHandler<GetInvoiceByIdQuery, InvoiceResponse>>()
            .HandleAsync(new GetInvoiceByIdQuery(invoiceId))).Value!;

        Assert.Equal(300m, invoice.PaidAmount);
        Assert.Equal(500m, invoice.Balance);
        Assert.Equal("Open", invoice.Status);

        var unallocated = await sp.GetRequiredService<
                IQueryHandler<GetUnallocatedLedgerEntriesQuery, PagedResult<UnallocatedLedgerEntryResponse>>>()
            .HandleAsync(new GetUnallocatedLedgerEntriesQuery(scope.ProjectId));

        var row = Assert.Single(unallocated.Value!.Items, e => e.Id == paymentId);
        Assert.Equal(500m, row.Amount);
        Assert.Equal(200m, row.UnallocatedAmount);
    }

    /// <summary>A fully-allocated payment drops off the unallocated worklist. Paired with the positive
    /// assertion above, so "absent" means allocated rather than never present.</summary>
    [Fact]
    public async Task GetUnallocated_ExcludesAFullyAllocatedPayment()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var invoiceId = await ClientInvoiceAsync(scope.ProjectId, owner.ClientId, 90m);
        var paymentId = await ClientPaymentAsync(scope.ProjectId, owner.ClientId, 90m, owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;
        var list = sp.GetRequiredService<
            IQueryHandler<GetUnallocatedLedgerEntriesQuery, PagedResult<UnallocatedLedgerEntryResponse>>>();

        var before = await list.HandleAsync(new GetUnallocatedLedgerEntriesQuery(scope.ProjectId));
        Assert.Contains(before.Value!.Items, e => e.Id == paymentId);

        await sp.GetRequiredService<ICommandHandler<AllocateLedgerEntryCommand>>()
            .HandleAsync(new AllocateLedgerEntryCommand(paymentId, invoiceId, 90m, owner.EmployeeId));

        var after = await list.HandleAsync(new GetUnallocatedLedgerEntriesQuery(scope.ProjectId));
        Assert.DoesNotContain(after.Value!.Items, e => e.Id == paymentId);
    }

    /// <summary>DIVERGENCE: a reversed payment has no money left to allocate, so offering it on the
    /// worklist would let a user settle an invoice against a movement that has been undone. The backend
    /// does not exclude these.</summary>
    [Fact]
    public async Task GetUnallocated_ExcludesAReversedPaymentAndItsMirror()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);
        var paymentId = await ClientPaymentAsync(scope.ProjectId, owner.ClientId, 70m, owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var sp = dbScope.ServiceProvider;
        var list = sp.GetRequiredService<
            IQueryHandler<GetUnallocatedLedgerEntriesQuery, PagedResult<UnallocatedLedgerEntryResponse>>>();

        var before = await list.HandleAsync(new GetUnallocatedLedgerEntriesQuery(scope.ProjectId));
        Assert.Contains(before.Value!.Items, e => e.Id == paymentId);

        var mirrorId = (await sp.GetRequiredService<ICommandHandler<ReverseLedgerEntryCommand, Guid>>()
            .HandleAsync(new ReverseLedgerEntryCommand(paymentId, owner.EmployeeId))).Value;

        var after = await list.HandleAsync(new GetUnallocatedLedgerEntriesQuery(scope.ProjectId));
        Assert.DoesNotContain(after.Value!.Items, e => e.Id == paymentId);
        Assert.DoesNotContain(after.Value!.Items, e => e.Id == mirrorId);
    }

    // ── Reads ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByProject_FiltersByTypeAndDateWindow()
    {
        var scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        var owner = await FinanceTestData.ScopeAsync(fixture);

        var old = new DateOnly(2021, 1, 1);
        var recent = new DateOnly(2026, 6, 1);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 10m, LedgerDirection.Out, LedgerEntryType.ActualCost, old), owner.EmployeeId);

        await PostAsync(new PostLedgerEntryRequest(
            scope.ProjectId, 20m, LedgerDirection.Out, LedgerEntryType.Commitment, recent), owner.EmployeeId);

        await using var dbScope = fixture.CreateScope();
        var list = dbScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetLedgerEntriesByProjectQuery, PagedResult<LedgerEntryListItemResponse>>>();

        var all = await list.HandleAsync(new GetLedgerEntriesByProjectQuery(scope.ProjectId));
        Assert.Equal(2, all.Value!.TotalCount);

        var commitments = await list.HandleAsync(
            new GetLedgerEntriesByProjectQuery(scope.ProjectId, Type: LedgerEntryType.Commitment));
        Assert.Single(commitments.Value!.Items);
        Assert.Equal("Commitment", commitments.Value.Items[0].Type);

        var windowed = await list.HandleAsync(new GetLedgerEntriesByProjectQuery(
            scope.ProjectId, FromDate: new DateOnly(2026, 1, 1)));
        Assert.Single(windowed.Value!.Items);
        Assert.Equal(recent, windowed.Value.Items[0].OccurredOn);
    }

    [Fact]
    public async Task GetById_ForAnUnknownEntry_FailsWithNotFound()
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetLedgerEntryByIdQuery, LedgerEntryResponse>>()
            .HandleAsync(new GetLedgerEntryByIdQuery(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("ledger_entries.NotFound", read.Error.Code);
    }
}
