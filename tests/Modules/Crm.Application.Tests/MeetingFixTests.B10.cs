using Crm.Contracts.Meetings;
using SharedKernel;
using Xunit;
using MeetingType = Crm.Contracts.MeetingType;

namespace Crm.Application.Tests;

/// <summary>B-10 (L1): meeting input is validated up front — per-slot ranges, at most 10 slots, a 5..480
/// minute duration, and every time must be UTC. A non-UTC time used to reach Npgsql and fail as a 500.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B10_ANonUtcTime_Is400_NotA500()
    {
        MeetingProject m = await SeedAsync("b10-kind");
        var unspecified = DateTime.SpecifyKind(At(3), DateTimeKind.Unspecified);

        Result proposed = await SafeAsync(() => TryProposeAsync(m, new CrmMeetingSlotRequest(unspecified, unspecified.AddHours(1))));
        AssertError(proposed, 400);

        Result requested = await SafeAsync(() => CommandFor<RequestCrmMeetingByClientCommand, Guid>(m.P.ClientAppUserId,
            new RequestCrmMeetingByClientCommand(m.P.ProjectId, m.ContactId, "Visit", MeetingType.OnSite,
                At(3).ToLocalTime(), At(3).ToLocalTime().AddHours(1)), readAll: false));
        AssertError(requested, 400);
    }

    [Fact]
    public async Task B10_SlotRangesCountsAndDurations_AreValidated()
    {
        MeetingProject m = await SeedAsync("b10-rules");
        Guid meetingId = await ProposeAsync(m, Slot(3));
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;

        AssertError(Plain(await TryProposeAsync(m, Slot(3), new CrmMeetingSlotRequest(At(4), At(4).AddHours(-1)))), 400);
        AssertError(Plain(await TryProposeAsync(m, [.. Enumerable.Range(1, 11).Select(d => Slot(d))])), 400);
        AssertError(Plain(await CommandFor<ProposeCrmMeetingCommand, Guid>(m.P.OwnerAppUserId,
            new ProposeCrmMeetingCommand(m.P.ProjectId, m.P.EmployeeId, "Short", MeetingType.InOffice, [Slot(3)], m.P.OwnerAppUserId,
                DurationMinutes: 4))), 400);
        AssertError(Plain(await CommandFor<ProposeCrmMeetingCommand, Guid>(m.P.OwnerAppUserId,
            new ProposeCrmMeetingCommand(m.P.ProjectId, m.P.EmployeeId, new string('t', 257), MeetingType.InOffice, [Slot(3)], m.P.OwnerAppUserId))), 400);

        AssertError(await Command(m.P.ClientAppUserId, new DeclineCrmMeetingSlotByContactCommand(slotId, m.ContactId,
            [.. Enumerable.Range(1, 11).Select(d => Slot(d))]), readAll: false), 400);
        AssertError(await Command(m.P.ClientAppUserId, new DeclineCrmMeetingSlotByContactCommand(slotId, m.ContactId,
            [new CrmMeetingSlotRequest(At(5), At(5).AddMinutes(-30))]), readAll: false), 400);
    }

    /// <summary>Turns an exception into a 500-shaped failure, so the red run reports what the API did.</summary>
    private static async Task<Result> SafeAsync<T>(Func<Task<Result<T>>> action)
    {
        try
        {
            return Plain(await action());
        }
        catch (Exception ex)
        {
            return Error.Unexpected($"500: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
