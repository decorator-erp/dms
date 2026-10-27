using BuildingBlocks.Outbox;
using Crm.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-8 (M4): two accepts racing on one meeting — exactly one schedules it, the other is a 409, and
/// one "scheduled" event is raised.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B8_ParallelAcceptsOfTwoSlots_OneWins_TheOtherIs409_AndOneEventIsRaised()
    {
        MeetingProject m = await SeedAsync("b8-race");
        Guid meetingId = await ProposeAsync(m, Slot(3), Slot(4));
        List<Domain.CrmMeetingSlot> slots = await LoadSlotsAsync(meetingId);

        Result[] results = await Task.WhenAll(AcceptAsContactAsync(m, slots[0].Id), AcceptAsContactAsync(m, slots[1].Id));

        Assert.Single(results, r => r.IsSuccess);
        // A true overlap is crm.meetings.ConcurrentUpdate; if the two happen to serialize, the loser finds its
        // slot already declined (crm.meetings.SlotNotProposed). Either way it is one 409, never a 500.
        AssertError(Assert.Single(results, r => r.IsFailure), 409);
        Assert.Single(await LoadSlotsAsync(meetingId), s => s.Status == Domain.MeetingSlotStatus.Accepted);
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<string> events = await scope.ServiceProvider.GetRequiredService<SharedDbContext>().OutboxMessages.AsNoTracking()
            .Where(x => x.Type.Contains(nameof(MeetingScheduledIntegrationEvent))).Select(x => x.Content).ToListAsync();
        Assert.Single(events, c => c.Contains(meetingId.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
