using Crm.Contracts.Meetings;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / B-F5: a soft-deleted meeting takes no writes — accept, decline, reschedule and cancel are
/// 404 from the office and from the portal, as its reads already are (B-12).</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task D16_B5_WritesOnAnInactiveMeeting_Are404()
    {
        MeetingProject m = await SeedAsync("d16-b5");
        Guid meetingId = await ProposeAsync(m, Slot(3), Slot(4));
        List<Domain.CrmMeetingSlot> slots = await LoadSlotsAsync(meetingId);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """UPDATE crm.crm_meetings SET "IsActive" = false WHERE "Id" = @id""";
            command.Parameters.AddWithValue("id", meetingId);
            await command.ExecuteNonQueryAsync();
        }

        string client = m.P.ClientAppUserId;
        Result[] results =
        [
            await Command(client, new AcceptCrmMeetingSlotByContactCommand(slots[0].Id, m.ContactId), readAll: false),
            await Command(client, new DeclineCrmMeetingSlotByContactCommand(slots[1].Id, m.ContactId), readAll: false),
            await Command(client, new RescheduleCrmMeetingByContactCommand(meetingId, m.ContactId, Slot(9).StartsAtUtc, Slot(9).EndsAtUtc), readAll: false),
            await Command(client, new CancelCrmMeetingByContactCommand(meetingId, m.ContactId, "Not needed"), readAll: false),
            await RescheduleAsEmployeeAsync(m, meetingId, Slot(9)),
            await CancelAsEmployeeAsync(m, meetingId),
            // FINDING-P1-12 (adjacent): the office's complete and no-show too.
            await Command(m.P.OwnerAppUserId, new CompleteCrmMeetingCommand(meetingId, m.P.OwnerAppUserId)),
            await Command(m.P.OwnerAppUserId, new MarkCrmMeetingNoShowCommand(meetingId, m.P.OwnerAppUserId)),
        ];

        foreach (Result result in results)
        {
            AssertError(result, 404);
        }
    }
}
