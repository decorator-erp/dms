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
    // --- M2: the intake is frozen once the lead is converted ---

    [Fact]
    public async Task IntakeSave_on_a_converted_lead_is_refused_and_leaves_the_intake_unchanged()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);
        Assert.True((await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(JobTitle: "Before"))).IsSuccess);
        Guid clientId = await CreateClientAsync(sp);
        Assert.True((await ConvertAsync(sp, leadId, clientId)).IsSuccess);

        Result result = await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(JobTitle: "After"));

        Assert.True(result.IsFailure);
        Assert.Equal("crm.leads.AlreadyConverted", result.Error.Code);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal("Before", (await GetIntakeAsync(sp, leadId)).Value!.Data.JobTitle);
    }
}
