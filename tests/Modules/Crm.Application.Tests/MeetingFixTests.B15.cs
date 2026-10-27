using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts.Portal;
using Crm.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-15 (MT-N7, MT-N8): the portal meetings list is paged, and notification times are written in
/// the office time zone with the zone named.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B15_ThePortalMeetingsList_IsPaged_WithTheTotalCount()
    {
        MeetingProject m = await SeedAsync("b15-paged");
        for (int i = 0; i < 3; i++)
        {
            await ProposeAsync(m, Slot(3 + i));
        }

        PagedResult<PortalMeetingResponse> first = await PortalMeetingsAsync(m, 1, 2);
        PagedResult<PortalMeetingResponse> second = await PortalMeetingsAsync(m, 2, 2);

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
    }

    [Fact]
    public async Task B15_MeetingTimes_AreShownInCairoTime_WithTheZoneNamed()
    {
        var utc = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2026-11-02 14:00 (Africa/Cairo)", MeetingTimes.Display(utc, "Africa/Cairo"));

        MeetingProject m = await SeedAsync("b15-zone");
        Guid meetingId = await ProposeAsync(m, SlotIn(30));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);
        await DrainOutboxAsync();

        NotificationResponse scheduled = Assert.Single(await NotificationsAsync(m.P.ClientAppUserId, meetingId),
            n => n.Type == NotificationType.CrmMeetingScheduled);
        Assert.Contains("(Africa/Cairo)", scheduled.Body, StringComparison.Ordinal);
    }

    private async Task<PagedResult<PortalMeetingResponse>> PortalMeetingsAsync(MeetingProject m, int page, int size)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<PagedResult<PortalMeetingResponse>> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetMyPortalMeetingsQuery, PagedResult<PortalMeetingResponse>>>()
            .HandleAsync(new GetMyPortalMeetingsQuery(m.P.ClientAppUserId, page, size));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value!;
    }
}
