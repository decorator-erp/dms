using Crm.Contracts.Meetings;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-9 (M6, MT-N9): free slots leave out times already booked, and never offer the past.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B9_ABookedSlot_IsNoLongerFree()
    {
        MeetingProject m = await SeedAsync("b9-booked");
        IReadOnlyList<FreeSlotResponse> before = await FreeSlotsAsync(m, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(9));
        Assert.True(before.Count > 2, "The office calendar should offer several free slots in a week.");
        FreeSlotResponse booked = before[1];

        Guid meetingId = await ProposeAsync(m, new CrmMeetingSlotRequest(booked.StartsAtUtc, booked.EndsAtUtc));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);

        IReadOnlyList<FreeSlotResponse> after = await FreeSlotsAsync(m, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(9));
        Assert.DoesNotContain(after, s => s.StartsAtUtc == booked.StartsAtUtc);
        Assert.Contains(after, s => s.StartsAtUtc == before[0].StartsAtUtc);
    }

    [Fact]
    public async Task B9_AWindowStartingInThePast_OffersOnlyFutureSlots()
    {
        MeetingProject m = await SeedAsync("b9-past");

        IReadOnlyList<FreeSlotResponse> slots = await FreeSlotsAsync(m, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5));

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(s.StartsAtUtc > DateTime.UtcNow.AddMinutes(-1), $"{s.StartsAtUtc:O} is in the past"));
    }

    private async Task<IReadOnlyList<FreeSlotResponse>> FreeSlotsAsync(MeetingProject m, DateTime fromUtc, DateTime toUtc)
    {
        Result<IReadOnlyList<FreeSlotResponse>> result = await QueryFor<GetFreeMeetingSlotsQuery, IReadOnlyList<FreeSlotResponse>>(
            m.P.OwnerAppUserId, new GetFreeMeetingSlotsQuery(m.P.ProjectId, m.P.OwnerAppUserId, fromUtc, toUtc));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value!;
    }
}
