using Finance.Domain;
using SharedKernel;
using Xunit;

namespace Finance.UnitTests;

/// <summary>
/// The Variation state machine, enumerated: every legal transition and every illegal one, each
/// asserting the <b>specific</b> error. A bare <c>IsFailure</c> here would let a guard that rejects for
/// the wrong reason pass — and on a two-kind aggregate the wrong reason is usually "the other kind's
/// rule fired", which is precisely the bug worth catching.
/// </summary>
public class VariationStateMachineTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid BudgetLineId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    private static Variation ClientDifference() =>
        Variation.Create(
            VariationKind.ClientDifference, ProjectId, "Client upgraded the joinery", ActorId,
            projectTaskId: TaskId, budgetedAmount: 1000m, chosenAmount: 1750m).Value!;

    private static Variation InternalChange() =>
        Variation.Create(
            VariationKind.InternalChange, ProjectId, "Material price rise", ActorId,
            budgetLineId: BudgetLineId, amount: 500m).Value!;

    // ── Creation: the two kinds have disjoint requirements ─────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAReason_FailsWithReasonRequired(string reason)
    {
        Result<Variation> result = Variation.Create(
            VariationKind.InternalChange, ProjectId, reason, ActorId, budgetLineId: BudgetLineId, amount: 500m);

        Assert.True(result.IsFailure);
        Assert.Equal(VariationErrors.ReasonRequired, result.Error);
    }

    [Fact]
    public void ClientDifference_DerivesAmountFromTheTwoPrices_AndNeverTakesItDirectly()
    {
        Variation variation = ClientDifference();

        Assert.Equal(750m, variation.Amount);
        Assert.Equal(1000m, variation.BudgetedAmount);
        Assert.Equal(1750m, variation.ChosenAmount);
        Assert.Equal(VariationStatus.Draft, variation.Status);
    }

    /// <summary>Chosen must strictly exceed budgeted. Equal is not a variation — there is no difference
    /// to sign — and less is a saving, which this record does not model.</summary>
    [Fact]
    public void ClientDifference_RequiresChosenToExceedBudgeted()
    {
        foreach ((decimal budgeted, decimal chosen) in new[] { (1000m, 1000m), (1000m, 999.9999m) })
        {
            Result<Variation> result = Variation.Create(
                VariationKind.ClientDifference, ProjectId, "x", ActorId,
                projectTaskId: TaskId, budgetedAmount: budgeted, chosenAmount: chosen);

            Assert.True(result.IsFailure);
            Assert.Equal(VariationErrors.MissingClientDifferenceFields, result.Error);
        }

        // One ten-thousandth above IS a variation - the boundary is strict, not rounded.
        Result<Variation> justAbove = Variation.Create(
            VariationKind.ClientDifference, ProjectId, "x", ActorId,
            projectTaskId: TaskId, budgetedAmount: 1000m, chosenAmount: 1000.0001m);

        Assert.True(justAbove.IsSuccess);
        Assert.Equal(0.0001m, justAbove.Value!.Amount);
    }

    [Fact]
    public void ClientDifference_WithoutATask_Fails()
    {
        Result<Variation> result = Variation.Create(
            VariationKind.ClientDifference, ProjectId, "x", ActorId,
            budgetedAmount: 1000m, chosenAmount: 1750m);

        Assert.True(result.IsFailure);
        Assert.Equal(VariationErrors.MissingClientDifferenceFields, result.Error);
    }

    [Fact]
    public void InternalChange_WithoutABudgetLine_FailsWithMissingBudgetLine()
    {
        Result<Variation> result = Variation.Create(
            VariationKind.InternalChange, ProjectId, "x", ActorId, amount: 500m);

        Assert.True(result.IsFailure);
        // Not MissingClientDifferenceFields - the other kind's rule must not fire here.
        Assert.Equal(VariationErrors.MissingBudgetLine, result.Error);
    }

    [Fact]
    public void InternalChange_WithNonPositiveAmount_FailsWithAmountMustBePositive()
    {
        foreach (decimal? amount in new decimal?[] { null, 0m, -1m })
        {
            Result<Variation> result = Variation.Create(
                VariationKind.InternalChange, ProjectId, "x", ActorId,
                budgetLineId: BudgetLineId, amount: amount);

            Assert.True(result.IsFailure);
            Assert.Equal(VariationErrors.AmountMustBePositive, result.Error);
        }
    }

    // ── Submit ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Submit_FromDraft_Succeeds_AndOnlyAClientDifferenceIsSentToTheClient()
    {
        Variation clientDifference = ClientDifference();
        Assert.True(clientDifference.Submit().IsSuccess);
        Assert.Equal(VariationStatus.Submitted, clientDifference.Status);
        Assert.NotNull(clientDifference.SentToClientAt);

        Variation internalChange = InternalChange();
        Assert.True(internalChange.Submit().IsSuccess);
        Assert.Equal(VariationStatus.Submitted, internalChange.Status);
        // An internal change never leaves the office.
        Assert.Null(internalChange.SentToClientAt);
    }

    [Fact]
    public void Submit_IsNotRepeatable()
    {
        Variation variation = InternalChange();
        Assert.True(variation.Submit().IsSuccess);

        Result again = variation.Submit();
        Assert.True(again.IsFailure);
        Assert.Equal(VariationErrors.InvalidStatusTransition, again.Error);
    }

    // ── Approve / sign ─────────────────────────────────────────────────────────

    [Fact]
    public void Approve_FromDraft_IsRejected_SubmitIsMandatory()
    {
        Variation variation = InternalChange();

        Result result = variation.Approve(ActorId);

        Assert.True(result.IsFailure);
        Assert.Equal(VariationErrors.InvalidStatusTransition, result.Error);
        Assert.Equal(VariationStatus.Draft, variation.Status);
    }

    /// <summary>For a client difference, approval IS the signature — so approving without the signing
    /// client is refused rather than producing an approved-but-unsigned record.</summary>
    [Fact]
    public void Approve_ClientDifference_WithoutTheSigningClient_FailsWithSignatureRequired()
    {
        Variation variation = ClientDifference();
        variation.Submit();

        Result result = variation.Approve(ActorId);

        Assert.True(result.IsFailure);
        Assert.Equal(VariationErrors.SignatureRequired, result.Error);
        Assert.Equal(VariationStatus.Submitted, variation.Status);
        Assert.Null(variation.SignedAt);
    }

    [Fact]
    public void Approve_ClientDifference_WithTheSigningClient_StampsTheSignature()
    {
        Variation variation = ClientDifference();
        variation.Submit();

        Assert.True(variation.Approve(ActorId, ClientId, "s3://signed.pdf").IsSuccess);

        Assert.Equal(VariationStatus.Approved, variation.Status);
        Assert.Equal(ActorId, variation.ApprovedById);
        Assert.NotNull(variation.ApprovedAt);
        Assert.Equal(ClientId, variation.SignedByClientId);
        Assert.NotNull(variation.SignedAt);
        Assert.Equal("s3://signed.pdf", variation.SignedDocumentPath);
        Assert.True(variation.IsSigned);
    }

    /// <summary>An internal change carries no signature. Supplying one is an error rather than being
    /// ignored, so a caller that confused the kinds is told instead of silently storing a wrong record.</summary>
    [Fact]
    public void Approve_InternalChange_WithSignatureFields_FailsWithSignatureFieldsNotAllowed()
    {
        foreach ((Guid? client, string? path) in new (Guid?, string?)[] { (ClientId, null), (null, "s3://x.pdf") })
        {
            Variation variation = InternalChange();
            variation.Submit();

            Result result = variation.Approve(ActorId, client, path);

            Assert.True(result.IsFailure);
            Assert.Equal(VariationErrors.SignatureFieldsNotAllowed, result.Error);
            Assert.Equal(VariationStatus.Submitted, variation.Status);
        }
    }

    [Fact]
    public void Approve_InternalChange_WithoutSignatureFields_Succeeds_AndLeavesSignatureNull()
    {
        Variation variation = InternalChange();
        variation.Submit();

        Assert.True(variation.Approve(ActorId).IsSuccess);

        Assert.Equal(VariationStatus.Approved, variation.Status);
        Assert.Null(variation.SignedByClientId);
        Assert.Null(variation.SignedAt);
        Assert.False(variation.IsSigned);
    }

    // ── Reject / cancel ────────────────────────────────────────────────────────

    [Fact]
    public void RejectAndCancel_AreAvailableFromDraftAndSubmittedOnly()
    {
        // Legal from Draft.
        Variation draftReject = InternalChange();
        Assert.True(draftReject.Reject(ActorId, "no").IsSuccess);
        Assert.Equal(VariationStatus.Rejected, draftReject.Status);
        Assert.Equal(ActorId, draftReject.ApprovedById);
        Assert.Equal("no", draftReject.ResolutionNote);

        Variation draftCancel = InternalChange();
        Assert.True(draftCancel.Cancel("withdrawn").IsSuccess);
        Assert.Equal(VariationStatus.Cancelled, draftCancel.Status);

        // Legal from Submitted.
        Variation submittedReject = InternalChange();
        submittedReject.Submit();
        Assert.True(submittedReject.Reject(ActorId).IsSuccess);

        Variation submittedCancel = InternalChange();
        submittedCancel.Submit();
        Assert.True(submittedCancel.Cancel().IsSuccess);
    }

    /// <summary>There is no path back from a terminal state. Enumerated across all four terminals and
    /// both operations, because a state machine's gaps live in the combinations nobody wrote down.</summary>
    [Fact]
    public void RejectAndCancel_AreRefusedFromEveryTerminalState()
    {
        foreach (Func<Variation> makeTerminal in TerminalFactories())
        {
            Variation forReject = makeTerminal();
            Result reject = forReject.Reject(ActorId);
            Assert.True(reject.IsFailure);
            Assert.Equal(VariationErrors.InvalidStatusTransition, reject.Error);

            Variation forCancel = makeTerminal();
            Result cancel = forCancel.Cancel();
            Assert.True(cancel.IsFailure);
            Assert.Equal(VariationErrors.InvalidStatusTransition, cancel.Error);
        }
    }

    // ── Apply ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MarkApplied_FromApproved_Succeeds()
    {
        Variation variation = InternalChange();
        variation.Submit();
        variation.Approve(ActorId);

        Assert.True(variation.MarkApplied().IsSuccess);
        Assert.Equal(VariationStatus.Applied, variation.Status);
    }

    /// <summary>Applying twice must fail: it drives a budget-line adjustment that has to happen exactly
    /// once, so the guard is what stops a retry from doubling the money.</summary>
    [Fact]
    public void MarkApplied_IsNotRepeatable()
    {
        Variation variation = InternalChange();
        variation.Submit();
        variation.Approve(ActorId);
        variation.MarkApplied();

        Result again = variation.MarkApplied();

        Assert.True(again.IsFailure);
        Assert.Equal(VariationErrors.NotApproved, again.Error);
        Assert.Equal(VariationStatus.Applied, variation.Status);
    }

    [Fact]
    public void MarkApplied_FromAnyNonApprovedState_FailsWithNotApproved()
    {
        Variation draft = InternalChange();
        Assert.Equal(VariationErrors.NotApproved, draft.MarkApplied().Error);

        Variation submitted = InternalChange();
        submitted.Submit();
        Assert.Equal(VariationErrors.NotApproved, submitted.MarkApplied().Error);

        Variation rejected = InternalChange();
        rejected.Reject(ActorId);
        Assert.Equal(VariationErrors.NotApproved, rejected.MarkApplied().Error);

        Variation cancelled = InternalChange();
        cancelled.Cancel();
        Assert.Equal(VariationErrors.NotApproved, cancelled.MarkApplied().Error);
    }

    /// <summary>Approve is refused from every terminal state too — the complement of
    /// <see cref="Approve_FromDraft_IsRejected_SubmitIsMandatory"/>, closing the transition table.</summary>
    [Fact]
    public void Approve_IsRefusedFromEveryTerminalState()
    {
        foreach (Func<Variation> makeTerminal in TerminalFactories())
        {
            Variation variation = makeTerminal();
            Result result = variation.Approve(ActorId, ClientId, "s3://x.pdf");

            Assert.True(result.IsFailure);
            Assert.Equal(VariationErrors.InvalidStatusTransition, result.Error);
        }
    }

    // ── S4: the description is editable in Draft only ─────────────────────────

    [Fact]
    public void UpdateDescription_InDraft_ReplacesIt_AndBlankClearsIt()
    {
        Variation variation = InternalChange();

        Assert.True(variation.UpdateDescription("Revised scope").IsSuccess);
        Assert.Equal("Revised scope", variation.Description);

        Assert.True(variation.UpdateDescription("   ").IsSuccess);
        Assert.Null(variation.Description);
    }

    [Fact]
    public void UpdateDescription_AfterSubmit_FailsWithDescriptionNotEditable_AndKeepsIt()
    {
        Variation variation = InternalChange();
        variation.UpdateDescription("Before submit");
        variation.Submit();

        Result result = variation.UpdateDescription("After submit");

        Assert.Equal(VariationErrors.DescriptionNotEditable, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal("Before submit", variation.Description);
    }

    private static IEnumerable<Func<Variation>> TerminalFactories()
    {
        yield return () =>
        {
            Variation v = InternalChange();
            v.Submit();
            v.Approve(ActorId);
            return v;                       // Approved
        };
        yield return () =>
        {
            Variation v = InternalChange();
            v.Submit();
            v.Approve(ActorId);
            v.MarkApplied();
            return v;                       // Applied
        };
        yield return () =>
        {
            Variation v = InternalChange();
            v.Reject(ActorId);
            return v;                       // Rejected
        };
        yield return () =>
        {
            Variation v = InternalChange();
            v.Cancel();
            return v;                       // Cancelled
        };
    }
}
