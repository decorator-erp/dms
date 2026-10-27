using Crm.Contracts.Meetings;
using Crm.Contracts.Portal;
using SharedKernel;
using Xunit;
using MeetingSlotStatus = Crm.Contracts.MeetingSlotStatus;
using MeetingStatus = Crm.Contracts.MeetingStatus;

namespace Crm.Application.Tests;

/// <summary>B-1 (H2/X-7, MT-N1): the client answers a staff proposal from the portal — a portal-only detail
/// route with the slots, scoped to the caller's own client. The client never gets <c>crm.meetings.read</c>.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B1_StaffProposes_TheClientReadsThePortalDetail_AndAcceptsASlotFromIt()
    {
        MeetingProject m = await SeedAsync("b1-flow");
        Guid meetingId = await ProposeAsync(m);

        Result<PortalMeetingDetailResponse> detail = await QueryFor<GetMyPortalMeetingByIdQuery, PortalMeetingDetailResponse>(
            m.P.ClientAppUserId, new GetMyPortalMeetingByIdQuery(m.P.ClientAppUserId, meetingId), readAll: false);
        Assert.True(detail.IsSuccess, detail.Error?.Description);
        Assert.Equal(2, detail.Value!.Slots.Count);
        Assert.All(detail.Value.Slots, s => Assert.False(s.ProposedByMe));
        Assert.DoesNotContain(typeof(PortalMeetingSlotResponse).GetProperties(), p => p.Name.Contains("Employee", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(PortalMeetingDetailResponse).GetProperties(), p => p.Name.Contains("Employee", StringComparison.Ordinal));

        Guid slotId = detail.Value.Slots[0].Id;
        Result accepted = await AcceptAsContactAsync(m, slotId);
        Assert.True(accepted.IsSuccess, accepted.Error?.Description);

        Result<PortalMeetingDetailResponse> after = await QueryFor<GetMyPortalMeetingByIdQuery, PortalMeetingDetailResponse>(
            m.P.ClientAppUserId, new GetMyPortalMeetingByIdQuery(m.P.ClientAppUserId, meetingId), readAll: false);
        Assert.Equal(MeetingStatus.Scheduled, after.Value!.Status);
        Assert.Equal(MeetingSlotStatus.Accepted, after.Value.Slots.Single(s => s.Id == slotId).Status);
    }

    [Fact]
    public async Task B1_AnotherClientsMeeting_OrAnUnknownOne_Is404_OnThePortalDetail()
    {
        MeetingProject mine = await SeedAsync("b1-mine");
        MeetingProject other = await SeedAsync("b1-other");
        Guid otherMeeting = await ProposeAsync(other);

        foreach (Guid id in new[] { otherMeeting, Guid.NewGuid() })
        {
            Result<PortalMeetingDetailResponse> detail = await QueryFor<GetMyPortalMeetingByIdQuery, PortalMeetingDetailResponse>(
                mine.P.ClientAppUserId, new GetMyPortalMeetingByIdQuery(mine.P.ClientAppUserId, id), readAll: false);
            AssertError(Plain(detail), 404, "crm.portal.NotFound");
        }
    }

    private async Task<Result> AcceptAsContactAsync(MeetingProject m, Guid slotId) =>
        await Command(m.P.ClientAppUserId, new AcceptCrmMeetingSlotCommand(slotId, null, m.ContactId), readAll: false);
}
