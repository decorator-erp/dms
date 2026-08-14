using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmMeetingSlotTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSlotStatus.Proposed, result.Value!.Status);
    }

    [Fact]
    public void Create_WithBothProposers_Fails()
    {
        var result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidProposer, result.Error);
    }

    [Fact]
    public void Create_WithEndBeforeStart_Fails()
    {
        var result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(-1), Guid.NewGuid(), null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidSlotRange, result.Error);
    }

    [Fact]
    public void Create_InThePast_Fails()
    {
        var result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(-1), Now.AddDays(-1).AddHours(1), Guid.NewGuid(), null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidSlotRange, result.Error);
    }

    [Fact]
    public void Accept_WhenProposed_Succeeds()
    {
        var slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;

        var result = slot.Accept();

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSlotStatus.Accepted, slot.Status);
    }

    [Fact]
    public void Accept_WhenAlreadyAccepted_Fails()
    {
        var slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;
        Assert.True(slot.Accept().IsSuccess);

        var result = slot.Accept();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.SlotNotProposed, result.Error);
    }

    [Fact]
    public void Decline_WhenProposed_Succeeds()
    {
        var slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;

        var result = slot.Decline();

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSlotStatus.Declined, slot.Status);
    }

    [Fact]
    public void Demote_FromAccepted_SetsDeclined()
    {
        var slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;
        Assert.True(slot.Accept().IsSuccess);

        slot.Demote();

        Assert.Equal(MeetingSlotStatus.Declined, slot.Status);
    }
}
