using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Leads;
using Crm.Contracts.Notes;
using Hr.Contracts.Employees;
using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>
/// G9-09's own verify block, resolving every handler from the real DI graph (proving
/// <c>AddModuleCqrs&lt;CrmDbContext&gt;</c>'s wiring, not just the handler logic in isolation) — the
/// same discipline <c>ProjectFlowTests</c> established. Needs Docker; see the standing environment note
/// in PROGRESS.md from G9-07 onward if this suite could not run.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class ClientLeadNoteFlowTests(CrmApplicationFixture fixture)
{
    /// <summary>
    /// Replaces <c>CreateClient_WithUnknownAppUserId_IsRejected</c>, whose subject is gone: the command
    /// no longer takes an <c>AppUserId</c>, so there is no unknown one to pass. What is worth asserting
    /// in its place is the portal-login gate — the client's account exists but cannot be used until
    /// <c>ClientCreatedIntegrationEvent</c> is delivered.
    /// </summary>
    [Fact]
    public async Task CreateClient_provisions_an_inactive_portal_login_until_the_outbox_delivers()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>()
            .HandleAsync(new CreateClientCommand(
                $"portal-{Guid.NewGuid():N}@example.com", IdentityTestSeeding.Password,
                "Portal", "Client", "123 Main St"));
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);

        ClientSummary client = (await sp.GetRequiredService<IClientDirectory>()
            .GetAsync(created.Value, CancellationToken.None))!;

        IUserDirectory userDirectory = sp.GetRequiredService<IUserDirectory>();

        UserSummary beforeDrain = (await userDirectory.GetAsync(client.AppUserId, CancellationToken.None))!;
        Assert.False(
            beforeDrain.IsActive,
            "A new client's portal login was already active before ClientCreatedIntegrationEvent was "
            + "delivered. CreateClientCommandHandler must not activate inline.");

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        Assert.True(
            (await userDirectory.GetAsync(client.AppUserId, CancellationToken.None))!.IsActive,
            "The client-created event was delivered but the portal login was never activated.");
    }

    [Fact]
    public async Task CreateClient_AddChild_UpdateProfile_RoundTrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
            sp, $"client-owner-{Guid.NewGuid():N}@example.com", address: "123 Main St");

        ICommandHandler<AddClientChildCommand, Guid> addChild = sp.GetRequiredService<ICommandHandler<AddClientChildCommand, Guid>>();
        Result<Guid> addChildResult = await addChild.HandleAsync(new AddClientChildCommand(clientId, 'M', 8, CrmTestAccess.Office(sp)));
        Assert.True(addChildResult.IsSuccess);

        ICommandHandler<UpdateClientProfileCommand> updateProfile = sp.GetRequiredService<ICommandHandler<UpdateClientProfileCommand>>();
        Assert.True((await updateProfile.HandleAsync(
            new UpdateClientProfileCommand(clientId, CrmTestAccess.Office(sp), JobTitle: "Architect"))).IsSuccess);

        IQueryHandler<GetClientByIdQuery, ClientResponse> getById = sp.GetRequiredService<IQueryHandler<GetClientByIdQuery, ClientResponse>>();
        Result<ClientResponse> response = await getById.HandleAsync(new GetClientByIdQuery(clientId, CrmTestAccess.Office(sp)));

        Assert.True(response.IsSuccess);
        Assert.Equal("Architect", response.Value!.JobTitle);
        Assert.Single(response.Value.Children);
    }

    [Fact]
    public async Task LeadLifecycle_CreateChangeStatusConvert_PublishesLeadConverted()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid ownerId = await SeedEmployeeAsync(sp, "lead-owner");

        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
            sp, $"convert-target-{Guid.NewGuid():N}@example.com", address: "999 Oak Ave");

        ICommandHandler<CreateCrmLeadCommand, Guid> createLead = sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>();
        Result<Guid> leadResult = await createLead.HandleAsync(
            new CreateCrmLeadCommand("Jane", "Prospect", LeadSource.Website, ownerId));
        Assert.True(leadResult.IsSuccess);
        Guid leadId = leadResult.Value;

        ICommandHandler<ChangeCrmLeadStatusCommand> changeStatus = sp.GetRequiredService<ICommandHandler<ChangeCrmLeadStatusCommand>>();
        Assert.True((await changeStatus.HandleAsync(new ChangeCrmLeadStatusCommand(leadId, LeadStatus.Qualified))).IsSuccess);

        ICommandHandler<ConvertCrmLeadCommand, Guid> convert = sp.GetRequiredService<ICommandHandler<ConvertCrmLeadCommand, Guid>>();
        Result<Guid> convertResult = await convert.HandleAsync(new ConvertCrmLeadCommand(leadId, clientId));
        Assert.True(convertResult.IsSuccess);
        Assert.Equal(clientId, convertResult.Value);

        IQueryHandler<GetCrmLeadByIdQuery, CrmLeadResponse> getLead = sp.GetRequiredService<IQueryHandler<GetCrmLeadByIdQuery, CrmLeadResponse>>();
        Result<CrmLeadResponse> lead = await getLead.HandleAsync(new GetCrmLeadByIdQuery(leadId));
        Assert.Equal(LeadStatus.Converted, lead.Value!.Status);
        Assert.Equal(clientId, lead.Value.ConvertedClientId);
    }

    [Fact]
    public async Task LeadIntake_SaveThenRead_RoundTrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid ownerId = await SeedEmployeeAsync(sp, "intake-owner");
        ICommandHandler<CreateCrmLeadCommand, Guid> createLead = sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>();
        Guid leadId = (await createLead.HandleAsync(
            new CreateCrmLeadCommand("Sam", "Intake", LeadSource.Referral, ownerId))).Value;

        ICommandHandler<SaveCrmLeadIntakeCommand> saveIntake = sp.GetRequiredService<ICommandHandler<SaveCrmLeadIntakeCommand>>();
        var intakeData = new CrmLeadIntakeData(JobTitle: "Manager", Sector: ProjectSector.Residential);
        Assert.True((await saveIntake.HandleAsync(new SaveCrmLeadIntakeCommand(leadId, intakeData))).IsSuccess);

        IQueryHandler<GetCrmLeadIntakeQuery, CrmLeadIntakeResponse> getIntake = sp.GetRequiredService<IQueryHandler<GetCrmLeadIntakeQuery, CrmLeadIntakeResponse>>();
        Result<CrmLeadIntakeResponse> intake = await getIntake.HandleAsync(new GetCrmLeadIntakeQuery(leadId));

        Assert.True(intake.IsSuccess);
        Assert.Equal("Manager", intake.Value!.Data.JobTitle);
        Assert.Equal(ProjectSector.Residential, intake.Value.Data.Sector);
    }

    [Fact]
    public async Task ContactLifecycle_CreateTwoContacts_SetPrimary_DemotesThePrevious()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
            sp, $"contact-owner-{Guid.NewGuid():N}@example.com", address: "1 Elm St");

        ICommandHandler<CreateCrmContactCommand, Guid> createContact = sp.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>();
        Result<Guid> firstResult = await createContact.HandleAsync(
            new CreateCrmContactCommand(clientId, "Alice", "Owner", ContactRole.Owner, IsPrimary: true, CrmTestAccess.Office(sp)));
        Result<Guid> secondResult = await createContact.HandleAsync(
            new CreateCrmContactCommand(clientId, "Bob", "Spouse", ContactRole.Spouse, IsPrimary: false, CrmTestAccess.Office(sp)));
        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);

        ICommandHandler<SetPrimaryCrmContactCommand> setPrimary = sp.GetRequiredService<ICommandHandler<SetPrimaryCrmContactCommand>>();
        Assert.True((await setPrimary.HandleAsync(
            new SetPrimaryCrmContactCommand(clientId, secondResult.Value, CrmTestAccess.Office(sp)))).IsSuccess);

        IQueryHandler<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>> getContacts = sp.GetRequiredService<IQueryHandler<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>>();
        IReadOnlyList<CrmContactResponse> contacts = (await getContacts.HandleAsync(new GetCrmContactsByClientQuery(clientId, CrmTestAccess.Office(sp)))).Value!;

        Assert.True(contacts.Single(c => c.Id == secondResult.Value).IsPrimary);
        Assert.False(contacts.Single(c => c.Id == firstResult.Value).IsPrimary);
    }

    [Fact]
    public async Task ClientNote_CreateUpdateDelete_RoundTrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid clientId, _) = await CrmClientSeeding.CreateClientAsync(
            sp, $"note-owner-{Guid.NewGuid():N}@example.com", address: "2 Elm St");
        Guid writtenById = await SeedEmployeeAsync(sp, "note-writer");

        ICommandHandler<CreateCrmClientNoteCommand, Guid> createNote = sp.GetRequiredService<ICommandHandler<CreateCrmClientNoteCommand, Guid>>();
        Result<Guid> noteResult = await createNote.HandleAsync(
            new CreateCrmClientNoteCommand(clientId, null, "first note", writtenById, CrmTestAccess.Office(sp)));
        Assert.True(noteResult.IsSuccess);

        ICommandHandler<UpdateCrmClientNoteCommand> updateNote = sp.GetRequiredService<ICommandHandler<UpdateCrmClientNoteCommand>>();
        Assert.True((await updateNote.HandleAsync(
            new UpdateCrmClientNoteCommand(noteResult.Value, "revised note", writtenById, CrmTestAccess.Office(sp)))).IsSuccess);

        IQueryHandler<GetCrmClientNotesByClientQuery, IReadOnlyList<CrmClientNoteResponse>> getNotes = sp.GetRequiredService<IQueryHandler<GetCrmClientNotesByClientQuery, IReadOnlyList<CrmClientNoteResponse>>>();
        IReadOnlyList<CrmClientNoteResponse> notes = (await getNotes.HandleAsync(new GetCrmClientNotesByClientQuery(clientId, CrmTestAccess.Office(sp)))).Value!;
        Assert.Equal("revised note", notes.Single().Content);

        ICommandHandler<DeleteCrmClientNoteCommand> deleteNote = sp.GetRequiredService<ICommandHandler<DeleteCrmClientNoteCommand>>();
        Assert.True((await deleteNote.HandleAsync(new DeleteCrmClientNoteCommand(noteResult.Value, writtenById, CrmTestAccess.Office(sp)))).IsSuccess);

        notes = (await getNotes.HandleAsync(new GetCrmClientNotesByClientQuery(clientId, CrmTestAccess.Office(sp)))).Value!;
        Assert.Empty(notes);
    }

    /// <summary>E2-02: seeds a real <c>hr.employees</c> row via the real HR command handler, for the
    /// cross-schema FKs G9-08 added (<c>fk_crm_leads_owner</c>, <c>fk_crm_client_notes_written_by</c>) —
    /// a fabricated Guid no longer survives the insert, same fix shape as <c>Projects.Application.Tests</c>'
    /// own <c>CrmTestSeeding</c> for the <c>ClientId</c> FK ripple.</summary>
    private static async Task<Guid> SeedEmployeeAsync(IServiceProvider sp, string label)
    {
        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{Guid.NewGuid():N}@example.com", jobTitle: "Staff", lastName: label);

        return employeeId;
    }

    /// <summary>
    /// Registers a real Identity user for <c>crm.clients.AppUserId</c>'s FK to reference — role name is
    /// "employee" (the only Identity role guaranteed seeded; see <c>DefaultRoles</c>'s own remarks: the
    /// backend's <c>Client</c> role was deliberately dropped from the seeded set until a module that
    /// needs it existed). This is a test-fixture concern only — role membership carries no meaning for
    /// <see cref="Crm.Contracts.ICrmAccessGuard"/>, which resolves the client actor kind purely from
    /// <c>crm.clients.AppUserId</c> matching, never from an Identity role claim. Whether the Portal
    /// (G9-14) needs Identity's <c>DefaultRoles</c> to actually gain a "client" role for real
    /// permission-claim issuance is a separate, still-open question for that task.
    /// </summary>
    private static async Task<string> RegisterIdentityUserAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        return await IdentityTestSeeding.ProvisionActiveUserAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);
    }
}
