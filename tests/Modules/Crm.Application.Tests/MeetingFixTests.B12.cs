using Crm.Contracts.Meetings;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-12 (L4): a soft-deleted meeting is gone from every office read, not only the list.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B12_AnInactiveMeeting_Is404_OnGetByIdAndOnItsSlots()
    {
        MeetingProject m = await SeedAsync("b12-inactive");
        Guid meetingId = await ProposeAsync(m, Slot(3));

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """UPDATE crm.crm_meetings SET "IsActive" = false WHERE "Id" = @id""";
            command.Parameters.AddWithValue("id", meetingId);
            await command.ExecuteNonQueryAsync();
        }

        string owner = m.P.OwnerAppUserId;
        AssertError(Plain(await QueryFor<GetCrmMeetingByIdQuery, CrmMeetingResponse>(owner, new GetCrmMeetingByIdQuery(meetingId, owner))),
            404, "crm.meetings.NotFound");
        AssertError(Plain(await QueryFor<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>(owner,
            new GetCrmMeetingSlotsByMeetingQuery(meetingId, owner))), 404, "crm.meetings.NotFound");
    }
}
