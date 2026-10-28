using Crm.Contracts;
using Crm.Contracts.Meetings;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / B-F3: a slot lasts at most 480 minutes, on every path that creates one — propose, a
/// client's request, a reschedule (office and portal) and the counter-slots of a decline. 480 itself passes.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task D16_B3_ASlotLongerThan480Minutes_Is400_OnEveryPath()
    {
        MeetingProject m = await SeedAsync("d16-b3");
        CrmMeetingSlotRequest tooLong = Slot(5, minutes: 481);
        Guid meetingId = await ProposeAsync(m, Slot(3, minutes: 480), Slot(4));
        Guid slotId = (await LoadSlotsAsync(meetingId))[1].Id;
        string client = m.P.ClientAppUserId;

        Result[] results =
        [
            Plain(await TryProposeAsync(m, tooLong)),
            Plain(await CommandFor<RequestCrmMeetingByClientCommand, Guid>(client, new RequestCrmMeetingByClientCommand(
                m.P.ProjectId, m.ContactId, "Site visit", MeetingType.InOffice, tooLong.StartsAtUtc, tooLong.EndsAtUtc), readAll: false)),
            await Command(client, new DeclineCrmMeetingSlotByContactCommand(slotId, m.ContactId, [tooLong]), readAll: false),
            await Command(client, new RescheduleCrmMeetingByContactCommand(meetingId, m.ContactId, tooLong.StartsAtUtc, tooLong.EndsAtUtc), readAll: false),
            await RescheduleAsEmployeeAsync(m, meetingId, tooLong),
        ];

        foreach (Result result in results)
        {
            AssertError(result, 400);
        }
    }
}
