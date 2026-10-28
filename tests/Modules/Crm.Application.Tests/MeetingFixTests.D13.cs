using Crm.Contracts.Meetings;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-13 (B-F2): a decline writes the meeting row, so it collides with an accept on the meeting's
/// <c>xmin</c>, and only a meeting still being arranged (Requested or Rescheduled) can have a slot declined.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task D13_DecliningASlot_OfACanceledMeeting_Is409_AndAddsNoCounterSlots()
    {
        MeetingProject m = await SeedAsync("d13-canceled");
        Guid meetingId = await ProposeAsync(m, Slot(3));
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;
        Assert.True((await CancelAsEmployeeAsync(m, meetingId)).IsSuccess);

        Result declined = await Command(m.P.ClientAppUserId,
            new DeclineCrmMeetingSlotByContactCommand(slotId, m.ContactId, [Slot(5)]), readAll: false);

        // Cancel already withdraws its open slots (SlotNotProposed); the meeting-status rule backs that up.
        AssertError(declined, 409);
        Assert.Single(await LoadSlotsAsync(meetingId));
    }

    [Fact]
    public async Task D13_ParallelAcceptAndDecline_OfOneSlot_OneSucceeds_TheOtherIs409()
    {
        for (int round = 0; round < 5; round++)
        {
            MeetingProject m = await SeedAsync($"d13-race-{round}");
            Guid meetingId = await ProposeAsync(m, Slot(3));
            Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;

            Result[] results = await Task.WhenAll(
                AcceptAsContactAsync(m, slotId),
                Command(m.P.ClientAppUserId, new DeclineCrmMeetingSlotByContactCommand(slotId, m.ContactId, [Slot(6)]), readAll: false));

            Assert.Single(results, r => r.IsSuccess);
            AssertError(Assert.Single(results, r => r.IsFailure), 409);
        }
    }
}
