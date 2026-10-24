using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- M3: bad input is a 400 from the pipeline, never a 500 from Postgres ---

    [Fact]
    public async Task Create_with_an_over_long_name_is_400_not_500()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp);

        Result<Guid> result = await sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>()
            .HandleAsync(new CreateCrmLeadCommand(new string('a', 129), "B", LeadSource.Website, owner));

        AssertValidation400(result.Error, "FirstName");
    }

    [Fact]
    public async Task Create_with_an_undefined_source_or_bad_email_is_400()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp);
        ICommandHandler<CreateCrmLeadCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>();

        AssertValidation400((await create.HandleAsync(
            new CreateCrmLeadCommand("A", "B", (LeadSource)99, owner))).Error, "Source");
        AssertValidation400((await create.HandleAsync(
            new CreateCrmLeadCommand("A", "B", LeadSource.Website, owner, Email: "not-an-email"))).Error, "Email");
    }

    [Fact]
    public async Task Update_with_an_over_long_phone_or_undefined_source_is_400()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp);
        Guid leadId = await CreateLeadAsync(sp, ownerId: owner);
        ICommandHandler<UpdateCrmLeadCommand> update = sp.GetRequiredService<ICommandHandler<UpdateCrmLeadCommand>>();

        AssertValidation400((await update.HandleAsync(new UpdateCrmLeadCommand(
            leadId, "A", "B", LeadSource.Website, owner, null, new string('1', 33), null))).Error, "Phone");
        AssertValidation400((await update.HandleAsync(new UpdateCrmLeadCommand(
            leadId, "A", "B", (LeadSource)99, owner, null, null, null))).Error, "Source");
    }

    [Fact]
    public async Task ChangeStatus_to_an_undefined_status_or_to_Converted_is_400()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);
        ICommandHandler<ChangeCrmLeadStatusCommand> change = sp.GetRequiredService<ICommandHandler<ChangeCrmLeadStatusCommand>>();

        AssertValidation400((await change.HandleAsync(new ChangeCrmLeadStatusCommand(leadId, (LeadStatus)99))).Error, "Status");
        AssertValidation400((await change.HandleAsync(new ChangeCrmLeadStatusCommand(leadId, LeadStatus.Converted))).Error, "Status");
    }

    [Fact]
    public async Task Intake_with_an_over_long_field_an_undefined_enum_or_a_zero_area_is_400()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);

        AssertValidation400((await SaveIntakeAsync(sp, leadId,
            new CrmLeadIntakeData(ClientAddress: new string('a', 513)))).Error, "Data.ClientAddress");
        AssertValidation400((await SaveIntakeAsync(sp, leadId,
            new CrmLeadIntakeData(Sector: (ProjectSector)99))).Error, "Data.Sector");
        AssertValidation400((await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            Sector: ProjectSector.Residential,
            Residential: new CrmLeadIntakeResidentialData(UnitType: (UnitType)99)))).Error, "Data.Residential.UnitType");
        AssertValidation400((await SaveIntakeAsync(sp, leadId,
            new CrmLeadIntakeData(AreaSqm: 0m))).Error, "Data.AreaSqm");
        Assert.False(await IntakeExistsAsync(leadId));
    }

    [Fact]
    public async Task Create_and_Update_with_an_unknown_owner_are_OwnerNotFound()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>()
            .HandleAsync(new CreateCrmLeadCommand("A", "B", LeadSource.Website, Guid.NewGuid()));
        AssertOwnerNotFound(created.Error);

        Guid leadId = await CreateLeadAsync(sp);
        Result updated = await sp.GetRequiredService<ICommandHandler<UpdateCrmLeadCommand>>()
            .HandleAsync(new UpdateCrmLeadCommand(leadId, "A", "B", LeadSource.Website, Guid.NewGuid(), null, null, null));
        AssertOwnerNotFound(updated.Error);
    }

    [Fact]
    public async Task An_inactive_owner_is_refused_on_create_and_on_reassignment_but_keeps_existing_leads_editable()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leaving = await SeedEmployeeAsync(sp, "leaving-owner");
        Guid leadId = await CreateLeadAsync(sp, ownerId: leaving);
        Guid otherLead = await CreateLeadAsync(sp);
        Result deactivated = await sp.GetRequiredService<ICommandHandler<DeactivateEmployeeCommand>>()
            .HandleAsync(new DeactivateEmployeeCommand(leaving));
        Assert.True(deactivated.IsSuccess, deactivated.IsFailure ? deactivated.Error.ToString() : null);

        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>()
            .HandleAsync(new CreateCrmLeadCommand("A", "B", LeadSource.Website, leaving));
        AssertOwnerNotFound(created.Error);

        ICommandHandler<UpdateCrmLeadCommand> update = sp.GetRequiredService<ICommandHandler<UpdateCrmLeadCommand>>();
        AssertOwnerNotFound((await update.HandleAsync(new UpdateCrmLeadCommand(
            otherLead, "A", "B", LeadSource.Website, leaving, null, null, null))).Error);

        // Unchanged owner: the lead stays editable after its owner leaves.
        Result kept = await update.HandleAsync(new UpdateCrmLeadCommand(
            leadId, "Still", "Editable", LeadSource.Website, leaving, null, null, null));
        Assert.True(kept.IsSuccess, kept.IsFailure ? kept.Error.ToString() : null);
    }

    private static void AssertValidation400(Error error, string property)
    {
        Assert.Equal(400, error.Status);
        ValidationError validation = Assert.IsType<ValidationError>(error);
        Assert.Contains(validation.Errors, e => e.Code == property);
    }

    private static void AssertOwnerNotFound(Error error)
    {
        Assert.Equal("crm.leads.OwnerNotFound", error.Code);
        Assert.Equal(400, error.Status);
    }
}
