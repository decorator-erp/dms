using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmBriefSpaceTests
{
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

        Result result = entry.Send();

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.PendingApproval, entry.ApprovalStatus);
    }

    [Fact]
    public void Send_WhenAlreadyPending_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);

        Result result = entry.Send();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryPending, result.Error);
    }

    [Fact]
    public void Send_WhenApproved_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);
        Assert.True(entry.MarkApproved().IsSuccess);

        Result result = entry.Send();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryLocked, result.Error);
    }

    [Fact]
    public void Update_WhenApproved_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);
        Assert.True(entry.MarkApproved().IsSuccess);

        Result result = entry.Update(null, 1, "x", null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmBriefErrors.EntryLocked, result.Error);
    }

    [Fact]
    public void Update_WhenPending_Fails()
    {
        CrmBriefSpace entry = CreateEntry();
        Assert.True(entry.Send().IsSuccess);

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
        Assert.True(original.Send().IsSuccess);
        Assert.True(original.MarkApproved().IsSuccess);

        Result<CrmBriefSpace> result = CrmBriefSpace.CreateSuperseding(original, null, Guid.NewGuid(), requirements: "corrected");

        Assert.True(result.IsSuccess);
        Assert.Equal(original.Id, result.Value!.SupersedesSpaceId);
        Assert.Equal(ApprovalState.Draft, result.Value.ApprovalStatus);
    }
}
