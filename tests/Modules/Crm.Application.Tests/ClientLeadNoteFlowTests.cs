using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Leads;
using Crm.Contracts.Notes;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
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
    [Fact]
    public async Task CreateClient_WithUnknownAppUserId_IsRejected()
    {
        await using var scope = fixture.CreateScope();
        var create = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();

        var result = await create.HandleAsync(new CreateClientCommand("no-such-user", "123 Main St"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateClient_AddChild_UpdateProfile_RoundTrips()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var appUserId = await RegisterIdentityUserAsync(sp, "client-owner");

        var create = sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();
        var createResult = await create.HandleAsync(new CreateClientCommand(appUserId, "123 Main St", "some notes"));
        Assert.True(createResult.IsSuccess);
        var clientId = createResult.Value;

        var addChild = sp.GetRequiredService<ICommandHandler<AddClientChildCommand, Guid>>();
        var addChildResult = await addChild.HandleAsync(new AddClientChildCommand(clientId, 'M', 8));
        Assert.True(addChildResult.IsSuccess);

        var updateProfile = sp.GetRequiredService<ICommandHandler<UpdateClientProfileCommand>>();
        Assert.True((await updateProfile.HandleAsync(
            new UpdateClientProfileCommand(clientId, JobTitle: "Architect"))).IsSuccess);

        var getById = sp.GetRequiredService<IQueryHandler<GetClientByIdQuery, ClientResponse>>();
        var response = await getById.HandleAsync(new GetClientByIdQuery(clientId));

        Assert.True(response.IsSuccess);
        Assert.Equal("Architect", response.Value!.JobTitle);
        Assert.Single(response.Value.Children);
    }

    [Fact]
    public async Task LeadLifecycle_CreateChangeStatusConvert_PublishesLeadConverted()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var ownerId = await SeedEmployeeAsync(sp, "lead-owner");
        var clientAppUserId = await RegisterIdentityUserAsync(sp, "convert-target");

        var createClient = sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();
        var clientId = (await createClient.HandleAsync(new CreateClientCommand(clientAppUserId, "999 Oak Ave"))).Value;

        var createLead = sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>();
        var leadResult = await createLead.HandleAsync(
            new CreateCrmLeadCommand("Jane", "Prospect", LeadSource.Website, ownerId));
        Assert.True(leadResult.IsSuccess);
        var leadId = leadResult.Value;

        var changeStatus = sp.GetRequiredService<ICommandHandler<ChangeCrmLeadStatusCommand>>();
        Assert.True((await changeStatus.HandleAsync(new ChangeCrmLeadStatusCommand(leadId, LeadStatus.Qualified))).IsSuccess);

        var convert = sp.GetRequiredService<ICommandHandler<ConvertCrmLeadCommand, Guid>>();
        var convertResult = await convert.HandleAsync(new ConvertCrmLeadCommand(leadId, clientId));
        Assert.True(convertResult.IsSuccess);
        Assert.Equal(clientId, convertResult.Value);

        var getLead = sp.GetRequiredService<IQueryHandler<GetCrmLeadByIdQuery, CrmLeadResponse>>();
        var lead = await getLead.HandleAsync(new GetCrmLeadByIdQuery(leadId));
        Assert.Equal(LeadStatus.Converted, lead.Value!.Status);
        Assert.Equal(clientId, lead.Value.ConvertedClientId);
    }

    [Fact]
    public async Task LeadIntake_SaveThenRead_RoundTrips()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var ownerId = await SeedEmployeeAsync(sp, "intake-owner");
        var createLead = sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>();
        var leadId = (await createLead.HandleAsync(
            new CreateCrmLeadCommand("Sam", "Intake", LeadSource.Referral, ownerId))).Value;

        var saveIntake = sp.GetRequiredService<ICommandHandler<SaveCrmLeadIntakeCommand>>();
        var intakeData = new CrmLeadIntakeData(JobTitle: "Manager", Sector: ProjectSector.Residential);
        Assert.True((await saveIntake.HandleAsync(new SaveCrmLeadIntakeCommand(leadId, intakeData))).IsSuccess);

        var getIntake = sp.GetRequiredService<IQueryHandler<GetCrmLeadIntakeQuery, CrmLeadIntakeResponse>>();
        var intake = await getIntake.HandleAsync(new GetCrmLeadIntakeQuery(leadId));

        Assert.True(intake.IsSuccess);
        Assert.Equal("Manager", intake.Value!.Data.JobTitle);
        Assert.Equal(ProjectSector.Residential, intake.Value.Data.Sector);
    }

    [Fact]
    public async Task ContactLifecycle_CreateTwoContacts_SetPrimary_DemotesThePrevious()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var appUserId = await RegisterIdentityUserAsync(sp, "contact-owner");
        var createClient = sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();
        var clientId = (await createClient.HandleAsync(new CreateClientCommand(appUserId, "1 Elm St"))).Value;

        var createContact = sp.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>();
        var firstResult = await createContact.HandleAsync(
            new CreateCrmContactCommand(clientId, "Alice", "Owner", ContactRole.Owner, IsPrimary: true));
        var secondResult = await createContact.HandleAsync(
            new CreateCrmContactCommand(clientId, "Bob", "Spouse", ContactRole.Spouse, IsPrimary: false));
        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);

        var setPrimary = sp.GetRequiredService<ICommandHandler<SetPrimaryCrmContactCommand>>();
        Assert.True((await setPrimary.HandleAsync(
            new SetPrimaryCrmContactCommand(clientId, secondResult.Value))).IsSuccess);

        var getContacts = sp.GetRequiredService<IQueryHandler<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>>();
        var contacts = (await getContacts.HandleAsync(new GetCrmContactsByClientQuery(clientId))).Value!;

        Assert.True(contacts.Single(c => c.Id == secondResult.Value).IsPrimary);
        Assert.False(contacts.Single(c => c.Id == firstResult.Value).IsPrimary);
    }

    [Fact]
    public async Task ClientNote_CreateUpdateDelete_RoundTrips()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var appUserId = await RegisterIdentityUserAsync(sp, "note-owner");
        var createClient = sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();
        var clientId = (await createClient.HandleAsync(new CreateClientCommand(appUserId, "2 Elm St"))).Value;
        var writtenById = await SeedEmployeeAsync(sp, "note-writer");

        var createNote = sp.GetRequiredService<ICommandHandler<CreateCrmClientNoteCommand, Guid>>();
        var noteResult = await createNote.HandleAsync(
            new CreateCrmClientNoteCommand(clientId, null, "first note", writtenById));
        Assert.True(noteResult.IsSuccess);

        var updateNote = sp.GetRequiredService<ICommandHandler<UpdateCrmClientNoteCommand>>();
        Assert.True((await updateNote.HandleAsync(
            new UpdateCrmClientNoteCommand(noteResult.Value, "revised note"))).IsSuccess);

        var getNotes = sp.GetRequiredService<IQueryHandler<GetCrmClientNotesByClientQuery, IReadOnlyList<CrmClientNoteResponse>>>();
        var notes = (await getNotes.HandleAsync(new GetCrmClientNotesByClientQuery(clientId))).Value!;
        Assert.Equal("revised note", notes.Single().Content);

        var deleteNote = sp.GetRequiredService<ICommandHandler<DeleteCrmClientNoteCommand>>();
        Assert.True((await deleteNote.HandleAsync(new DeleteCrmClientNoteCommand(noteResult.Value))).IsSuccess);

        notes = (await getNotes.HandleAsync(new GetCrmClientNotesByClientQuery(clientId))).Value!;
        Assert.Empty(notes);
    }

    /// <summary>E2-02: seeds a real <c>hr.employees</c> row via the real HR command handler, for the
    /// cross-schema FKs G9-08 added (<c>fk_crm_leads_owner</c>, <c>fk_crm_client_notes_written_by</c>) —
    /// a fabricated Guid no longer survives the insert, same fix shape as <c>Projects.Application.Tests</c>'
    /// own <c>CrmTestSeeding</c> for the <c>ClientId</c> FK ripple.</summary>
    private static async Task<Guid> SeedEmployeeAsync(IServiceProvider sp, string label)
    {
        var appUserId = await RegisterIdentityUserAsync(sp, label);
        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var result = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Staff", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(result.IsSuccess);
        return result.Value;
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
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var result = await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"{label}-{suffix}@example.com", "P@ssw0rd123!", "Test", label, null, "employee")));
        Assert.True(result.IsSuccess);
        return result.Value!;
    }
}
