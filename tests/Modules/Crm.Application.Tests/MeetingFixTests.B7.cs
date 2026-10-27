using Npgsql;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-7 (M3): a slot whose start has passed cannot be accepted — that meeting could never happen.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B7_AcceptingASlotWhoseStartHasPassed_Is409_SlotExpired()
    {
        MeetingProject m = await SeedAsync("b7-expired");
        Guid meetingId = await ProposeAsync(m, Slot(3));
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;

        // Time passes: the proposal sat unanswered until after its start.
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """UPDATE crm.crm_meeting_slots SET "StartsAtUtc" = now() - interval '1 hour', "EndsAtUtc" = now() WHERE "Id" = @id""";
            command.Parameters.AddWithValue("id", slotId);
            await command.ExecuteNonQueryAsync();
        }

        Result accepted = await AcceptAsContactAsync(m, slotId);

        AssertError(accepted, 409, "crm.meetings.SlotExpired");
        Assert.Equal(Domain.MeetingStatus.Requested, (await LoadMeetingAsync(meetingId)).Status);
    }
}
