using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/BudgetLine.cs</c>. One budgeted line of a project, at
/// project, stage or task scope, carrying the four money columns the whole ledger moves.
///
/// <para><b>Cross-module ids are bare <see cref="Guid"/>s with no navigation properties.</b> The
/// backend has <c>Project</c>, <c>ProjectStage</c> and <c>ProjectTask</c> navigations here; a
/// navigation to another module's entity type is what arch rule 3 forbids, so they are dropped and the
/// ids stand alone. <see cref="CostCategoryId"/> is Finance-owned and could have carried a navigation,
/// but is left bare for symmetry — nothing in this module traverses it, and every read projects
/// straight to a DTO.</para>
///
/// <para><b>The four columns and what moves them.</b> <see cref="AllocatedAmount"/> is the original
/// estimate and is only ever set by an explicit allocation update.
/// <see cref="AdjustmentAllocation"/> is the running sum of applied <c>InternalChange</c> variation
/// deltas. <see cref="CommittedAmount"/> and <see cref="ActualAmount"/> are moved by ledger postings —
/// a <c>Commitment</c> raises Committed; an <c>ActualCost</c> lowers Committed <i>and</i> raises
/// Actual, which is the commitment-to-actual handoff and the single easiest thing in this module to
/// port wrong.</para>
/// </summary>
internal sealed class BudgetLine : Entity, IAuditable
{
    public string Name { get; private set; } = string.Empty;
    public string? Code { get; private set; }
    public string? Description { get; private set; }

    public decimal AllocatedAmount { get; private set; }
    public decimal AdjustmentAllocation { get; private set; }
    public decimal CommittedAmount { get; private set; }
    public decimal ActualAmount { get; private set; }

    /// <summary>Bare Guid → <c>projects.projects</c>. Real FK, added by raw SQL at F1-05.</summary>
    public Guid ProjectId { get; private set; }

    public Guid? CostCategoryId { get; private set; }

    /// <summary>Bare Guid → <c>projects.project_stages</c>.</summary>
    public Guid? ProjectStageId { get; private set; }

    /// <summary>Bare Guid → <c>projects.project_tasks</c>.</summary>
    public Guid? ProjectTaskId { get; private set; }

    /// <summary>
    /// The original estimate plus the running sum of applied <c>InternalChange</c> variation deltas.
    /// This — not <see cref="AllocatedAmount"/> — is the ceiling spending is measured against, and the
    /// value the Budget gate's coverage check reads.
    /// </summary>
    public decimal EffectiveAllocation => AllocatedAmount + AdjustmentAllocation;

    public decimal AvailableAmount => EffectiveAllocation - CommittedAmount - ActualAmount;

    private BudgetLine()
    {
        // EF materialization.
    }

    public static Result<BudgetLine> Create(
        string name,
        decimal allocatedAmount,
        Guid projectId,
        Guid? costCategoryId = null,
        Guid? projectStageId = null,
        Guid? projectTaskId = null,
        string? code = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BudgetLineErrors.NameRequired;
        }

        if (allocatedAmount < 0)
        {
            return BudgetLineErrors.NegativeAllocation;
        }

        // A task-scoped line must also name its stage. That the task actually belongs to that stage and
        // project is a cross-module fact this entity cannot see; the handler checks it via
        // IProjectDirectory and returns the same error.
        if (projectTaskId.HasValue && !projectStageId.HasValue)
        {
            return BudgetLineErrors.TaskStageMismatch;
        }

        return new BudgetLine
        {
            Name = name,
            Code = code,
            AllocatedAmount = allocatedAmount,
            AdjustmentAllocation = 0,
            CommittedAmount = 0,
            ActualAmount = 0,
            ProjectId = projectId,
            CostCategoryId = costCategoryId,
            ProjectStageId = projectStageId,
            ProjectTaskId = projectTaskId,
            Description = description,
        };
    }

    /// <summary>
    /// Applies an approved <c>InternalChange</c> variation delta. <see cref="AllocatedAmount"/> is
    /// preserved — the original estimate stays auditable and the adjustment is tracked separately.
    ///
    /// <para><b>Negative deltas are accepted deliberately</b>, so a reversal is the same operation with
    /// the sign flipped rather than a second code path. A zero delta is a no-op and does not stamp
    /// <see cref="Entity.UpdatedAtUtc"/>.</para>
    /// </summary>
    public void ApplyAdjustment(decimal delta)
    {
        if (delta == 0)
        {
            return;
        }

        AdjustmentAllocation += delta;
    }

    /// <summary>
    /// Moves <see cref="CommittedAmount"/> by <paramref name="delta"/>. Negative on reversal.
    /// <b>Not floored at zero</b>: the commitment-to-actual handoff drives this negative by design when
    /// an actual cost consumes an earlier commitment, and clamping would silently lose the consumption.
    /// </summary>
    public void ApplyCommitment(decimal delta)
    {
        if (delta == 0)
        {
            return;
        }

        CommittedAmount += delta;
    }

    /// <summary>
    /// The commitment-to-actual handoff, as one operation because it is one fact: money that was
    /// committed has now actually been spent. Raises <see cref="ActualAmount"/> and lowers
    /// <see cref="CommittedAmount"/> by the same <paramref name="delta"/>, so
    /// <see cref="AvailableAmount"/> is unchanged when the actual matches its commitment.
    ///
    /// <para>Splitting this into two calls is how the port goes wrong: doing only the Actual half
    /// double-counts the spend against the budget forever.</para>
    /// </summary>
    public void ApplyActualCost(decimal delta)
    {
        if (delta == 0)
        {
            return;
        }

        ActualAmount += delta;
        CommittedAmount -= delta;
    }

    public Result UpdateAllocation(decimal allocatedAmount)
    {
        if (allocatedAmount < 0)
        {
            return BudgetLineErrors.NegativeAllocation;
        }

        AllocatedAmount = allocatedAmount;
        return Result.Success();
    }

    /// <summary>Replaces the running aggregates with values re-derived from a full ledger re-sum. The
    /// repair hatch behind <c>RecalculateBudgetLineCommand</c>; nothing else should set these two
    /// directly.</summary>
    public void ResetAggregates(decimal committedAmount, decimal actualAmount)
    {
        CommittedAmount = committedAmount;
        ActualAmount = actualAmount;
    }
}
