using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-6 (M2): after a reschedule, the meeting lasts as long as the slot that was accepted.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B6_AfterAReschedule_TheDurationIsTheNewlyAcceptedSlotsLength()
    {
        MeetingProject m = await SeedAsync("b6-duration");
        Guid meetingId = await ProposeAsync(m, Slot(3, 10, 60));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);
        Assert.Equal(60, (await LoadMeetingAsync(meetingId)).DurationMinutes);

        Assert.True((await RescheduleAsEmployeeAsync(m, meetingId, Slot(5, 10, 90))).IsSuccess);
        Guid newSlot = (await LoadSlotsAsync(meetingId)).Single(s => s.Status == Domain.MeetingSlotStatus.Proposed).Id;
        Assert.True((await AcceptAsContactAsync(m, newSlot)).IsSuccess);

        Assert.Equal(90, (await LoadMeetingAsync(meetingId)).DurationMinutes);
    }
}
