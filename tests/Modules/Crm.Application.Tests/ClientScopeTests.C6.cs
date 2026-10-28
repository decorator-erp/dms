using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using SharedKernel;
using Xunit;
using ContactRole = Crm.Contracts.ContactRole;

namespace Crm.Application.Tests;

/// <summary>C-6 (LC-M6, LC-L4): a client carries an <c>xmin</c> token. Its children are one JSON column on
/// the client row, so two racing writes used to lose one child without a word; now the loser is a 409.
/// Two racing primary-contact changes are a 409 instead of a 500 on the partial unique index.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C6_ParallelAddChild_NeverLosesAChildSilently()
    {
        (Guid clientId, _) = await SeedClientWithoutProjectAsync("c6-children");
        string office = CrmTestAccess.OfficeUserId;
        int succeeded = 0;

        for (int round = 0; round < 5; round++)
        {
            Result[] results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
                Plain(await CommandFor<AddClientChildCommand, Guid>(office, new AddClientChildCommand(clientId, 'M', (short)i, office), Office))));
            Assert.All(results.Where(r => r.IsFailure), r => AssertError(r, 409, "crm.clients.ConcurrentUpdate"));
            succeeded += results.Count(r => r.IsSuccess);
        }

        int saved = (await QueryFor<GetClientByIdQuery, ClientResponse>(office, new GetClientByIdQuery(clientId, office), Office)).Value!.Children.Count;
        Assert.Equal(succeeded, saved);
    }

    [Fact]
    public async Task C6_ParallelPrimaryContactChanges_AreNeverA500_AndLeaveOnePrimary()
    {
        ClientFixture c = await SeedAsync("c6-primary");
        string office = CrmTestAccess.OfficeUserId;
        var others = new Guid[4];
        for (int i = 0; i < others.Length; i++)
        {
            others[i] = (await CommandFor<CreateCrmContactCommand, Guid>(office,
                new CreateCrmContactCommand(c.P.ClientId, $"Other{i}", "Contact", ContactRole.FamilyMember, IsPrimary: false, office), Office)).Value;
        }

        for (int round = 0; round < 5; round++)
        {
            Result[] results = await Task.WhenAll(others.Select(id =>
                Command(office, new SetPrimaryCrmContactCommand(c.P.ClientId, id, office), Office)));
            Assert.All(results.Where(r => r.IsFailure), r => AssertError(r, 409, "crm.contacts.ConcurrentPrimaryChange"));
        }

        IReadOnlyList<CrmContactResponse> contacts = (await QueryFor<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>>(office,
            new GetCrmContactsByClientQuery(c.P.ClientId, office), Office)).Value!;
        Assert.Single(contacts, x => x.IsPrimary);
    }
}
