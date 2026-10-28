using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Notes;
using Npgsql;
using SharedKernel;
using Xunit;
using ContactRole = Crm.Contracts.ContactRole;

namespace Crm.Application.Tests;

/// <summary>C-4 (LC-M4, LC-L6): client, contact and note input is bounded like its columns, so an
/// over-long field is a 400 instead of a database error — and a rejected client create leaves no portal
/// login behind.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C4_AnOverLongAddress_Is400_AndCreatesNoLogin()
    {
        string email = $"c4-long-{Guid.NewGuid():N}@example.com";
        string office = CrmTestAccess.OfficeUserId;

        Result created = Plain(await CommandFor<CreateClientCommand, Guid>(office,
            new CreateClientCommand(email, IdentityTestSeeding.Password, "Long", "Address", new string('a', 513)), Office));

        AssertError(created, 400);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM identity.users WHERE "NormalizedEmail" = upper(@email)""";
        command.Parameters.AddWithValue("email", email);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task C4_ClientContactAndNoteFields_AreBoundedLikeTheirColumns()
    {
        ClientFixture c = await SeedAsync("c4-bounds");
        Guid clientId = c.P.ClientId;
        string office = CrmTestAccess.OfficeUserId;
        static string Long(int n) => new('x', n);

        Result[] results =
        [
            await Command(office, new UpdateClientProfileCommand(clientId, office, JobTitle: Long(129)), Office),
            await Command(office, new UpdateClientProfileCommand(clientId, office, MaritalStatus: Long(65)), Office),
            await Command(office, new UpdateClientProfileCommand(clientId, office, RepresentativeName: Long(129)), Office),
            Plain(await CommandFor<CreateClientCommand, Guid>(office, new CreateClientCommand(
                $"c4-notes-{Guid.NewGuid():N}@example.com", IdentityTestSeeding.Password, "Long", "Notes", "1 St", Notes: Long(2001)), Office)),
            Plain(await CommandFor<AddClientChildCommand, Guid>(office, new AddClientChildCommand(clientId, 'X', 5, office), Office)),
            Plain(await CommandFor<AddClientChildCommand, Guid>(office, new AddClientChildCommand(clientId, 'M', 121, office), Office)),
            Plain(await CommandFor<CreateCrmContactCommand, Guid>(office,
                new CreateCrmContactCommand(clientId, Long(129), "Doe", ContactRole.Other, IsPrimary: false, office), Office)),
            Plain(await CommandFor<CreateCrmContactCommand, Guid>(office,
                new CreateCrmContactCommand(clientId, "Jo", "Doe", ContactRole.Other, IsPrimary: false, office, Email: "not-an-email"), Office)),
            Plain(await CommandFor<CreateCrmContactCommand, Guid>(office,
                new CreateCrmContactCommand(clientId, "Jo", "Doe", ContactRole.Other, IsPrimary: false, office, Phone: Long(33)), Office)),
            Plain(await CommandFor<CreateCrmContactCommand, Guid>(office,
                new CreateCrmContactCommand(clientId, "Jo", "Doe", (ContactRole)99, IsPrimary: false, office), Office)),
            await Command(office, new UpdateCrmContactCommand(c.ContactId, "Jane", "Doe", ContactRole.Owner,
                null, null, Long(33), null, office), Office),
            await Command(office, new UpdateCrmContactCommand(c.ContactId, "Jane", "Doe", ContactRole.Owner,
                null, null, null, Long(2001), office), Office),
            Plain(await CommandFor<CreateCrmClientNoteCommand, Guid>(office,
                new CreateCrmClientNoteCommand(clientId, null, Long(4001), c.P.EmployeeId, office), Office)),
            await Command(office, new UpdateCrmClientNoteCommand(c.NoteId, Long(4001), office), Office),
        ];

        Assert.All(results, r => AssertError(r, 400));
    }
}
