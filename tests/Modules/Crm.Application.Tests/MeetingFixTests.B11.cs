using Xunit;
using SlotStatus = Crm.Domain.MeetingSlotStatus;

namespace Crm.Application.Tests;

/// <summary>B-11 (L2, L5): a reschedule leaves exactly one live candidate (the new slot), and a cancel
/// leaves no Accepted slot behind on a canceled meeting.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B11_Reschedule_DeclinesEveryOldProposedSlot_AndLeavesOnlyTheNewOne()
    {
        MeetingProject m = await SeedAsync("b11-resched");
        Guid meetingId = await ProposeAsync(m, Slot(3), Slot(4), Slot(5));

        AssertOk(await RescheduleAsEmployeeAsync(m, meetingId, Slot(6)));

        List<Domain.CrmMeetingSlot> slots = await LoadSlotsAsync(meetingId);
        Assert.Equal(4, slots.Count);
        Domain.CrmMeetingSlot live = Assert.Single(slots, s => s.Status == SlotStatus.Proposed);
        Assert.Equal(At(6), live.StartsAtUtc);
        Assert.All(slots.Where(s => s.Id != live.Id), s => Assert.Equal(SlotStatus.Declined, s.Status));
    }

    [Fact]
    public async Task B11_Cancel_DemotesTheAcceptedSlot()
    {
        MeetingProject m = await SeedAsync("b11-cancel");
        Guid meetingId = await ProposeAsync(m, Slot(3));
        AssertOk(await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id));

        AssertOk(await CancelAsEmployeeAsync(m, meetingId));

        Assert.DoesNotContain(await LoadSlotsAsync(meetingId), s => s.Status is SlotStatus.Accepted or SlotStatus.Proposed);
    }

    private static void AssertOk(SharedKernel.Result result) => Assert.True(result.IsSuccess, result.Error?.Description);
}
