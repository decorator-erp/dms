using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Meetings;
using Crm.Contracts.Portal;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (employeeId, _) = await SeedEmployeeAsync(sp, "propose-owner");
        var (projectId, clientId, _) = await SeedProjectAsync(sp, "propose-owner-2");
        var contactId = await SeedContactAsync(sp, clientId);

        var propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        var proposeResult = await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "Kickoff", MeetingType.InOffice,
            [
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)),
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1)),
            ]));
        Assert.True(proposeResult.IsSuccess);
        var meetingId = proposeResult.Value;

        var getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        var slots = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(2, slots.Count);
        var firstSlotId = slots[0].Id;
        var secondSlotId = slots[1].Id;

        // Accept is the CONTACT's action - the employee proposed, so the contact is the counterparty.
        // The contact belongs to the SAME client that owns projectId - the tenant-scope check requires it.
        var accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        var acceptResult = await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(firstSlotId, null, contactId));
        Assert.True(acceptResult.IsSuccess);

        var getById = sp.GetRequiredService<IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>>();
        var meeting = (await getById.HandleAsync(new GetCrmMeetingByIdQuery(meetingId))).Value!;
        Assert.Equal(MeetingStatus.Scheduled, meeting.Status);
        Assert.Equal(slots[0].StartsAtUtc, meeting.ScheduledAtUtc);

        var afterAccept = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(MeetingSlotStatus.Accepted, afterAccept.Single(s => s.Id == firstSlotId).Status);
        Assert.Equal(MeetingSlotStatus.Declined, afterAccept.Single(s => s.Id == secondSlotId).Status);

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RequestByClient_ThenEmployeeDeclinesWithCounterSlot_AddsTheCounterAsProposed()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, clientId, _) = await SeedProjectAsync(sp, "request-owner");
        var contactId = await SeedContactAsync(sp, clientId);

        var request = sp.GetRequiredService<ICommandHandler<RequestCrmMeetingByClientCommand, Guid>>();
        var requestResult = await request.HandleAsync(new RequestCrmMeetingByClientCommand(
            projectId, contactId, "Site visit", MeetingType.OnSite,
            DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)));
        Assert.True(requestResult.IsSuccess);
        var meetingId = requestResult.Value;

        var getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        var originalSlotId = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;

        var (employeeId, _) = await SeedEmployeeAsync(sp, "request-decliner");
        var decline = sp.GetRequiredService<ICommandHandler<DeclineCrmMeetingSlotCommand>>();
        var declineResult = await decline.HandleAsync(new DeclineCrmMeetingSlotCommand(
            originalSlotId, employeeId, null,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(3).AddHours(1))]));
        Assert.True(declineResult.IsSuccess);

        var slots = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(2, slots.Count);
        Assert.Equal(MeetingSlotStatus.Declined, slots.Single(s => s.Id == originalSlotId).Status);
        var counter = slots.Single(s => s.Id != originalSlotId);
        Assert.Equal(MeetingSlotStatus.Proposed, counter.Status);
        Assert.Equal(employeeId, counter.ProposedByEmployeeId);
    }

    [Fact]
    public async Task Reschedule_DemotesTheAcceptedSlot_ThenTheNewSlotCanBeAccepted()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (employeeId, _) = await SeedEmployeeAsync(sp, "reschedule-owner");
        var (projectId, clientId, _) = await SeedProjectAsync(sp, "reschedule-owner-2");
        var contactId = await SeedContactAsync(sp, clientId);

        var propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        var meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "First pass", MeetingType.Online,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).Value;

        var getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        var firstSlotId = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;

        var accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        Assert.True((await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(firstSlotId, null, contactId))).IsSuccess);

        var reschedule = sp.GetRequiredService<ICommandHandler<RescheduleCrmMeetingCommand>>();
        var newStart = DateTime.UtcNow.AddDays(5);
        var rescheduleResult = await reschedule.HandleAsync(new RescheduleCrmMeetingCommand(
            meetingId, employeeId, null, newStart, newStart.AddHours(1)));
        Assert.True(rescheduleResult.IsSuccess);

        var getById = sp.GetRequiredService<IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>>();
        var meeting = (await getById.HandleAsync(new GetCrmMeetingByIdQuery(meetingId))).Value!;
        Assert.Equal(MeetingStatus.Rescheduled, meeting.Status);
        Assert.Null(meeting.ScheduledAtUtc);

        var slotsAfter = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
        Assert.Equal(MeetingSlotStatus.Declined, slotsAfter.Single(s => s.Id == firstSlotId).Status);
        var newSlot = slotsAfter.Single(s => s.Id != firstSlotId);
        Assert.Equal(MeetingSlotStatus.Proposed, newSlot.Status);

        // Accepting the new slot must succeed - proves Demote() actually cleared the unique-accepted-slot index.
        Assert.True((await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(newSlot.Id, null, contactId))).IsSuccess);
    }

    [Fact]
    public async Task Complete_BeforeTheMeetingStarts_FailsWithInvalidStateTransition()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (employeeId, _) = await SeedEmployeeAsync(sp, "complete-owner");
        var (projectId, clientId, _) = await SeedProjectAsync(sp, "complete-owner-2");
        var contactId = await SeedContactAsync(sp, clientId);

        var propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        var meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "Future meeting", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(10).AddHours(1))]))).Value;
        var slotId = (await sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>()
            .HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;
        var accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        Assert.True((await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(slotId, null, contactId))).IsSuccess);

        var complete = sp.GetRequiredService<ICommandHandler<CompleteCrmMeetingCommand>>();
        var result = await complete.HandleAsync(new CompleteCrmMeetingCommand(meetingId));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Cancel_DeclinesEveryRemainingProposedSlot()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (employeeId, _) = await SeedEmployeeAsync(sp, "cancel-owner");
        var (projectId, _, _) = await SeedProjectAsync(sp, "cancel-owner-2");

        var propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        var meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "To be canceled", MeetingType.InOffice,
            [
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)),
                new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1)),
            ]))).Value;

        var cancel = sp.GetRequiredService<ICommandHandler<CancelCrmMeetingCommand>>();
        var cancelResult = await cancel.HandleAsync(new CancelCrmMeetingCommand(meetingId, employeeId, null, "client asked"));
        Assert.True(cancelResult.IsSuccess);

        var getById = sp.GetRequiredService<IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>>();
        var meeting = (await getById.HandleAsync(new GetCrmMeetingByIdQuery(meetingId))).Value!;
        Assert.Equal(MeetingStatus.Canceled, meeting.Status);
        Assert.Equal("client asked", meeting.CancelReason);

        var getSlots = sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>();
        var slots = (await getSlots.HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!;
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (employeeId, _) = await SeedEmployeeAsync(sp, "tenant-owner");
        var (projectId, _, _) = await SeedProjectAsync(sp, "tenant-owner-2");

        var propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        var meetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            projectId, employeeId, "Tenant test", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).Value;
        var slotId = (await sp.GetRequiredService<IQueryHandler<GetCrmMeetingSlotsByMeetingQuery, IReadOnlyList<CrmMeetingSlotResponse>>>()
            .HandleAsync(new GetCrmMeetingSlotsByMeetingQuery(meetingId))).Value!.Single().Id;

        // A different client's contact - never associated with projectId's owning client.
        var strangerClientId = await SeedClientAsync(sp, "tenant-stranger");
        var strangerContactId = await SeedContactAsync(sp, strangerClientId);

        var accept = sp.GetRequiredService<ICommandHandler<AcceptCrmMeetingSlotCommand>>();
        var result = await accept.HandleAsync(new AcceptCrmMeetingSlotCommand(slotId, null, strangerContactId));

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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (employeeId, _) = await SeedEmployeeAsync(sp, "portal-owner");
        var (ownProjectId, _, clientAppUserId) = await SeedProjectAsync(sp, "portal-owner-2");
        var (otherProjectId, _, _) = await SeedProjectAsync(sp, "portal-other-owner");

        var propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        var ownMeetingId = (await propose.HandleAsync(new ProposeCrmMeetingCommand(
            ownProjectId, employeeId, "My project's meeting", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).Value;
        Assert.True((await propose.HandleAsync(new ProposeCrmMeetingCommand(
            otherProjectId, employeeId, "Someone else's meeting", MeetingType.InOffice,
            [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1))]))).IsSuccess);

        var getPortalMeetings = sp.GetRequiredService<IQueryHandler<GetMyPortalMeetingsQuery, IReadOnlyList<PortalMeetingResponse>>>();
        var result = await getPortalMeetings.HandleAsync(new GetMyPortalMeetingsQuery(clientAppUserId));

        Assert.True(result.IsSuccess);
        var meeting = Assert.Single(result.Value!);
        Assert.Equal(ownMeetingId, meeting.Id);
        Assert.Equal("My project's meeting", meeting.Title);
    }

    [Fact]
    public async Task GetCrmMeetingsByProject_Pages_AndOrdersNewestFirst()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (employeeId, _) = await SeedEmployeeAsync(sp, "page-owner");
        var (projectId, _, _) = await SeedProjectAsync(sp, "page-owner-2");

        var propose = sp.GetRequiredService<ICommandHandler<ProposeCrmMeetingCommand, Guid>>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await propose.HandleAsync(new ProposeCrmMeetingCommand(
                projectId, employeeId, $"Meeting {i}", MeetingType.InOffice,
                [new CrmMeetingSlotRequest(DateTime.UtcNow.AddDays(i + 1), DateTime.UtcNow.AddDays(i + 1).AddHours(1))]))).IsSuccess);
        }

        var getByProject = sp.GetRequiredService<IQueryHandler<GetCrmMeetingsByProjectQuery, PagedResult<CrmMeetingResponse>>>();
        var page = (await getByProject.HandleAsync(new GetCrmMeetingsByProjectQuery(projectId, PageNumber: 1, PageSize: 2))).Value!;

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
        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        var ownerAppUserId = await RegisterIdentityUserAsync(sp, ownerLabel);
        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        Assert.True((await createEmployee.HandleAsync(
            new CreateEmployeeCommand(ownerAppUserId, "Staff", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"))).IsSuccess);

        var (clientId, clientAppUserId) = await SeedClientWithAppUserIdAsync(sp, $"{ownerLabel}-client");

        currentUser.UserId = ownerAppUserId;
        var createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        var result = await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "Meetings test project", "1 Test St", WorkflowType.Stage, clientId));
        Assert.True(result.IsSuccess);
        currentUser.UserId = null;
        return (result.Value, clientId, clientAppUserId);
    }

    private static async Task<Guid> SeedClientAsync(IServiceProvider sp, string label) =>
        (await SeedClientWithAppUserIdAsync(sp, label)).ClientId;

    private static async Task<(Guid ClientId, string AppUserId)> SeedClientWithAppUserIdAsync(IServiceProvider sp, string label)
    {
        var appUserId = await RegisterIdentityUserAsync(sp, label);
        var createClient = sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();
        var result = await createClient.HandleAsync(new CreateClientCommand(appUserId, "123 Client St"));
        Assert.True(result.IsSuccess);
        return (result.Value, appUserId);
    }

    private static async Task<Guid> SeedContactAsync(IServiceProvider sp, Guid clientId)
    {
        var createContact = sp.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>();
        var result = await createContact.HandleAsync(
            new CreateCrmContactCommand(clientId, "Jane", "Doe", ContactRole.Owner, IsPrimary: true));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task<(Guid EmployeeId, string AppUserId)> SeedEmployeeAsync(IServiceProvider sp, string label)
    {
        var appUserId = await RegisterIdentityUserAsync(sp, label);
        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var result = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Staff", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(result.IsSuccess);
        return (result.Value, appUserId);
    }

    private static async Task<string> RegisterIdentityUserAsync(IServiceProvider sp, string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var result = await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"{label}-{suffix}@example.com", "P@ssw0rd123!", "Test", label, null, "employee")));
        Assert.True(result.IsSuccess);
        return result.Value!;
    }
}
