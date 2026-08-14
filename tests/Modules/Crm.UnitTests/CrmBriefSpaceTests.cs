using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmBriefSpaceTests
{
    private static CrmBriefSpace CreateEntry() =>
        CrmBriefSpace.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value!;

    [Fact]
    public void Create_StartsInDraft()
    {
        var result = CrmBriefSpace.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "reqs");

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.Draft, result.Value!.ApprovalStatus);
    }

    [Fact]
    public void Send_FromDraft_Succeeds()
    {
        var entry = CreateEntry();

        var result = entry.Send();

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.PendingApproval, entry.ApprovalStatus);
    }

    [Fact]
    public void Send_WhenAlreadyPending_Fails()
    {
        var entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);

        var result = entry.Send();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryPending, result.Error);
    }

    [Fact]
    public void Send_WhenApproved_Fails()
    {
        var entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);
        Assert.True(entry.MarkApproved().IsSuccess);

        var result = entry.Send();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryLocked, result.Error);
    }

    [Fact]
    public void Update_WhenApproved_Fails()
    {
        var entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);
        Assert.True(entry.MarkApproved().IsSuccess);

        var result = entry.Update(null, 1, "x", null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryLocked, result.Error);
    }

    [Fact]
    public void Update_WhenPending_Fails()
    {
        var entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);

        var result = entry.Update(null, 1, "x", null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryPending, result.Error);
    }

    [Fact]
    public void CreateSuperseding_WhenOriginalNotApproved_Fails()
    {
        var original = CreateEntry();

        var result = CrmBriefSpace.CreateSuperseding(original, null, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.OnlyLockedSuperseded, result.Error);
    }

    [Fact]
    public void CreateSuperseding_WhenOriginalApproved_Succeeds()
    {
        var original = CreateEntry();
        Assert.True(original.Send().IsSuccess);
        Assert.True(original.MarkApproved().IsSuccess);

        var result = CrmBriefSpace.CreateSuperseding(original, null, Guid.NewGuid(), requirements: "corrected");

        Assert.True(result.IsSuccess);
        Assert.Equal(original.Id, result.Value!.SupersedesSpaceId);
        Assert.Equal(ApprovalState.Draft, result.Value.ApprovalStatus);
    }
}
