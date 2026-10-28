using Crm.Contracts.Leads;
using SharedKernel;
using Xunit;
using LeadSource = Crm.Contracts.LeadSource;

namespace Crm.Application.Tests;

/// <summary>D-9 (C-F1): converting a lead into a client is client-scoped. A caller who can't see the target
/// client gets a 404 — it can neither convert into it, fill its blanks, nor probe whether it exists.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task D9_ConvertingIntoAClientTheCallerCantSee_Is404_AndTheLeadIsUntouched()
    {
        ClientFixture c = await SeedAsync("d9-convert");
        (Guid outsiderId, string outsider) = await HireEmployeeAsync("d9-outsider");
        Result<Guid> lead = await CommandFor<CreateCrmLeadCommand, Guid>(outsider,
            new CreateCrmLeadCommand("Lead", "Person", LeadSource.Website, outsiderId));
        AssertOk(Plain(lead));

        Result converted = Plain(await CommandFor<ConvertCrmLeadCommand, Guid>(outsider,
            new ConvertCrmLeadCommand(lead.Value, c.P.ClientId, outsider)));
        Result unknown = Plain(await CommandFor<ConvertCrmLeadCommand, Guid>(outsider,
            new ConvertCrmLeadCommand(lead.Value, Guid.NewGuid(), outsider)));

        AssertError(converted, 404, "crm.clients.NotFound");
        AssertError(unknown, 404, "crm.clients.NotFound");
        CrmLeadResponse after = (await QueryFor<GetCrmLeadByIdQuery, CrmLeadResponse>(outsider, new GetCrmLeadByIdQuery(lead.Value))).Value!;
        Assert.Null(after.ConvertedClientId);
    }
}
