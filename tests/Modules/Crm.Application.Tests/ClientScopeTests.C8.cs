using Crm.Contracts;
using Crm.Contracts.Notes;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>C-8 (LC-I1): a note is changed or deleted only by its author, or by a holder of
/// <c>crm.notes.manage_all</c>. Everyone else who can see the client gets 403 <c>crm.notes.NotAuthor</c>.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C8_OnlyTheAuthor_OrManageAll_ChangesANote()
    {
        ClientFixture c = await SeedAsync("c8-notes");
        (Guid colleagueId, string colleague) = await HireEmployeeAsync("c8-colleague");
        string author = c.P.OwnerAppUserId;

        AssertError(await Command(colleague, new UpdateCrmClientNoteCommand(c.NoteId, "Not mine", colleagueId, colleague), Office),
            403, "crm.notes.NotAuthor");
        AssertError(await Command(colleague, new DeleteCrmClientNoteCommand(c.NoteId, colleagueId, colleague), Office),
            403, "crm.notes.NotAuthor");

        AssertOk(await Command(author, new UpdateCrmClientNoteCommand(c.NoteId, "Author's edit", c.P.EmployeeId, author), Office));
        AssertOk(await Command(colleague, new UpdateCrmClientNoteCommand(c.NoteId, "Manager's edit", colleagueId, colleague),
            CrmPermissions.Clients.ReadAll, CrmPermissions.Notes.ManageAll));

        CrmClientNoteResponse note = Assert.Single((await QueryFor<GetCrmClientNotesByClientQuery, IReadOnlyList<CrmClientNoteResponse>>(
            author, new GetCrmClientNotesByClientQuery(c.P.ClientId, author), Office)).Value!);
        Assert.Equal("Manager's edit", note.Content);
        Assert.Equal(c.P.EmployeeId, note.WrittenById);

        AssertOk(await Command(colleague, new DeleteCrmClientNoteCommand(c.NoteId, colleagueId, colleague),
            CrmPermissions.Clients.ReadAll, CrmPermissions.Notes.ManageAll));
    }
}
