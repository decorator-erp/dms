using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmDesignBriefTests
{
    private static readonly Guid ApprovalId = Guid.NewGuid();

    private static CrmBriefHeadData MinimalHead(string? address = null) =>
        new((UnitType)1, (UnitCondition)1, Address: address);

    private static CrmDesignBrief NewBrief(string? address = null) =>
        CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MinimalHead(address));

    private static CrmDesignBrief Pending()
    {
        CrmDesignBrief brief = NewBrief();
        Assert.True(brief.BeginRound(ApprovalId).IsSuccess);
        return brief;
    }

    private static CrmDesignBrief Approved()
    {
        CrmDesignBrief brief = Pending();
        Assert.True(brief.MarkApproved(ApprovalId).IsSuccess);
        return brief;
    }

    private static CrmDesignBrief Rejected()
    {
        CrmDesignBrief brief = Pending();
        Assert.True(brief.MarkRejected(ApprovalId).IsSuccess);
        return brief;
    }

    [Fact]
    public void Create_StartsInDraft()
    {
        CrmDesignBrief brief = NewBrief();

        Assert.Equal(ApprovalState.Draft, brief.Status);
        Assert.Null(brief.PendingApprovalId);
    }

    [Fact]
    public void UpdateHead_WhileDraft_StaysDraft()
    {
        CrmDesignBrief brief = NewBrief();

        Assert.True(brief.UpdateHead(MinimalHead("New Addr"), null).IsSuccess);

        Assert.Equal(ApprovalState.Draft, brief.Status);
        Assert.Equal("New Addr", brief.Address);
    }

    [Fact]
    public void UpdateHead_WhileApproved_ReturnsToDraft()
    {
        CrmDesignBrief brief = Approved();

        Assert.True(brief.UpdateHead(MinimalHead("Edited"), null).IsSuccess);

        Assert.Equal(ApprovalState.Draft, brief.Status);
        Assert.Null(brief.PendingApprovalId);
        Assert.Equal("Edited", brief.Address);
    }

    [Fact]
    public void UpdateHead_WhileRejected_ReturnsToDraft()
    {
        CrmDesignBrief brief = Rejected();

        Assert.True(brief.UpdateHead(MinimalHead("Reworked"), null).IsSuccess);

        Assert.Equal(ApprovalState.Draft, brief.Status);
    }

    [Fact]
    public void UpdateHead_WhilePending_IsLocked_AndChangesNothing()
    {
        CrmDesignBrief brief = Pending();

        Result result = brief.UpdateHead(MinimalHead("Sneaky edit"), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.HeadLockedPendingApproval, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Null(brief.Address);
        Assert.Null(brief.LastMeetingId);
        Assert.Equal(ApprovalState.PendingApproval, brief.Status);
    }

    [Fact]
    public void BeginRound_FromDraft_BindsTheApproval()
    {
        CrmDesignBrief brief = NewBrief();

        Assert.True(brief.BeginRound(ApprovalId).IsSuccess);

        Assert.Equal(ApprovalState.PendingApproval, brief.Status);
        Assert.Equal(ApprovalId, brief.PendingApprovalId);
    }

    [Fact]
    public void BeginRound_FromRejected_Succeeds()
    {
        CrmDesignBrief brief = Rejected();
        var second = Guid.NewGuid();

        Assert.True(brief.BeginRound(second).IsSuccess);

        Assert.Equal(second, brief.PendingApprovalId);
    }

    [Fact]
    public void BeginRound_WhilePending_Fails()
    {
        CrmDesignBrief brief = Pending();

        Result result = brief.BeginRound(Guid.NewGuid());

        Assert.Equal(CrmBriefErrors.NotSendable, result.Error);
        Assert.Equal(ApprovalId, brief.PendingApprovalId);
    }

    [Fact]
    public void BeginRound_WhenApproved_Fails()
    {
        CrmDesignBrief brief = Approved();

        Result result = brief.BeginRound(Guid.NewGuid());

        Assert.Equal(CrmBriefErrors.NotSendable, result.Error);
        Assert.Equal(ApprovalState.Approved, brief.Status);
    }

    [Fact]
    public void MarkApproved_WhenNotPending_Fails()
    {
        CrmDesignBrief brief = NewBrief();

        Result result = brief.MarkApproved(ApprovalId);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.NotPending, result.Error);
    }

    [Fact]
    public void MarkApproved_WithTheRoundsApproval_ApprovesAndClearsTheBinding()
    {
        CrmDesignBrief brief = Pending();

        Assert.True(brief.MarkApproved(ApprovalId).IsSuccess);

        Assert.Equal(ApprovalState.Approved, brief.Status);
        Assert.Null(brief.PendingApprovalId);
    }

    [Fact]
    public void MarkApproved_WithAnotherApprovalsId_IsIgnored()
    {
        CrmDesignBrief brief = Pending();

        Result result = brief.MarkApproved(Guid.NewGuid());

        Assert.Equal(CrmBriefErrors.NotPending, result.Error);
        Assert.Equal(ApprovalState.PendingApproval, brief.Status);
        Assert.Equal(ApprovalId, brief.PendingApprovalId);
    }

    [Fact]
    public void MarkRejected_WithAnotherApprovalsId_IsIgnored()
    {
        CrmDesignBrief brief = Pending();

        Result result = brief.MarkRejected(Guid.NewGuid());

        Assert.Equal(CrmBriefErrors.NotPending, result.Error);
        Assert.Equal(ApprovalState.PendingApproval, brief.Status);
    }

    [Fact]
    public void MarkRejected_WithTheRoundsApproval_RejectsAndClearsTheBinding()
    {
        CrmDesignBrief brief = Pending();

        Assert.True(brief.MarkRejected(ApprovalId).IsSuccess);

        Assert.Equal(ApprovalState.Rejected, brief.Status);
        Assert.Null(brief.PendingApprovalId);
    }

    [Fact]
    public void ADecisionForAnEarlierRound_DoesNotApplyToTheCurrentOne()
    {
        CrmDesignBrief brief = Rejected();
        Assert.True(brief.UpdateHead(MinimalHead("Round 2"), null).IsSuccess);
        var roundTwo = Guid.NewGuid();
        Assert.True(brief.BeginRound(roundTwo).IsSuccess);

        Assert.True(brief.MarkApproved(ApprovalId).IsFailure);

        Assert.Equal(ApprovalState.PendingApproval, brief.Status);
        Assert.Equal(roundTwo, brief.PendingApprovalId);
    }

    [Fact]
    public void SeedFromIntake_WhileDraft_FillsBlanksOnly()
    {
        CrmDesignBrief brief = NewBrief("Office Addr");

        Result result = brief.SeedFromIntake(
            (ProjectSector)1, "Intake Addr", 100m, true, (PastExperience)1, "reason",
            (UnitType)2, (UnitCondition)2, true, false, (FinishingPurpose)1);

        Assert.True(result.IsSuccess);
        // Address was already set by the office ("Office Addr"), so the intake value is ignored.
        Assert.Equal("Office Addr", brief.Address);
        Assert.Equal((ProjectSector)1, brief.Sector);
        Assert.Equal(100m, brief.AreaSqm);
    }

    [Fact]
    public void SeedFromIntake_WhenNotDraft_Fails()
    {
        CrmDesignBrief brief = Approved();

        Result result = brief.SeedFromIntake(null, null, null, null, null, null, null, null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.NotDraft, result.Error);
    }
}
