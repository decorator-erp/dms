using Crm.Contracts.Meetings;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / A-F4: the portal's meeting writes refuse a project that is no longer active — 404, the
/// same answer as a project that isn't the client's.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task D16_A4_PortalMeetingWrites_OnAnInactiveProject_Are404()
    {
        MeetingProject m = await SeedAsync("d16-a4");
        Guid meetingId = await ProposeAsync(m, Slot(3), Slot(4));
        List<Domain.CrmMeetingSlot> slots = await LoadSlotsAsync(meetingId);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """UPDATE projects.projects SET "IsActive" = false WHERE "Id" = @id""";
            command.Parameters.AddWithValue("id", m.P.ProjectId);
            await command.ExecuteNonQueryAsync();
        }

        string client = m.P.ClientAppUserId;
        Result[] results =
        [
            await Command(client, new AcceptCrmMeetingSlotByContactCommand(slots[0].Id, m.ContactId), readAll: false),
            await Command(client, new DeclineCrmMeetingSlotByContactCommand(slots[1].Id, m.ContactId), readAll: false),
            await Command(client, new RescheduleCrmMeetingByContactCommand(meetingId, m.ContactId, Slot(9).StartsAtUtc, Slot(9).EndsAtUtc), readAll: false),
            await Command(client, new CancelCrmMeetingByContactCommand(meetingId, m.ContactId, "Not needed"), readAll: false),
        ];

        foreach (Result result in results)
        {
            AssertError(result, 404, "crm.portal.NotFound");
        }
    }
}
