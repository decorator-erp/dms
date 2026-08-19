using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/Variation.cs</c>. One budget-change record discriminated
/// by <see cref="Kind"/>, and the richest state machine in the module.
///
/// <para><b>The two kinds are genuinely different records sharing a table.</b>
/// <see cref="VariationKind.ClientDifference"/> is "the client chose above the task budget and signs
/// the difference" — it requires a task and both amounts, derives <see cref="Amount"/> as
/// <c>Chosen - Budgeted</c>, and its approval <i>is</i> the client's signature.
/// <see cref="VariationKind.InternalChange"/> is "the team raises a budget line" — it requires the
/// budget line and an explicit amount, and its signature columns must stay null.</para>
///
/// <para><b>The state machine:</b> Draft → Submitted → Approved → Applied, with Reject and Cancel
/// available from Draft or Submitted only. There is no path back from Approved, Rejected, Cancelled or
/// Applied; a mistake after approval is corrected by a reversing ledger entry, not by editing history.</para>
///
/// <para><b>Applying is asymmetric, and this is the easiest thing here to "fix" into a bug.</b> For
/// <see cref="VariationKind.InternalChange"/>, applying pushes <see cref="Amount"/> into
/// <c>BudgetLine.AdjustmentAllocation</c>. For <see cref="VariationKind.ClientDifference"/> it writes
/// <b>nothing</b> — the signed delta stays on this row and is read back by the Budget gate's coverage
/// check (<c>../backend/Domain/Services/VariationDomainService.cs:140-144</c>). Making the two
/// symmetrical would double-count every client difference.</para>
/// </summary>
internal sealed class Variation : Entity, IAuditable
{
    public VariationKind Kind { get; private set; }

    /// <summary>Bare Guid → <c>projects.projects</c>.</summary>
    public Guid ProjectId { get; private set; }

    public Guid? BudgetLineId { get; private set; }

    /// <summary>Bare Guid → <c>projects.project_tasks</c>.</summary>
    public Guid? ProjectTaskId { get; private set; }

    /// <summary>Bare Guid → <c>projects.project_stages</c>.</summary>
    public Guid? ProjectStageId { get; private set; }

    public string Reason { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Always positive. For a client difference it is derived and cannot be supplied.</summary>
    public decimal Amount { get; private set; }

    public decimal? BudgetedAmount { get; private set; }
    public decimal? ChosenAmount { get; private set; }

    public VariationStatus Status { get; private set; }

    /// <summary>Bare Guid → <c>hr.employees</c>.</summary>
    public Guid RequestedById { get; private set; }

    /// <summary>Bare Guid → <c>hr.employees</c>. Also stamped on reject, carrying the decider either way.</summary>
    public Guid? ApprovedById { get; private set; }

    public DateTime? ApprovedAt { get; private set; }
    public DateTime? SentToClientAt { get; private set; }

    /// <summary>Bare Guid → <c>crm.clients</c>.</summary>
    public Guid? SignedByClientId { get; private set; }

    public DateTime? SignedAt { get; private set; }
    public string? SignedDocumentPath { get; private set; }
    public string? ResolutionNote { get; private set; }

    public bool IsSigned => SignedAt.HasValue;

    private Variation()
    {
        // EF materialization.
    }

    public static Result<Variation> Create(
        VariationKind kind,
        Guid projectId,
        string reason,
        Guid requestedById,
        Guid? projectTaskId = null,
        Guid? budgetLineId = null,
        decimal? budgetedAmount = null,
        decimal? chosenAmount = null,
        decimal? amount = null,
        string? description = null,
        Guid? projectStageId = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return VariationErrors.ReasonRequired;

        var variation = new Variation
        {
            Kind = kind,
            ProjectId = projectId,
            Reason = reason,
            RequestedById = requestedById,
            ProjectTaskId = projectTaskId,
            ProjectStageId = projectStageId,
            BudgetLineId = budgetLineId,
            Description = description,
            Status = VariationStatus.Draft,
        };

        switch (kind)
        {
            case VariationKind.ClientDifference:
                // chosen must EXCEED budgeted: a variation records an overrun the client agrees to
                // cover, so a non-positive difference is not a variation at all.
                if (projectTaskId is null || budgetedAmount is null || chosenAmount is null
                    || chosenAmount <= budgetedAmount)
                    return VariationErrors.MissingClientDifferenceFields;

                variation.BudgetedAmount = budgetedAmount;
                variation.ChosenAmount = chosenAmount;
                variation.Amount = chosenAmount.Value - budgetedAmount.Value;
                break;

            case VariationKind.InternalChange:
                if (budgetLineId is null)
                    return VariationErrors.MissingBudgetLine;

                if (amount is null or <= 0)
                    return VariationErrors.AmountMustBePositive;

                variation.Amount = amount.Value;
                break;

            default:
                return VariationErrors.MissingClientDifferenceFields;
        }

        return variation;
    }

    public Result Submit()
    {
        if (Status != VariationStatus.Draft)
            return VariationErrors.InvalidStatusTransition;

        Status = VariationStatus.Submitted;

        // Only a client difference goes out for signature; an internal change never leaves the office.
        if (Kind == VariationKind.ClientDifference)
            SentToClientAt = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// For <see cref="VariationKind.ClientDifference"/> approval <i>is</i> the client signing:
    /// <paramref name="signedByClientId"/> is required and the signature columns are stamped. For
    /// <see cref="VariationKind.InternalChange"/> it is a manager approval and the signature arguments
    /// must be absent — passing them is an error rather than being ignored, so a caller that confused
    /// the two kinds is told rather than silently getting the wrong record.
    /// </summary>
    public Result Approve(Guid approvedById, Guid? signedByClientId = null, string? signedDocumentPath = null)
    {
        if (Status != VariationStatus.Submitted)
            return VariationErrors.InvalidStatusTransition;

        switch (Kind)
        {
            case VariationKind.ClientDifference:
                if (signedByClientId is null)
                    return VariationErrors.SignatureRequired;

                SignedByClientId = signedByClientId;
                SignedAt = DateTime.UtcNow;
                SignedDocumentPath = signedDocumentPath;
                break;

            case VariationKind.InternalChange:
                if (signedByClientId is not null || signedDocumentPath is not null)
                    return VariationErrors.SignatureFieldsNotAllowed;
                break;
        }

        Status = VariationStatus.Approved;
        ApprovedById = approvedById;
        ApprovedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reject(Guid rejectedById, string? note = null)
    {
        if (Status is not (VariationStatus.Draft or VariationStatus.Submitted))
            return VariationErrors.InvalidStatusTransition;

        Status = VariationStatus.Rejected;
        ApprovedById = rejectedById;
        ApprovedAt = DateTime.UtcNow;
        ResolutionNote = note;
        return Result.Success();
    }

    public Result Cancel(string? note = null)
    {
        if (Status is not (VariationStatus.Draft or VariationStatus.Submitted))
            return VariationErrors.InvalidStatusTransition;

        Status = VariationStatus.Cancelled;
        ResolutionNote = note;
        return Result.Success();
    }

    /// <summary>Terminal. Only an approved variation can be applied, and applying is idempotent-hostile
    /// by design — a second call fails, because the budget-line adjustment it drives must happen once.</summary>
    public Result MarkApplied()
    {
        if (Status != VariationStatus.Approved)
            return VariationErrors.NotApproved;

        Status = VariationStatus.Applied;
        return Result.Success();
    }
}
