using Crm.Contracts.Meetings;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-4 (MT-N2): a contact from another client — or a contact naming an unknown slot or meeting —
/// gets the portal's 404, never a 403 that confirms the meeting exists. The tenant check runs before the
/// actor-kind check.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B4_ACrossTenantContact_Gets404_OnEveryContactMeetingCommand()
    {
        MeetingProject mine = await SeedAsync("b4-mine");
        MeetingProject stranger = await SeedAsync("b4-stranger");
        Guid meetingId = await ProposeAsync(mine, Slot(3), Slot(4));
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;
        Guid s = stranger.ContactId;

        Result[] results =
        [
            await Command(stranger.P.ClientAppUserId, new AcceptCrmMeetingSlotByContactCommand(slotId, s), readAll: false),
            await Command(stranger.P.ClientAppUserId, new DeclineCrmMeetingSlotByContactCommand(slotId, s), readAll: false),
            await Command(stranger.P.ClientAppUserId, new RescheduleCrmMeetingByContactCommand(meetingId, s, At(6), At(6).AddHours(1)), readAll: false),
            await Command(stranger.P.ClientAppUserId, new CancelCrmMeetingByContactCommand(meetingId, s, "Not mine"), readAll: false),
            await Command(stranger.P.ClientAppUserId, new AcceptCrmMeetingSlotByContactCommand(Guid.NewGuid(), s), readAll: false),
            await Command(stranger.P.ClientAppUserId, new CancelCrmMeetingByContactCommand(Guid.NewGuid(), s, "Unknown"), readAll: false),
        ];

        Assert.All(results, r => AssertError(r, 404, "crm.portal.NotFound"));
        Assert.Equal(Domain.MeetingStatus.Requested, (await LoadMeetingAsync(meetingId)).Status);
    }
}
