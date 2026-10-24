using Crm.Application.Features.Leads;
using Crm.Contracts;
using Crm.Contracts.Leads;
using FluentValidation;
using FluentValidation.Results;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>M3's field rules, checked on the validators alone — no fixture, no Docker. The lengths are the
/// EF configurations' column lengths, so each of these used to reach Postgres as a 22001 (500).</summary>
public sealed class LeadValidatorTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly string Long129 = new('a', 129);

    private static CreateCrmLeadCommand Create(
        string first = "A", string last = "B", LeadSource source = LeadSource.Website,
        string? email = null, string? phone = null, string? notes = null) =>
        new(first, last, source, Owner, email, phone, notes);

    private static UpdateCrmLeadCommand Update(
        string first = "A", string last = "B", LeadSource source = LeadSource.Website,
        string? email = null, string? phone = null, string? notes = null) =>
        new(Guid.NewGuid(), first, last, source, Owner, email, phone, notes);

    public static TheoryData<string, CreateCrmLeadCommand> InvalidCreates => new()
    {
        { "FirstName", Create(first: Long129) },
        { "LastName", Create(last: Long129) },
        { "Email", Create(email: new string('a', 251) + "@x.com") },
        { "Email", Create(email: "not-an-email") },
        { "Phone", Create(phone: new string('1', 33)) },
        { "Notes", Create(notes: new string('a', 2001)) },
        { "Source", Create(source: (LeadSource)99) },
    };

    public static TheoryData<string, UpdateCrmLeadCommand> InvalidUpdates => new()
    {
        { "FirstName", Update(first: Long129) },
        { "LastName", Update(last: Long129) },
        { "Email", Update(email: "not-an-email") },
        { "Phone", Update(phone: new string('1', 33)) },
        { "Notes", Update(notes: new string('a', 2001)) },
        { "Source", Update(source: (LeadSource)99) },
    };

    public static TheoryData<string, CrmLeadIntakeData> InvalidIntakes => new()
    {
        { "Data.JobTitle", new CrmLeadIntakeData(JobTitle: Long129) },
        { "Data.ClientAddress", new CrmLeadIntakeData(ClientAddress: new string('a', 513)) },
        { "Data.UnitAddress", new CrmLeadIntakeData(UnitAddress: new string('a', 513)) },
        { "Data.DesignerExperienceReason", new CrmLeadIntakeData(DesignerExperienceReason: new string('a', 1001)) },
        { "Data.AreaSqm", new CrmLeadIntakeData(AreaSqm: 0m) },
        { "Data.AreaSqm", new CrmLeadIntakeData(AreaSqm: -5m) },
        { "Data.Sector", new CrmLeadIntakeData(Sector: (ProjectSector)99) },
        { "Data.DesignerExperience", new CrmLeadIntakeData(DesignerExperience: (PastExperience)99) },
        { "Data.Residential.UnitType", Residential(new CrmLeadIntakeResidentialData(UnitType: (UnitType)99)) },
        { "Data.Residential.UnitCondition", Residential(new CrmLeadIntakeResidentialData(UnitCondition: (UnitCondition)99)) },
        { "Data.Residential.FinishingPurpose", Residential(new CrmLeadIntakeResidentialData(FinishingPurpose: (FinishingPurpose)99)) },
    };

    [Theory]
    [MemberData(nameof(InvalidCreates))]
    public void Create_rejects(string property, CreateCrmLeadCommand command) =>
        AssertInvalid(new CreateCrmLeadCommandValidator(), command, property);

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public void Update_rejects(string property, UpdateCrmLeadCommand command) =>
        AssertInvalid(new UpdateCrmLeadCommandValidator(), command, property);

    [Theory]
    [MemberData(nameof(InvalidIntakes))]
    public void Intake_rejects(string property, CrmLeadIntakeData data) =>
        AssertInvalid(new SaveCrmLeadIntakeCommandValidator(), new SaveCrmLeadIntakeCommand(Guid.NewGuid(), data), property);

    [Theory]
    [InlineData((LeadStatus)99)]
    [InlineData(LeadStatus.Converted)]
    public void ChangeStatus_rejects_undefined_and_converted(LeadStatus status) =>
        AssertInvalid(new ChangeCrmLeadStatusCommandValidator(), new ChangeCrmLeadStatusCommand(Guid.NewGuid(), status), "Status");

    [Fact]
    public void Values_at_the_column_limits_are_accepted()
    {
        Assert.True(new CreateCrmLeadCommandValidator().Validate(Create(
            first: new string('a', 128), last: new string('a', 128), email: "a@example.com",
            phone: new string('1', 32), notes: new string('a', 2000))).IsValid);
        Assert.True(new SaveCrmLeadIntakeCommandValidator().Validate(new SaveCrmLeadIntakeCommand(Guid.NewGuid(),
            new CrmLeadIntakeData(JobTitle: new string('a', 128), AreaSqm: 0.5m,
                Sector: ProjectSector.Residential, Residential: new CrmLeadIntakeResidentialData(UnitType.Villa)))).IsValid);
    }

    // F1: a blank email is "none", not an invalid address.

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_emails_are_accepted_on_create_and_update(string blank)
    {
        Assert.True(new CreateCrmLeadCommandValidator().Validate(Create(email: blank)).IsValid);
        Assert.True(new UpdateCrmLeadCommandValidator().Validate(Update(email: blank)).IsValid);
    }

    [Theory]
    [InlineData("x@")]
    [InlineData("no-at-sign")]
    public void Malformed_emails_are_still_rejected(string email)
    {
        AssertInvalid(new CreateCrmLeadCommandValidator(), Create(email: email), "Email");
        AssertInvalid(new UpdateCrmLeadCommandValidator(), Update(email: email), "Email");
    }

    [Fact]
    public void A_257_character_email_is_still_rejected()
    {
        string email = new string('a', 251) + "@x.com";
        Assert.Equal(257, email.Length);

        AssertInvalid(new CreateCrmLeadCommandValidator(), Create(email: email), "Email");
        AssertInvalid(new UpdateCrmLeadCommandValidator(), Update(email: email), "Email");
    }

    private static CrmLeadIntakeData Residential(CrmLeadIntakeResidentialData residential) =>
        new(Sector: ProjectSector.Residential, Residential: residential);

    private static void AssertInvalid<T>(IValidator<T> validator, T instance, string property)
    {
        ValidationResult result = validator.Validate(instance);
        Assert.Contains(result.Errors, e => e.PropertyName == property);
    }
}
