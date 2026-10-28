using BuildingBlocks.Pagination;
using Crm.Contracts.Meetings;
using SharedKernel;
using Xunit;
using MeetingType = Crm.Contracts.MeetingType;

namespace Crm.Application.Tests;

/// <summary>B-3 (M1, owner amendment M1): every office meeting message is project-scoped — always, with
/// no caller-influenced skip. The portal sends its own contact command types instead.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B3_AnEmployeeOffTheProject_Gets404_OnEveryOfficeMeetingMessage()
    {
        MeetingProject m = await SeedAsync("b3-outsider");
        Guid meetingId = await ProposeAsync(m, Slot(3), Slot(4));
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;
        (Guid outsiderId, string outsider) = await HireEmployeeAsync("b3-outsider");

        Result[] results =
        [
            Plain(await CommandFor<ProposeCrmMeetingCommand, Guid>(outsider,
                new ProposeCrmMeetingCommand(m.P.ProjectId, outsiderId, "Sneaky", MeetingType.InOffice, [Slot(5)], outsider))),
            await Command(outsider, new AcceptCrmMeetingSlotCommand(slotId, outsiderId, outsider)),
            await Command(outsider, new DeclineCrmMeetingSlotCommand(slotId, outsiderId, outsider)),
            await Command(outsider, new RescheduleCrmMeetingCommand(meetingId, outsiderId, outsider, At(6), At(6).AddHours(1))),
            await Command(outsider, new CancelCrmMeetingCommand(meetingId, outsiderId, outsider, "Not mine")),
            await Command(outsider, new CompleteCrmMeetingCommand(meetingId, outsider)),
            await Command(outsider, new MarkCrmMeetingNoShowCommand(meetingId, outsider)),
            Plain(await QueryFor<GetCrmMeetingByIdQuery, CrmMeetingResponse>(outsider, new GetCrmMeetingByIdQuery(meetingId, outsider))),
            Plain(await QueryFor<GetCrmMeetingsByProjectQuery, PagedResult<CrmMeetingResponse>>(outsider,
                new GetCrmMeetingsByProjectQuery(m.P.ProjectId, outsider, 1, 20))),
            Plain(await QueryFor<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>(outsider,
                new GetCrmMeetingSlotsByMeetingQuery(meetingId, outsider))),
            Plain(await QueryFor<GetFreeMeetingSlotsQuery, IReadOnlyList<FreeSlotResponse>>(outsider,
                new GetFreeMeetingSlotsQuery(m.P.ProjectId, outsider, At(1), At(3)))),
        ];

        Assert.All(results, r => AssertError(r, 404));
        Assert.Equal(Domain.MeetingStatus.Requested, (await LoadMeetingAsync(meetingId)).Status);
    }

    [Fact]
    public async Task B3_AStageTeamMember_WorksTheProjectsMeetings_WithoutReadAll()
    {
        (Guid headId, _) = await HireEmployeeAsync("b3-head");
        MeetingProject m = await SeedAsync("b3-member", headId);
        (Guid memberId, string member) = await HireEmployeeAsync("b3-member");
        await using (Microsoft.Extensions.DependencyInjection.AsyncServiceScope scope = fixture.CreateScope())
        {
            await BriefTestSeeding.AddStageTeamMemberAsync(scope.ServiceProvider, fixture.ConnectionString, m.P, m.P.StageId, memberId);
        }

        Result<Guid> proposed = await CommandFor<ProposeCrmMeetingCommand, Guid>(member,
            new ProposeCrmMeetingCommand(m.P.ProjectId, memberId, "Site visit", MeetingType.OnSite, [Slot(3)], member), readAll: false);
        Assert.True(proposed.IsSuccess, proposed.Error?.Description);
        Result<CrmMeetingResponse> read = await QueryFor<GetCrmMeetingByIdQuery, CrmMeetingResponse>(
            member, new GetCrmMeetingByIdQuery(proposed.Value, member), readAll: false);
        Assert.True(read.IsSuccess, read.Error?.Description);
    }
}
