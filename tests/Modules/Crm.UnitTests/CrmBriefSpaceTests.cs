using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmBriefSpaceTests
{
    private static readonly Guid ApprovalId = Guid.NewGuid();

    private static CrmBriefSpace CreateEntry() =>
        CrmBriefSpace.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value!;

    [Fact]
    public void Create_StartsInDraft()
    {
        Result<CrmBriefSpace> result = CrmBriefSpace.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "reqs");

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.Draft, result.Value!.ApprovalStatus);
    }

    [Fact]
    public void Send_FromDraft_Succeeds()
    {
        CrmBriefSpace entry = CreateEntry();

        Result result = entry.Send(ApprovalId);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.PendingApproval, entry.ApprovalStatus);
    }

    [Fact]
    public void Send_WhenAlreadyPending_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send(ApprovalId).IsSuccess);

        Result result = entry.Send(ApprovalId);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryPending, result.Error);
    }

    [Fact]
    public void Send_WhenApproved_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send(ApprovalId).IsSuccess);
        Assert.True(entry.MarkApproved(ApprovalId).IsSuccess);

        Result result = entry.Send(ApprovalId);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryLocked, result.Error);
    }

    [Fact]
    public void Update_WhenApproved_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send(ApprovalId).IsSuccess);
        Assert.True(entry.MarkApproved(ApprovalId).IsSuccess);

        Result result = entry.Update(null, 1, "x", null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryLocked, result.Error);
    }

    [Fact]
    public void Update_WhenPending_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send(ApprovalId).IsSuccess);

        Result result = entry.Update(null, 1, "x", null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryPending, result.Error);
    }

    [Fact]
    public void CreateSuperseding_WhenOriginalNotApproved_Fails()
    {
        CrmBriefSpace original = CreateEntry();

        Result<CrmBriefSpace> result = CrmBriefSpace.CreateSuperseding(original, null, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.OnlyLockedSuperseded, result.Error);
    }

    [Fact]
    public void CreateSuperseding_WhenOriginalApproved_Succeeds()
    {
        CrmBriefSpace original = CreateEntry();
        Assert.True(original.Send(ApprovalId).IsSuccess);
        Assert.True(original.MarkApproved(ApprovalId).IsSuccess);

        Result<CrmBriefSpace> result = CrmBriefSpace.CreateSuperseding(original, original.SpaceId, Guid.NewGuid(), requirements: "corrected");

        Assert.True(result.IsSuccess);
        Assert.Equal(original.Id, result.Value!.SupersedesSpaceId);
        Assert.Equal(ApprovalState.Draft, result.Value.ApprovalStatus);
    }

    [Fact]
    public void Send_BindsTheApproval_AndCountsTheRound()
    {
        CrmBriefSpace entry = CreateEntry();

        Assert.True(entry.Send(ApprovalId).IsSuccess);

        Assert.Equal(ApprovalId, entry.PendingApprovalId);
        Assert.Equal(1, entry.SendCount);
    }

    [Fact]
    public void MarkApproved_WithAnotherApprovalsId_IsIgnored()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send(ApprovalId).IsSuccess);

        Result result = entry.MarkApproved(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.NotPending, result.Error);
        Assert.Equal(ApprovalState.PendingApproval, entry.ApprovalStatus);
        Assert.Equal(ApprovalId, entry.PendingApprovalId);
    }

    [Fact]
    public void MarkRejected_WithAnotherApprovalsId_IsIgnored()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send(ApprovalId).IsSuccess);

        Result result = entry.MarkRejected(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalState.PendingApproval, entry.ApprovalStatus);
    }

    [Fact]
    public void Rejected_ThenResent_OpensRoundTwoUnderTheNewApproval()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send(ApprovalId).IsSuccess);
        Assert.True(entry.MarkRejected(ApprovalId).IsSuccess);
        Assert.Null(entry.PendingApprovalId);

        var second = Guid.NewGuid();
        Assert.True(entry.Send(second).IsSuccess);

        Assert.Equal(2, entry.SendCount);
        Assert.Equal(second, entry.PendingApprovalId);
        Assert.True(entry.MarkRejected(ApprovalId).IsFailure);
        Assert.True(entry.MarkApproved(second).IsSuccess);
        Assert.Equal(ApprovalState.Approved, entry.ApprovalStatus);
        Assert.Null(entry.PendingApprovalId);
    }

    [Fact]
    public void CreateSuperseding_OnADifferentSpace_Fails()
    {
        CrmBriefSpace original = CreateEntry();
        Assert.True(original.Send(ApprovalId).IsSuccess);
        Assert.True(original.MarkApproved(ApprovalId).IsSuccess);

        Result<CrmBriefSpace> result = CrmBriefSpace.CreateSuperseding(original, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(CrmBriefErrors.SupersedeSpaceMismatch, result.Error);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public void Update_OfACorrection_CannotMoveItToAnotherSpace()
    {
        CrmBriefSpace original = CreateEntry();
        Assert.True(original.Send(ApprovalId).IsSuccess);
        Assert.True(original.MarkApproved(ApprovalId).IsSuccess);
        CrmBriefSpace correction = CrmBriefSpace.CreateSuperseding(original, original.SpaceId, Guid.NewGuid()).Value!;

        Result moved = correction.Update(Guid.NewGuid(), 1, "x", null, null);
        Result kept = correction.Update(original.SpaceId, 2, "y", null, null);

        Assert.Equal(CrmBriefErrors.SupersedeSpaceMismatch, moved.Error);
        Assert.True(kept.IsSuccess);
        Assert.Equal(original.SpaceId, correction.SpaceId);
    }
}
