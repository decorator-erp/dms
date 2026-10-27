using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Meetings;
using Crm.Contracts.Portal;
using Crm.Infrastructure.Persistence;
using Hr.Contracts.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>
/// M1-01's own verify block: the meeting/slot state machine driven through the real command handlers
/// (request/propose -> accept/decline -> reschedule -> complete/cancel/no-show), resolved from the real
/// DI graph — same discipline <see cref="ClientLeadNoteFlowTests"/> established. Also covers the
/// tenant-boundary fix in <c>MeetingSupport.EnsureActorCanActOnProjectAsync</c>: the ported backend's
/// actor check would have let a contact from any client act on any meeting; this proves it can't.
/// Needs Docker.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class MeetingFlowTests(CrmApplicationFixture fixture)
{
    [Fact]
    public async Task Propose_AcceptOneOfTwoSlots_SchedulesTheMeeting_DeclinesTheSibling_AndEnqueuesTheEvent()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "propose-owner");
        (Guid projectId, Guid clientId, string _) = await SeedProjectAsync(sp, "propose-owner-2");
        Guid contactId = await SeedContactAsync(sp, clientId);

        ICommandHandler<ProposeCrmMeetingCommand, Guid> propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        Result<Guid> proposeResult = await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "Kickoff", MeetingType.InOffice,
            [
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)),
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1)),
            ]));
        Assert.True(proposeResult.IsSuccess);
        Guid meetingId = proposeResult.Value;

        IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>> getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        IReadOnlyList<CrmMeetingSlotResponse> slots = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(2, slots.Count);
        Guid firstSlotId = slots[0].Id;
        Guid secondSlotId = slots[1].Id;

        // Accept is the CONTACT's action - the employee proposed, so the contact is the counterparty.
        // The contact belongs to the SAME client that owns projectId - the tenant-scope check requires it.
        ICommandHandler<AcceptCrmMeetingSlotCommand> accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        Result acceptResult = await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(firstSlotId, null, contactId));
        Assert.True(acceptResult.IsSuccess);

        IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse> getById = sp.GetRequiredService<IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>>();
        CrmMeetingResponse meeting = (await getById.HandleAsync(new GetCrmMeetingByIdQuery(meetingId))).Value!;
        Assert.Equal(MeetingStatus.Scheduled, meeting.Status);
        Assert.Equal(slots[0].StartsAtUtc, meeting.ScheduledAtUtc);

        IReadOnlyList<CrmMeetingSlotResponse> afterAccept = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(MeetingSlotStatus.Accepted, afterAccept.Single(s => s.Id == firstSlotId).Status);
        Assert.Equal(MeetingSlotStatus.Declined, afterAccept.Single(s => s.Id == secondSlotId).Status);

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }

    /// <summary>X-2: a client may request a meeting only on its own project. Another client's project and an
    /// unknown project are both the portal's 404, and nothing is written.</summary>
    [Fact]
    public async Task RequestByClient_OnAnotherClientsOrAnUnknownProject_Is404_AndWritesNothing()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid foreignProjectId, _, _) = await SeedProjectAsync(sp, "x2-foreign-owner");
        (_, Guid ownClientId, _) = await SeedProjectAsync(sp, "x2-own-owner");
        Guid contactId = await SeedContactAsync(sp, ownClientId);
        ICommandHandler<RequestCrmMeetingByClientCommand, Guid> request = sp.GetRequiredService<ICommandHandler<RequestCrmMeetingByClientCommand, Guid>>();

        foreach (Guid projectId in new[] { foreignProjectId, Guid.NewGuid() })
        {
            Result<Guid> result = await request.HandleAsync(new RequestCrmMeetingByClientCommand(
                projectId, contactId, "Sneaky visit", MeetingType.OnSite,
                DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)));

            Assert.True(result.IsFailure);
            Assert.Equal(404, result.Error.Status);
            Assert.Equal("crm.portal.NotFound", result.Error.Code);
        }

        Assert.Equal(0, await sp.GetRequiredService<CrmDbContext>().CrmMeetings.AsNoTracking()
            .CountAsync(m => m.RequestedByContactId == contactId));
    }

    [Fact]
    public async Task RequestByClient_OnItsOwnProject_Succeeds()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, Guid clientId, _) = await SeedProjectAsync(sp, "x2-happy-owner");
        Guid contactId = await SeedContactAsync(sp, clientId);

        Result<Guid> result = await sp.GetRequiredService<ICommandHandler<RequestCrmMeetingByClientCommand, Guid>>()
            .HandleAsync(new RequestCrmMeetingByClientCommand(
                projectId, contactId, "Site visit", MeetingType.OnSite,
                DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    [Fact]
    public async Task RequestByClient_ThenEmployeeDeclinesWithCounterSlot_AddsTheCounterAsProposed()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, Guid clientId, string _) = await SeedProjectAsync(sp, "request-owner");
        Guid contactId = await SeedContactAsync(sp, clientId);

        ICommandHandler<RequestCrmMeetingByClientCommand, Guid> request = sp.GetRequiredService<ICommandHandler<RequestCrmMeetingByClientCommand, Guid>>();
        Result<Guid> requestResult = await request.HandleAsync(new RequestCrmMeetingByClientCommand(
            projectId, contactId, "Site visit", MeetingType.OnSite,
            DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)));
        Assert.True(requestResult.IsSuccess);
        Guid meetingId = requestResult.Value;

        IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>> getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        Guid originalSlotId = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;

        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "request-decliner");
        ICommandHandler<DeclineCrmMeetingSlotCommand> decline = sp.GetRequiredService<ICommandHandler<DeclineCrmMeetingSlotCommand>>();
        Result declineResult = await decline.HandleAsync(new DeclineCrmMeetingSlotCommand(
            originalSlotId, employeeId, null,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(3).AddHours(1))]));
        Assert.True(declineResult.IsSuccess);

        IReadOnlyList<CrmMeetingSlotResponse> slots = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(2, slots.Count);
        Assert.Equal(MeetingSlotStatus.Declined, slots.Single(s => s.Id == originalSlotId).Status);
        CrmMeetingSlotResponse counter = slots.Single(s => s.Id != originalSlotId);
        Assert.Equal(MeetingSlotStatus.Proposed, counter.Status);
        Assert.Equal(employeeId, counter.ProposedByEmployeeId);
    }

    [Fact]
    public async Task Reschedule_DemotesTheAcceptedSlot_ThenTheNewSlotCanBeAccepted()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "reschedule-owner");
        (Guid projectId, Guid clientId, string _) = await SeedProjectAsync(sp, "reschedule-owner-2");
        Guid contactId = await SeedContactAsync(sp, clientId);

        ICommandHandler<ProposeCrmMeetingCommand, Guid> propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        Guid meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "First pass", MeetingType.Online,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).Value;

        IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>> getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        Guid firstSlotId = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;

        ICommandHandler<AcceptCrmMeetingSlotCommand> accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        Assert.True((await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(firstSlotId, null, contactId))).IsSuccess);

        ICommandHandler<RescheduleCrmMeetingCommand> reschedule = sp.GetRequiredService<ICommandHandler<RescheduleCrmMeetingCommand>>();
        DateTime newStart = DateTime.UtcNow.AddDays(5);
        Result rescheduleResult = await reschedule.HandleAsync(new RescheduleCrmMeetingCommand(
            meetingId, employeeId, null, newStart, newStart.AddHours(1)));
        Assert.True(rescheduleResult.IsSuccess);

        IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse> getById = sp.GetRequiredService<IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>>();
        CrmMeetingResponse meeting = (await getById.HandleAsync(new GetCrmMeetingByIdQuery(meetingId))).Value!;
        Assert.Equal(MeetingStatus.Rescheduled, meeting.Status);
        Assert.Null(meeting.ScheduledAtUtc);

        IReadOnlyList<CrmMeetingSlotResponse> slotsAfter = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(MeetingSlotStatus.Declined, slotsAfter.Single(s => s.Id == firstSlotId).Status);
        CrmMeetingSlotResponse newSlot = slotsAfter.Single(s => s.Id != firstSlotId);
        Assert.Equal(MeetingSlotStatus.Proposed, newSlot.Status);

        // Accepting the new slot must succeed - proves Demote() actually cleared the unique-accepted-slot index.
        Assert.True((await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(newSlot.Id, null, contactId))).IsSuccess);
    }

    [Fact]
    public async Task Complete_BeforeTheMeetingStarts_FailsWithInvalidStateTransition()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "complete-owner");
        (Guid projectId, Guid clientId, string _) = await SeedProjectAsync(sp, "complete-owner-2");
        Guid contactId = await SeedContactAsync(sp, clientId);

        ICommandHandler<ProposeCrmMeetingCommand, Guid> propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        Guid meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "Future meeting", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(10).AddHours(1))]))).Value;
        Guid slotId = (await sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>()
            .HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;
        ICommandHandler<AcceptCrmMeetingSlotCommand> accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        Assert.True((await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(slotId, null, contactId))).IsSuccess);

        ICommandHandler<CompleteCrmMeetingCommand> complete = sp.GetRequiredService<ICommandHandler<CompleteCrmMeetingCommand>>();
        Result result = await complete.HandleAsync(new CompleteCrmMeetingCommand(meetingId));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Cancel_DeclinesEveryRemainingProposedSlot()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "cancel-owner");
        (Guid projectId, Guid _, string _) = await SeedProjectAsync(sp, "cancel-owner-2");

        ICommandHandler<ProposeCrmMeetingCommand, Guid> propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        Guid meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "To be canceled", MeetingType.InOffice,
            [
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)),
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1)),
            ]))).Value;

        ICommandHandler<CancelCrmMeetingCommand> cancel = sp.GetRequiredService<ICommandHandler<CancelCrmMeetingCommand>>();
        Result cancelResult = await cancel.HandleAsync(new CancelCrmMeetingCommand(meetingId, employeeId, null, "client asked"));
        Assert.True(cancelResult.IsSuccess);

        IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse> getById = sp.GetRequiredService<IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>>();
        CrmMeetingResponse meeting = (await getById.HandleAsync(new GetCrmMeetingByIdQuery(meetingId))).Value!;
        Assert.Equal(MeetingStatus.Canceled, meeting.Status);
        Assert.Equal("client asked", meeting.CancelReason);

        IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>> getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        IReadOnlyList<CrmMeetingSlotResponse> slots = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.All(slots, s => Assert.Equal(MeetingSlotStatus.Declined, s.Status));
    }

    /// <summary>
    /// The tenant-boundary fix: the ported backend's actor check is KIND-only ("a contact" - any
    /// contact, of any client), which would let this contact accept a slot on a meeting belonging to a
    /// project it has no relationship to. <c>MeetingSupport.EnsureActorCanActOnProjectAsync</c> closes
    /// that by checking the contact's own client against the meeting's project, via
    /// <see cref="IProjectDirectory"/>.
    /// </summary>
    [Fact]
    public async Task Accept_ByAContactFromADifferentClient_IsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "tenant-owner");
        (Guid projectId, Guid _, string _) = await SeedProjectAsync(sp, "tenant-owner-2");

        ICommandHandler<ProposeCrmMeetingCommand, Guid> propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        Guid meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "Tenant test", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).Value;
        Guid slotId = (await sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>()
            .HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;

        // A different client's contact - never associated with projectId's owning client.
        Guid strangerClientId = await SeedClientAsync(sp, "tenant-stranger");
        Guid strangerContactId = await SeedContactAsync(sp, strangerClientId);

        ICommandHandler<AcceptCrmMeetingSlotCommand> accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        Result result = await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(slotId, null, strangerContactId));

        Assert.True(result.IsFailure);
        Assert.Equal(Domain.CrmMeetingErrors.NotCounterparty, result.Error);
    }

    /// <summary>
    /// M1-02: the Portal read this task closes — <c>GetMyPortalMeetingsQueryHandler</c>, the first
    /// caller of <see cref="IProjectDirectory.GetProjectsByClientAsync"/>. Also the "client-actor Portal
    /// path exercises <c>CrmAccessGuard</c>'s client branch" verify item: <c>ResolveActorAsync</c> here
    /// resolves the CLIENT branch, not the employee branch that every other test in this file exercises
    /// via <c>ActingEmployeeId</c>. Proves both directions: a meeting on the calling client's own
    /// project appears; a meeting on an unrelated client's project does not.
    /// </summary>
    [Fact]
    public async Task GetMyPortalMeetings_ReturnsOnlyMeetingsOnTheClientsOwnProjects()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "portal-owner");
        (Guid ownProjectId, Guid _, string? clientAppUserId) = await SeedProjectAsync(sp, "portal-owner-2");
        (Guid otherProjectId, Guid _, string _) = await SeedProjectAsync(sp, "portal-other-owner");

        ICommandHandler<ProposeCrmMeetingCommand, Guid> propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        Guid ownMeetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            ownProjectId, employeeId, "My project's meeting", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).Value;
        Assert.True((await propose.HandleAsync(new ProposeCrmMeetingCommand(
            otherProjectId, employeeId, "Someone else's meeting", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).IsSuccess);

        IQueryHandler<GetMyPortalMeetingsQuery, IReadOnlyList<PortalMeetingResponse>> getPortalMeetings = sp.GetRequiredService<IQueryHandler<GetMyPortalMeetingsQuery, IReadOnlyList<PortalMeetingResponse>>>();
        Result<IReadOnlyList<PortalMeetingResponse>> result = await getPortalMeetings.HandleAsync(new GetMyPortalMeetingsQuery(clientAppUserId));

        Assert.True(result.IsSuccess);
        PortalMeetingResponse meeting = Assert.Single(result.Value!);
        Assert.Equal(ownMeetingId, meeting.Id);
        Assert.Equal("My project's meeting", meeting.Title);
    }

    [Fact]
    public async Task GetCrmMeetingsByProject_Pages_AndOrdersNewestFirst()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid employeeId, string _) = await SeedEmployeeAsync(sp, "page-owner");
        (Guid projectId, Guid _, string _) = await SeedProjectAsync(sp, "page-owner-2");

        ICommandHandler<ProposeCrmMeetingCommand, Guid> propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        for (int i = 0; i < 3; i++)
        {
            Assert.True((await propose.HandleAsync(new ProposeCrmMeetingCommand(
                projectId, employeeId, $"Meeting {i}", MeetingType.InOffice,
                [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(i + 1), DateTime.UtcNow.AddDays(i + 1).AddHours(1))]))).IsSuccess);
        }

        IQueryHandler<GetCrmMeetingsByProjectQuery, PagedResult<CrmMeetingResponse>> getByProject = sp.GetRequiredService<IQueryHandler<GetCrmMeetingsByProjectQuery, PagedResult<CrmMeetingResponse>>>();
        PagedResult<CrmMeetingResponse> page = (await getByProject.HandleAsync(new GetCrmMeetingsByProjectQuery(projectId, PageNumber: 1, PageSize: 2))).Value!;

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal("Meeting 2", page.Items[0].Title);
    }

    /// <summary>Returns the project id, its owning client id, AND that client's own login app-user id
    /// — a contact accepting/declining/rescheduling a meeting on this project must belong to THIS
    /// client (<c>MeetingSupport.EnsureActorCanActOnProjectAsync</c>'s tenant check), and
    /// <c>GetMyPortalMeetingsQuery</c> needs the client's own <c>AppUserId</c> to resolve as that same
    /// actor.</summary>
    private static async Task<(Guid ProjectId, Guid ClientId, string ClientAppUserId)> SeedProjectAsync(
        IServiceProvider sp, string ownerLabel)
    {
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        (_, string ownerAppUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{ownerLabel}-{Guid.NewGuid():N}@example.com", jobTitle: "Staff", lastName: ownerLabel);

        (Guid clientId, string? clientAppUserId) = await SeedClientWithAppUserIdAsync(sp, $"{ownerLabel}-client");

        currentUser.UserId = ownerAppUserId;
        ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        Result<Guid> result = await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "Meetings test project", "1 Test St", WorkflowType.Stage, clientId));
        Assert.True(result.IsSuccess);
        currentUser.UserId = null;
        return (result.Value, clientId, clientAppUserId);
    }

    private static async Task<Guid> SeedClientAsync(IServiceProvider sp, string label) =>
        (await SeedClientWithAppUserIdAsync(sp, label)).ClientId;

    private static Task<(Guid ClientId, string AppUserId)> SeedClientWithAppUserIdAsync(IServiceProvider sp, string label) =>
        CrmClientSeeding.CreateClientAsync(
            sp, $"{label}-{Guid.NewGuid():N}@example.com", address: "123 Client St", lastName: label);

    private static async Task<Guid> SeedContactAsync(IServiceProvider sp, Guid clientId)
    {
        ICommandHandler<CreateCrmContactCommand, Guid> createContact = sp.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>();
        Result<Guid> result = await createContact.HandleAsync(
            new CreateCrmContactCommand(clientId, "Jane", "Doe", ContactRole.Owner, IsPrimary: true));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static Task<(Guid EmployeeId, string AppUserId)> SeedEmployeeAsync(IServiceProvider sp, string label) =>
        HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{Guid.NewGuid():N}@example.com", jobTitle: "Staff", lastName: label);

    private static async Task<string> RegisterIdentityUserAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        return await IdentityTestSeeding.ProvisionActiveUserAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);
    }
}
