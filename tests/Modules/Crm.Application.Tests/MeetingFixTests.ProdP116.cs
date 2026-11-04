using System.Data.Common;
using Crm.Application.Features.Meetings;
using Crm.Contracts.Meetings;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>FINDING-P1-16 (D-13): the other half of the accept-vs-decline race. The accept reads the slot, a
/// decline of that slot commits, and only then does the accept read the meeting — so the meeting's
/// <c>xmin</c> it holds is already the post-decline one, and the slot row it updates carries no token. The
/// accept must still lose: a 409, the slot stays Declined and the meeting is not scheduled.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task P116_AnAcceptWhoseSlotIsDeclinedBetweenItsSlotAndMeetingReads_Is409_AndSchedulesNothing()
    {
        MeetingProject m = await SeedAsync("p116-stale-slot");
        Guid meetingId = await ProposeAsync(m, Slot(3));
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;

        Result? declined = null;
        var declineAfterSlotRead = new RunOnceAfterFirstSlotRead(async () =>
            declined = await Command(m.P.ClientAppUserId,
                new DeclineCrmMeetingSlotByContactCommand(slotId, m.ContactId, [Slot(6)]), readAll: false));

        DbContextOptions<CrmDbContext> options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(declineAfterSlotRead)
            .Options;

        Result accepted;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        await using (var db = new CrmDbContext(options))
        {
            accepted = await AcceptMeetingSlot.RunAsync(
                db, scope.ServiceProvider.GetRequiredService<IProjectDirectory>(), slotId, null, m.ContactId, CancellationToken.None);
        }

        Assert.NotNull(declined);
        Assert.True(declined.IsSuccess, declined.Error?.Description);
        AssertError(accepted, 409);

        List<Domain.CrmMeetingSlot> slots = await LoadSlotsAsync(meetingId);
        Assert.Equal(Domain.MeetingSlotStatus.Declined, slots.Single(s => s.Id == slotId).Status);
        Assert.DoesNotContain(slots, s => s.Status == Domain.MeetingSlotStatus.Accepted);
        Assert.NotEqual(Domain.MeetingStatus.Scheduled, (await LoadMeetingAsync(meetingId)).Status);
    }

    /// <summary>Runs <paramref name="interleave"/> to completion, once, right after the first query that reads
    /// <c>crm_meeting_slots</c> has executed — the accept's own slot load — and before the handler's next
    /// statement.</summary>
    private sealed class RunOnceAfterFirstSlotRead(Func<Task> interleave) : DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!_done && command.CommandText.Contains("crm_meeting_slots", StringComparison.Ordinal))
            {
                _done = true;
                await interleave();
            }

            return result;
        }
    }
}
