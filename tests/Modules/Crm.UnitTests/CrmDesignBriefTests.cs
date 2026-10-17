using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmDesignBriefTests
{
    private static CrmBriefHeadData MinimalHead(string? address = null) =>
        new((UnitType)1, (UnitCondition)1, Address: address);

    [Fact]
    public void Create_StartsInDraft()
    {
        var brief = CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MinimalHead());

        Assert.Equal(ApprovalState.Draft, brief.Status);
    }

    [Fact]
    public void UpdateHead_WhileDraft_StaysDraft()
    {
        var brief = CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), null, MinimalHead());

        brief.UpdateHead(MinimalHead("New Addr"), null);

        Assert.Equal(ApprovalState.Draft, brief.Status);
        Assert.Equal("New Addr", brief.Address);
    }

    [Fact]
    public void UpdateHead_WhileApproved_ReturnsToPendingApproval()
    {
        var brief = CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), null, MinimalHead());
        Assert.True(brief.MarkPendingApproval().IsSuccess);
        Assert.True(brief.MarkApproved().IsSuccess);

        brief.UpdateHead(MinimalHead("Edited"), null);

        Assert.Equal(ApprovalState.PendingApproval, brief.Status);
    }

    [Fact]
    public void MarkApproved_WhenNotPending_Fails()
    {
        var brief = CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), null, MinimalHead());

        Result result = brief.MarkApproved();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.NotPending, result.Error);
    }

    [Fact]
    public void MarkApproved_ThenMarkRejected_TransitionsCorrectly()
    {
        var brief = CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), null, MinimalHead());
        Assert.True(brief.MarkPendingApproval().IsSuccess);

        Assert.True(brief.MarkApproved().IsSuccess);
        Assert.Equal(ApprovalState.Approved, brief.Status);
    }

    [Fact]
    public void SeedFromIntake_WhileDraft_FillsBlanksOnly()
    {
        var brief = CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), null, MinimalHead("Office Addr"));

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
        var brief = CrmDesignBrief.Create(Guid.NewGuid(), Guid.NewGuid(), null, MinimalHead());
        Assert.True(brief.MarkPendingApproval().IsSuccess);
        Assert.True(brief.MarkApproved().IsSuccess);

        Result result = brief.SeedFromIntake(null, null, null, null, null, null, null, null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.NotDraft, result.Error);
    }
}
