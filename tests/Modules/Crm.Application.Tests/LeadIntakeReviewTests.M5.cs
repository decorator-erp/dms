using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- M5: no conversion into a soft-deleted client ---

    [Fact]
    public async Task Convert_into_a_deactivated_client_is_refused_without_publishing()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);
        Guid clientId = await CreateClientAsync(sp);
        Result deleted = await sp.GetRequiredService<ICommandHandler<Crm.Contracts.Clients.DeleteClientCommand>>()
            .HandleAsync(new Crm.Contracts.Clients.DeleteClientCommand(clientId, CrmTestAccess.Office(sp)));
        Assert.True(deleted.IsSuccess, deleted.IsFailure ? deleted.Error.ToString() : null);

        Result<Guid> result = await ConvertAsync(sp, leadId, clientId);

        Assert.True(result.IsFailure);
        Assert.Equal("crm.clients.NotFound", result.Error.Code);
        CrmLeadResponse lead = (await sp.GetRequiredService<IQueryHandler<GetCrmLeadByIdQuery, CrmLeadResponse>>()
            .HandleAsync(new GetCrmLeadByIdQuery(leadId))).Value!;
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Null(lead.ConvertedClientId);
        Assert.Equal(0, await CountLeadConvertedEventsAsync(leadId));
    }
}
