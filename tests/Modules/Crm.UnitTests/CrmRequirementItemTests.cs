using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmRequirementItemTests
{
    private static CrmRequirementItem CreateEmployeeItem() =>
        CrmRequirementItem.CreateByEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1).Value!;

    [Fact]
    public void CreateByEmployee_WithValidSource_Succeeds()
    {
        var meetingId = Guid.NewGuid();

        var result = CrmRequirementItem.CreateByEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1,
            sourceType: RequirementSource.Meeting, meetingId: meetingId);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.Draft, result.Value!.ApprovalStatus);
    }

    [Fact]
    public void CreateByEmployee_WithSourceTypeButNoMatchingId_Fails()
    {
        var result = CrmRequirementItem.CreateByEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1,
            sourceType: RequirementSource.Meeting);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmItemErrors.InvalidSource, result.Error);
    }

    [Fact]
    public void CreateByEmployee_WithIdButNoSourceType_Fails()
    {
        var result = CrmRequirementItem.CreateByEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1,
            meetingId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmItemErrors.InvalidSource, result.Error);
    }

    [Fact]
    public void CreateByEmployee_WithTwoSourceIdsSet_Fails()
    {
        var result = CrmRequirementItem.CreateByEmployee(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1,
            sourceType: RequirementSource.Meeting, meetingId: Guid.NewGuid(), briefId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmItemErrors.InvalidSource, result.Error);
    }

    [Fact]
    public void CreateByClient_IsTrustedImmediately()
    {
        var item = CrmRequirementItem.CreateByClient(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1);

        Assert.Equal(ApprovalState.NotRequired, item.ApprovalStatus);
        Assert.Equal(RequirementSource.ClientDirect, item.SourceType);
        Assert.True(item.IsLocked);
    }

    [Fact]
    public void Send_ByEmployeeAuthor_Succeeds()
    {
        var item = CreateEmployeeItem();

        var result = item.Send();

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.PendingApproval, item.ApprovalStatus);
    }

    [Fact]
    public void Send_ByClientAuthor_Fails()
    {
        var item = CrmRequirementItem.CreateByClient(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1);

        var result = item.Send();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmItemErrors.SendRequiresEmployeeAuthor, result.Error);
    }

    [Fact]
    public void Update_WhenLocked_Fails()
    {
        var item = CreateEmployeeItem();
        Assert.True(item.Send().IsSuccess);
        Assert.True(item.MarkApproved().IsSuccess);

        var result = item.Update((RequirementCategory)2, "New", (TaskPriority)2, null, null, null, null, null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmItemErrors.ItemLocked, result.Error);
    }

    [Fact]
    public void CreateSuperseding_WhenOriginalNotLocked_Fails()
    {
        var original = CreateEmployeeItem();

        var result = CrmRequirementItem.CreateSuperseding(
            original, Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmItemErrors.OnlyLockedSuperseded, result.Error);
    }

    [Fact]
    public void CreateSuperseding_WhenOriginalLocked_Succeeds()
    {
        var original = CreateEmployeeItem();
        Assert.True(original.Send().IsSuccess);
        Assert.True(original.MarkApproved().IsSuccess);

        var result = CrmRequirementItem.CreateSuperseding(
            original, Guid.NewGuid(), (RequirementCategory)1, "Corrected", (TaskPriority)2);

        Assert.True(result.IsSuccess);
        Assert.Equal(original.Id, result.Value!.SupersedesItemId);
        Assert.Equal(ApprovalState.Draft, result.Value.ApprovalStatus);
    }

    [Fact]
    public void CreateSuperseding_WhenOriginalNotRequired_AlsoSucceeds()
    {
        // NotRequired (client-authored, trusted) counts as locked too - IsLocked covers both terminal
        // states, and a correction must be possible even for a client-authored entry.
        var original = CrmRequirementItem.CreateByClient(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RequirementCategory)1, "Title", (TaskPriority)1);

        var result = CrmRequirementItem.CreateSuperseding(
            original, Guid.NewGuid(), (RequirementCategory)1, "Corrected", (TaskPriority)1);

        Assert.True(result.IsSuccess);
    }
}
