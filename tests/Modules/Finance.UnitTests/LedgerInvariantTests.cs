using Finance.Domain;
using SharedKernel;
using Xunit;

namespace Finance.UnitTests;

/// <summary>
/// F1-02's verify block: the ledger invariants, not the happy path.
///
/// <para>Every assertion names the <b>specific</b> error rather than checking <c>IsFailure</c>. A bare
/// <c>Assert.True(result.IsFailure)</c> passes when the wrong guard fires, which on a double-entry
/// structure means a test that stays green while the money moves the wrong way.</para>
/// </summary>
public class LedgerInvariantTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateOnly Occurred = new(2026, 8, 19);

    private static Result<LedgerEntry> Post(
        LedgerEntryType type,
        LedgerDirection direction,
        decimal amount = 100m,
        Guid? clientId = null,
        Guid? reversalOfId = null) =>
        LedgerEntry.Create(ProjectId, amount, direction, type, Occurred, ActorId,
            clientId: clientId, reversalOfId: reversalOfId);

    // ── Amount ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-0.0001)]
    [InlineData(-100)]
    public void Create_WithNonPositiveAmount_FailsWithAmountMustBePositive(decimal amount)
    {
        var result = Post(LedgerEntryType.ActualCost, LedgerDirection.Out, amount);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerEntryErrors.AmountMustBePositive, result.Error);
    }

    /// <summary>The smallest amount the <c>numeric(19,4)</c> column can represent must be accepted —
    /// a guard written as <c>amount &lt; 1</c> or rounded to 2dp would reject it.</summary>
    [Fact]
    public void Create_WithSmallestRepresentableAmount_Succeeds()
    {
        var result = Post(LedgerEntryType.ActualCost, LedgerDirection.Out, 0.0001m);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.0001m, result.Value!.Amount);
    }

    // ── Direction ──────────────────────────────────────────────────────────────

    [Fact]
    public void ClientPayment_MustBeInbound()
    {
        var wrong = Post(LedgerEntryType.ClientPayment, LedgerDirection.Out, clientId: Guid.NewGuid());

        Assert.True(wrong.IsFailure);
        Assert.Equal(LedgerEntryErrors.InvalidDirection, wrong.Error);

        var right = Post(LedgerEntryType.ClientPayment, LedgerDirection.In, clientId: Guid.NewGuid());
        Assert.True(right.IsSuccess);
    }

    /// <summary>A [Theory] cannot carry these as InlineData: the enum is internal to Finance.Domain
    /// and a public test method may not expose it as a parameter. Both cases run here instead.</summary>
    [Fact]
    public void NonPaymentTypes_MustBeOutbound()
    {
        foreach (var type in new[] { LedgerEntryType.Commitment, LedgerEntryType.ActualCost })
        {
            var wrong = Post(type, LedgerDirection.In);

            Assert.True(wrong.IsFailure);
            Assert.Equal(LedgerEntryErrors.InvalidDirection, wrong.Error);

            Assert.True(Post(type, LedgerDirection.Out).IsSuccess);
        }
    }

    // ── Party ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ClientPayment_WithoutAClient_FailsWithInvalidPartyForType()
    {
        var result = Post(LedgerEntryType.ClientPayment, LedgerDirection.In, clientId: null);

        Assert.True(result.IsFailure);
        // Not InvalidDirection - the direction here is correct, and conflating the two would let a
        // direction bug hide behind a party assertion.
        Assert.Equal(LedgerEntryErrors.InvalidPartyForType, result.Error);
    }

    [Fact]
    public void NonPaymentTypes_NeedNoParty()
    {
        Assert.True(Post(LedgerEntryType.ActualCost, LedgerDirection.Out).IsSuccess);
        Assert.True(Post(LedgerEntryType.Commitment, LedgerDirection.Out).IsSuccess);
    }

    // ── Reversal ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A reversal is exempt from both invariants by construction. Asserted as the pair of cases that
    /// would otherwise fail: an inbound non-payment and an outbound client payment with no client.
    /// Without the exemption a client payment could never be reversed at all.
    /// </summary>
    [Fact]
    public void AReversal_IsExemptFromDirectionAndPartyRules()
    {
        var originalId = Guid.NewGuid();

        var inboundActualCost = Post(LedgerEntryType.ActualCost, LedgerDirection.In, reversalOfId: originalId);
        Assert.True(inboundActualCost.IsSuccess);

        var outboundPaymentWithNoClient = Post(
            LedgerEntryType.ClientPayment, LedgerDirection.Out, clientId: null, reversalOfId: originalId);
        Assert.True(outboundPaymentWithNoClient.IsSuccess);

        // The same two shapes without ReversalOfId must still fail - otherwise this test would pass
        // against an entity that had simply dropped both invariants.
        Assert.Equal(
            LedgerEntryErrors.InvalidDirection,
            Post(LedgerEntryType.ActualCost, LedgerDirection.In).Error);
        Assert.Equal(
            LedgerEntryErrors.InvalidPartyForType,
            Post(LedgerEntryType.ClientPayment, LedgerDirection.In, clientId: null).Error);
    }

    /// <summary>Reversal is <b>exact</b>: same magnitude and scope, opposite direction. Amount is never
    /// negated — the sign lives in <c>Direction</c>.</summary>
    [Fact]
    public void CreateReversal_MirrorsTheOriginalExactly()
    {
        var clientId = Guid.NewGuid();

        // Explicitly historical rather than the shared Occurred constant, which is "today" and would
        // make the NotEqual below pass or fail depending on the date the suite runs.
        var longAgo = new DateOnly(2020, 3, 4);

        var original = LedgerEntry.Create(
            ProjectId, 1234.5678m, LedgerDirection.In, LedgerEntryType.ClientPayment, longAgo, ActorId,
            projectStageId: Guid.NewGuid(), projectTaskId: Guid.NewGuid(), budgetLineId: Guid.NewGuid(),
            method: PaymentMethod.BankTransfer, clientId: clientId, reference: "REF-1").Value!;

        var reversal = LedgerEntry.CreateReversal(original, ActorId, "undo").Value!;

        Assert.Equal(original.Amount, reversal.Amount);
        Assert.Equal(LedgerDirection.Out, reversal.Direction);
        Assert.Equal(original.Type, reversal.Type);
        Assert.Equal(original.Id, reversal.ReversalOfId);
        Assert.True(reversal.IsReversal);

        // Scope travels, so the reversal's budget-line effects land on the same line as the original.
        Assert.Equal(original.ProjectId, reversal.ProjectId);
        Assert.Equal(original.ProjectStageId, reversal.ProjectStageId);
        Assert.Equal(original.ProjectTaskId, reversal.ProjectTaskId);
        Assert.Equal(original.BudgetLineId, reversal.BudgetLineId);
        Assert.Equal(original.ClientId, reversal.ClientId);

        // CHANGED AT F1-07, and the change is to the ASSERTION as well as the code. This previously
        // asserted the reversal carried the original's OccurredOn -- which is what F1-02 implemented
        // and what reading LedgerEntryDomainService.ReverseAsync showed to be wrong. A reversal is a new
        // event dated when it is posted; backdating it restates a period that may already be reported.
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), reversal.OccurredOn);
        Assert.NotEqual(original.OccurredOn, reversal.OccurredOn);

        // Reference is the source key the posting dedupe reads, and a reversal is not another posting of
        // that source. Method is dropped for the same class of reason: reversing a bank transfer is not
        // itself a bank transfer.
        Assert.Null(reversal.Reference);
        Assert.Null(reversal.Method);
    }

    [Fact]
    public void ReversingAReversal_FlipsBackToTheOriginalDirection()
    {
        var original = Post(LedgerEntryType.ActualCost, LedgerDirection.Out).Value!;
        var reversal = LedgerEntry.CreateReversal(original, ActorId).Value!;
        var reReversal = LedgerEntry.CreateReversal(reversal, ActorId).Value!;

        Assert.Equal(LedgerDirection.In, reversal.Direction);
        Assert.Equal(LedgerDirection.Out, reReversal.Direction);
        Assert.Equal(reversal.Id, reReversal.ReversalOfId);
    }

    // ── Allocation ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Allocation_WithNonPositiveAmount_Fails(decimal amount)
    {
        var result = LedgerAllocation.Create(Guid.NewGuid(), Guid.NewGuid(), amount);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerAllocationErrors.AmountMustBePositive, result.Error);
    }

    /// <summary>
    /// The allocation bound: the sum may reach the entry amount exactly but never pass it. Boundary
    /// cases on both sides of equality, because "&gt;" versus "&gt;=" here is the difference between
    /// allowing a full settlement and forbidding it.
    /// </summary>
    [Theory]
    [InlineData(100, 0, 100, false)]      // exact full allocation - allowed
    [InlineData(100, 60, 40, false)]      // exact top-up to full - allowed
    [InlineData(100, 60, 40.0001, true)]  // one ten-thousandth over - rejected
    [InlineData(100, 0, 100.0001, true)]
    [InlineData(100, 100, 0.0001, true)]  // already fully allocated
    [InlineData(100, 30, 20, false)]
    public void WouldExceedUnallocated_BoundsTheSetNotTheRow(
        decimal entryAmount, decimal alreadyAllocated, decimal requested, bool expectedExceeds)
    {
        Assert.Equal(
            expectedExceeds,
            LedgerAllocation.WouldExceedUnallocated(entryAmount, alreadyAllocated, requested));
    }
}
