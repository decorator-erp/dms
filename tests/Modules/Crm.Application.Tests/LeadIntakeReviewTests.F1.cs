using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- F1: a blank email means "no email", not an invalid one ---

    [Fact]
    public async Task Create_with_an_empty_email_saves_and_stores_null()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp);

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>()
            .HandleAsync(new CreateCrmLeadCommand("Blank", "Email", LeadSource.Website, owner, Email: "", Phone: ""));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        CrmLeadResponse lead = await GetLeadAsync(sp, created.Value);
        Assert.Null(lead.Email);
        Assert.Null(lead.Phone);
    }

    [Fact]
    public async Task Update_with_a_whitespace_email_saves_and_stores_null()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp);
        Guid leadId = await CreateLeadAsync(sp, ownerId: owner, email: "had@example.com");

        Result updated = await sp.GetRequiredService<ICommandHandler<UpdateCrmLeadCommand>>()
            .HandleAsync(new UpdateCrmLeadCommand(leadId, "Lead", "Person", LeadSource.Website, owner, "  ", "  ", null));

        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.ToString() : null);
        CrmLeadResponse lead = await GetLeadAsync(sp, leadId);
        Assert.Null(lead.Email);
        Assert.Null(lead.Phone);
    }

    [Fact]
    public async Task Create_with_a_malformed_email_is_still_400()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp);

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>()
            .HandleAsync(new CreateCrmLeadCommand("Bad", "Email", LeadSource.Website, owner, Email: "x@"));

        AssertValidation400(created.Error, "Email");
    }

    private static async Task<CrmLeadResponse> GetLeadAsync(IServiceProvider sp, Guid leadId) =>
        (await sp.GetRequiredService<IQueryHandler<GetCrmLeadByIdQuery, CrmLeadResponse>>()
            .HandleAsync(new GetCrmLeadByIdQuery(leadId))).Value!;
}
