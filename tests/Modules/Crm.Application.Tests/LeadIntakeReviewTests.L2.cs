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
    // --- L2: "no lead" and "no intake yet" are different answers ---

    [Fact]
    public async Task GetIntake_for_a_lead_without_intake_returns_empty_data()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);

        Result<CrmLeadIntakeResponse> result = await GetIntakeAsync(sp, leadId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Equal(leadId, result.Value!.LeadId);
        Assert.Equal(new CrmLeadIntakeData(), result.Value.Data);
    }

    [Fact]
    public async Task GetIntake_for_a_missing_lead_is_not_found()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        Result<CrmLeadIntakeResponse> result = await GetIntakeAsync(scope.ServiceProvider, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("crm.leads.NotFound", result.Error.Code);
        Assert.Equal(404, result.Error.Status);
    }
}
