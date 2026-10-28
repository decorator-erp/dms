using Crm.Contracts.Clients;
using Crm.Contracts.Notes;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / C-F4: a deactivated client's notes are frozen too — update and delete answer 409
/// <c>crm.clients.Inactive</c>, like every other write on the client (C-5).</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task D16_C4_NoteUpdateAndDelete_OnAnInactiveClient_Are409()
    {
        ClientFixture c = await SeedAsync("d16-c4");
        string office = CrmTestAccess.OfficeUserId;
        AssertOk(await Command(office, new SetClientActiveCommand(c.P.ClientId, false, office), Office));

        Result[] results =
        [
            await Command(office, new UpdateCrmClientNoteCommand(c.NoteId, "Edited after deactivation", c.P.EmployeeId, office), Office),
            await Command(office, new DeleteCrmClientNoteCommand(c.NoteId, c.P.EmployeeId, office), Office),
        ];

        Assert.All(results, r => AssertError(r, 409, "crm.clients.Inactive"));

        // FINDING-P1-6: authorship (403) is checked before the client's state (409).
        (Guid colleagueId, string colleague) = await HireEmployeeAsync("d16-c4-colleague");
        AssertError(await Command(colleague, new UpdateCrmClientNoteCommand(c.NoteId, "Not mine", colleagueId, colleague), Office),
            403, "crm.notes.NotAuthor");
        AssertError(await Command(colleague, new DeleteCrmClientNoteCommand(c.NoteId, colleagueId, colleague), Office),
            403, "crm.notes.NotAuthor");
    }
}
