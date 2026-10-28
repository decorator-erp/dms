using Crm.Contracts.Contacts;
using Crm.Contracts.Notes;
using SharedKernel;
using Xunit;
using ContactRole = Crm.Contracts.ContactRole;

namespace Crm.Application.Tests;

/// <summary>C-7 (LC-L2, LC-L3): a contact is changed only through its own client's route, and a note can
/// point only at one of its client's projects.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C7_UpdatingAContactThroughAnotherClientsRoute_Is404()
    {
        ClientFixture a = await SeedAsync("c7-a");
        ClientFixture b = await SeedAsync("c7-b");
        string office = CrmTestAccess.OfficeUserId;

        Result result = await Command(office, new UpdateCrmContactCommand(
            b.P.ClientId, a.ContactId, "Jane", "Hijacked", ContactRole.Owner, null, null, null, null, office), Office);

        AssertError(result, 404, "crm.contacts.NotFound");
        CrmContactResponse contact = Assert.Single((await QueryFor<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>(office,
            new GetCrmContactsByClientQuery(a.P.ClientId, office), Office)).Value!);
        Assert.Equal("Client", contact.LastName);
    }

    [Fact]
    public async Task C7_ANoteOnAProjectOfAnotherClient_OrAnUnknownProject_Is400()
    {
        ClientFixture a = await SeedAsync("c7-note-a");
        ClientFixture b = await SeedAsync("c7-note-b");
        string office = CrmTestAccess.OfficeUserId;

        Result foreign = Plain(await CommandFor<CreateCrmClientNoteCommand, Guid>(office,
            new CreateCrmClientNoteCommand(a.P.ClientId, b.P.ProjectId, "Wrong project", a.P.EmployeeId, office), Office));
        Result unknown = Plain(await CommandFor<CreateCrmClientNoteCommand, Guid>(office,
            new CreateCrmClientNoteCommand(a.P.ClientId, Guid.NewGuid(), "No project", a.P.EmployeeId, office), Office));
        Result own = Plain(await CommandFor<CreateCrmClientNoteCommand, Guid>(office,
            new CreateCrmClientNoteCommand(a.P.ClientId, a.P.ProjectId, "Right project", a.P.EmployeeId, office), Office));

        AssertError(foreign, 400, "crm.notes.ProjectMismatch");
        AssertError(unknown, 400, "crm.notes.ProjectMismatch");
        AssertOk(own);
    }
}
