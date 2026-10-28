using System.Data.Common;
using Crm.Application.Features.Meetings;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>FINDING-P1-5 (D-13): the accept-vs-decline race made deterministic. A competing write to the
/// meeting row (what an accept commits) lands after the decline has loaded and before it saves; the decline's
/// save must then lose on the meeting's <c>xmin</c> — a 409, never a second commit.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task D13_ADeclineThatLosesTheRaceForTheMeetingRow_Is409_AndChangesNothing()
    {
        MeetingProject m = await SeedAsync("d13-xmin");
        Guid meetingId = await ProposeAsync(m, Slot(3));
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;

        DbContextOptions<CrmDbContext> options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new TouchMeetingBeforeSave(fixture.ConnectionString, meetingId))
            .Options;

        Result declined;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        await using (var db = new CrmDbContext(options))
        {
            declined = await DeclineMeetingSlot.RunAsync(
                db, scope.ServiceProvider.GetRequiredService<IProjectDirectory>(), slotId, null, m.ContactId, [Slot(6)], CancellationToken.None);
        }

        AssertError(declined, 409, "crm.meetings.ConcurrentUpdate");
        Domain.CrmMeetingSlot slot = Assert.Single(await LoadSlotsAsync(meetingId));
        Assert.Equal(Domain.MeetingSlotStatus.Proposed, slot.Status);
    }

    /// <summary>Commits a no-op UPDATE of the meeting (bumping its xmin) on another connection, once, just
    /// before the decline's own save — the interleaving a concurrent accept produces.</summary>
    private sealed class TouchMeetingBeforeSave(string connectionString, Guid meetingId) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = $"""UPDATE crm.crm_meetings SET "Title" = "Title" WHERE "Id" = '{meetingId}'""";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
