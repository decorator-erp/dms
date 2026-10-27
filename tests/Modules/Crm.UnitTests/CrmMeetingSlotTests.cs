using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmMeetingSlotTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        Result<CrmMeetingSlot> result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSlotStatus.Proposed, result.Value!.Status);
    }

    [Fact]
    public void Create_WithBothProposers_Fails()
    {
        Result<CrmMeetingSlot> result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidProposer, result.Error);
    }

    [Fact]
    public void Create_WithEndBeforeStart_Fails()
    {
        Result<CrmMeetingSlot> result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(-1), Guid.NewGuid(), null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidSlotRange, result.Error);
    }

    [Fact]
    public void Create_InThePast_Fails()
    {
        Result<CrmMeetingSlot> result = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(-1), Now.AddDays(-1).AddHours(1), Guid.NewGuid(), null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidSlotRange, result.Error);
    }

    [Fact]
    public void Accept_WhenProposed_Succeeds()
    {
        CrmMeetingSlot slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;

        Result result = slot.Accept(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSlotStatus.Accepted, slot.Status);
    }

    [Fact]
    public void Accept_WhenAlreadyAccepted_Fails()
    {
        CrmMeetingSlot slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;
        Assert.True(slot.Accept(Now).IsSuccess);

        Result result = slot.Accept(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.SlotNotProposed, result.Error);
    }

    [Fact]
    public void Decline_WhenProposed_Succeeds()
    {
        CrmMeetingSlot slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;

        Result result = slot.Decline();

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSlotStatus.Declined, slot.Status);
    }

    [Fact]
    public void Accept_WhenDeclined_Fails()
    {
        CrmMeetingSlot slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;
        Assert.True(slot.Decline().IsSuccess);

        Result result = slot.Accept(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.SlotNotProposed, result.Error);
    }

    [Fact]
    public void Decline_WhenAlreadyDeclined_Fails()
    {
        CrmMeetingSlot slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;
        Assert.True(slot.Decline().IsSuccess);

        Result result = slot.Decline();

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.SlotNotProposed, result.Error);
    }

    [Fact]
    public void Demote_FromAccepted_SetsDeclined()
    {
        CrmMeetingSlot slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;
        Assert.True(slot.Accept(Now).IsSuccess);

        slot.Demote();

        Assert.Equal(MeetingSlotStatus.Declined, slot.Status);
    }

    /// <summary>B-7 (M3).</summary>
    [Fact]
    public void Accept_AtOrAfterItsStart_IsSlotExpired()
    {
        CrmMeetingSlot slot = CrmMeetingSlot.Create(
            Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), Guid.NewGuid(), null, Now).Value!;

        Assert.Equal(CrmMeetingErrors.SlotExpired, slot.Accept(Now.AddDays(1)).Error);
        Assert.Equal(MeetingSlotStatus.Proposed, slot.Status);
    }
}
