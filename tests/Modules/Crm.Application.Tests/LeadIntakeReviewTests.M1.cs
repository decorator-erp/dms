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
    // --- M1: the intake rules hold on the first save, not only on later edits ---

    [Fact]
    public async Task FirstIntakeSave_with_experience_but_no_prior_designer_is_rejected_and_writes_nothing()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);

        Result result = await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            UsedDesignerBefore: false, DesignerExperience: PastExperience.Good));

        Assert.True(result.IsFailure);
        Assert.Equal("crm.leads.DesignerExperienceWithoutUse", result.Error.Code);
        Assert.Equal(400, result.Error.Status);
        Assert.False(await IntakeExistsAsync(leadId));
    }
}
