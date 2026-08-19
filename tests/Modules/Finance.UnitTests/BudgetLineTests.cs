using Finance.Domain;
using Xunit;

namespace Finance.UnitTests;

/// <summary>
/// The four money columns and the arithmetic that moves them. The commitment-to-actual handoff is the
/// case worth the most here: it is one fact expressed as two column moves, and porting it as two
/// independent operations double-counts every spend against the budget forever.
/// </summary>
public class BudgetLineTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static BudgetLine Line(decimal allocated = 1000m) =>
        BudgetLine.Create("Joinery", allocated, ProjectId).Value!;

    // ── Creation ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAName_FailsWithNameRequired(string name)
    {
        var result = BudgetLine.Create(name, 100m, ProjectId);

        Assert.True(result.IsFailure);
        Assert.Equal(BudgetLineErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Create_WithNegativeAllocation_FailsWithNegativeAllocation()
    {
        var result = BudgetLine.Create("Joinery", -0.0001m, ProjectId);

        Assert.True(result.IsFailure);
        Assert.Equal(BudgetLineErrors.NegativeAllocation, result.Error);
    }

    /// <summary>Zero is a legitimate allocation — a line can be opened before its number is known.</summary>
    [Fact]
    public void Create_WithZeroAllocation_Succeeds()
    {
        Assert.True(BudgetLine.Create("Joinery", 0m, ProjectId).IsSuccess);
    }

    [Fact]
    public void Create_TaskScopedWithoutAStage_FailsWithTaskStageMismatch()
    {
        var result = BudgetLine.Create("Joinery", 100m, ProjectId, projectTaskId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(BudgetLineErrors.TaskStageMismatch, result.Error);
    }

    [Fact]
    public void Create_TaskScopedWithAStage_Succeeds()
    {
        var result = BudgetLine.Create(
            "Joinery", 100m, ProjectId, projectStageId: Guid.NewGuid(), projectTaskId: Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_StartsAggregatesAtZero()
    {
        var line = Line(1000m);

        Assert.Equal(0m, line.AdjustmentAllocation);
        Assert.Equal(0m, line.CommittedAmount);
        Assert.Equal(0m, line.ActualAmount);
        Assert.Equal(1000m, line.EffectiveAllocation);
        Assert.Equal(1000m, line.AvailableAmount);
    }

    // ── Derived values ─────────────────────────────────────────────────────────

    [Fact]
    public void EffectiveAllocation_IsAllocatedPlusAdjustment_AndAvailableSubtractsBothSpends()
    {
        var line = Line(1000m);
        line.ApplyAdjustment(250m);
        line.ApplyCommitment(400m);
        line.ApplyActualCost(100m);

        // Adjustment raises the ceiling; the actual cost consumed 100 of the commitment.
        Assert.Equal(1250m, line.EffectiveAllocation);
        Assert.Equal(300m, line.CommittedAmount);
        Assert.Equal(100m, line.ActualAmount);
        Assert.Equal(850m, line.AvailableAmount);
    }

    // ── Adjustment ─────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyAdjustment_PreservesTheOriginalAllocation()
    {
        var line = Line(1000m);
        line.ApplyAdjustment(250m);

        Assert.Equal(1000m, line.AllocatedAmount);
        Assert.Equal(250m, line.AdjustmentAllocation);
    }

    /// <summary>Negative deltas are accepted so a reversal is the same operation with the sign flipped.</summary>
    [Fact]
    public void ApplyAdjustment_AcceptsNegativeDeltas_AndIsExactlyReversible()
    {
        var line = Line(1000m);
        line.ApplyAdjustment(250.5555m);
        line.ApplyAdjustment(-250.5555m);

        Assert.Equal(0m, line.AdjustmentAllocation);
        Assert.Equal(1000m, line.EffectiveAllocation);
    }

    [Fact]
    public void ApplyAdjustment_WithZero_IsANoOp()
    {
        var line = Line(1000m);
        line.ApplyAdjustment(0m);

        Assert.Equal(0m, line.AdjustmentAllocation);
        Assert.Null(line.UpdatedAtUtc);
    }

    // ── The commitment-to-actual handoff ───────────────────────────────────────

    /// <summary>
    /// The load-bearing case. An actual cost that consumes an earlier commitment of the same size must
    /// leave <c>AvailableAmount</c> unchanged: the money was already reserved, and spending it is not a
    /// second consumption of the budget.
    /// </summary>
    [Fact]
    public void ActualCost_ConsumesItsCommitment_LeavingAvailableUnchanged()
    {
        var line = Line(1000m);

        line.ApplyCommitment(300m);
        var availableAfterCommitment = line.AvailableAmount;

        line.ApplyActualCost(300m);

        Assert.Equal(availableAfterCommitment, line.AvailableAmount);
        Assert.Equal(700m, line.AvailableAmount);
        Assert.Equal(0m, line.CommittedAmount);
        Assert.Equal(300m, line.ActualAmount);
    }

    /// <summary>
    /// An actual cost with no prior commitment drives <c>CommittedAmount</c> negative, and that is
    /// correct rather than a bug to clamp: the negative records that this spend was never reserved, and
    /// <c>AvailableAmount</c> still falls by exactly the amount spent. Flooring it at zero would make
    /// the line look like it had 300 more available than it does.
    /// </summary>
    [Fact]
    public void ActualCost_WithNoPriorCommitment_GoesNegativeRatherThanClamping()
    {
        var line = Line(1000m);
        line.ApplyActualCost(300m);

        Assert.Equal(-300m, line.CommittedAmount);
        Assert.Equal(300m, line.ActualAmount);
        Assert.Equal(1000m, line.EffectiveAllocation);
        Assert.Equal(1000m, line.AvailableAmount);
    }

    [Fact]
    public void Commitment_AndItsReversal_CancelExactly()
    {
        var line = Line(1000m);
        line.ApplyCommitment(432.1098m);
        line.ApplyCommitment(-432.1098m);

        Assert.Equal(0m, line.CommittedAmount);
        Assert.Equal(1000m, line.AvailableAmount);
    }

    [Fact]
    public void ActualCost_AndItsReversal_CancelExactly()
    {
        var line = Line(1000m);
        line.ApplyCommitment(500m);
        line.ApplyActualCost(432.1098m);
        line.ApplyActualCost(-432.1098m);

        Assert.Equal(0m, line.ActualAmount);
        Assert.Equal(500m, line.CommittedAmount);
        Assert.Equal(500m, line.AvailableAmount);
    }

    // ── Allocation update / recalculate ────────────────────────────────────────

    [Fact]
    public void UpdateAllocation_RejectsNegative_AndLeavesTheLineUntouched()
    {
        var line = Line(1000m);
        var result = line.UpdateAllocation(-1m);

        Assert.True(result.IsFailure);
        Assert.Equal(BudgetLineErrors.NegativeAllocation, result.Error);
        Assert.Equal(1000m, line.AllocatedAmount);
    }

    [Fact]
    public void UpdateAllocation_DoesNotDisturbAdjustmentsOrSpend()
    {
        var line = Line(1000m);
        line.ApplyAdjustment(200m);
        line.ApplyCommitment(300m);

        Assert.True(line.UpdateAllocation(1500m).IsSuccess);

        Assert.Equal(1500m, line.AllocatedAmount);
        Assert.Equal(200m, line.AdjustmentAllocation);
        Assert.Equal(300m, line.CommittedAmount);
        Assert.Equal(1700m, line.EffectiveAllocation);
    }

    [Fact]
    public void ResetAggregates_ReplacesTheRunningSums_ButNotTheAllocationOrAdjustment()
    {
        var line = Line(1000m);
        line.ApplyAdjustment(200m);
        line.ApplyCommitment(999m);
        line.ApplyActualCost(999m);

        line.ResetAggregates(committedAmount: 50m, actualAmount: 25m);

        Assert.Equal(50m, line.CommittedAmount);
        Assert.Equal(25m, line.ActualAmount);
        Assert.Equal(1000m, line.AllocatedAmount);
        Assert.Equal(200m, line.AdjustmentAllocation);
        Assert.Equal(1125m, line.AvailableAmount);
    }
}
