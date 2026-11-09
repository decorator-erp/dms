using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmLeadTests
{
    private static CrmLead CreateLead() =>
        CrmLead.Create("Jane", "Doe", (LeadSource)1, Guid.NewGuid()).Value!;

    [Fact]
    public void Create_WithValidData_StartsAsNew()
    {
        Result<CrmLead> result = CrmLead.Create("Jane", "Doe", (LeadSource)1, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal((LeadStatus)1, result.Value!.Status);
        Assert.Null(result.Value.ConvertedClientId);
    }

    /// <summary>LI-1: a website lead arrives with no owner; the office assigns one later.</summary>
    [Fact]
    public void Create_WithNoOwner_IsAnUnassignedLead_ThatCanBeAssignedLater()
    {
        Result<CrmLead> result = CrmLead.Create("Jane", "Doe", (LeadSource)1, ownerId: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.OwnerId);

        var owner = Guid.NewGuid();
        Assert.True(result.Value.Update("Jane", "Doe", (LeadSource)1, owner, null, null, null).IsSuccess);
        Assert.Equal(owner, result.Value.OwnerId);
    }

    [Theory]
    [InlineData("", "Doe")]
    [InlineData("Jane", "")]
    [InlineData(null, "Doe")]
    public void Create_WithoutRequiredNames_Fails(string? firstName, string lastName)
    {
        Result<CrmLead> result = CrmLead.Create(firstName!, lastName, (LeadSource)1, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.NameRequired, result.Error);
    }

    [Fact]
    public void ChangeStatus_ToNonConvertedValue_Succeeds()
    {
        CrmLead lead = CreateLead();

        Result result = lead.ChangeStatus((LeadStatus)2);

        Assert.True(result.IsSuccess);
        Assert.Equal((LeadStatus)2, lead.Status);
    }

    [Fact]
    public void ChangeStatus_ToConverted_IsRejected()
    {
        // Conversion happens only through Convert() so the client link is always set consistently.
        CrmLead lead = CreateLead();

        Result result = lead.ChangeStatus(LeadStatus.Converted);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.InvalidStatusTransition, result.Error);
    }

    [Fact]
    public void Convert_WithValidClientId_Succeeds()
    {
        CrmLead lead = CreateLead();
        var clientId = Guid.NewGuid();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Result result = lead.Convert(clientId, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeadStatus.Converted, lead.Status);
        Assert.Equal(clientId, lead.ConvertedClientId);
        Assert.Equal(now, lead.ConvertedAtUtc);
    }

    [Fact]
    public void Convert_WithEmptyClientId_Fails()
    {
        // The bug fixed on port: the backend's Convert() accepted any Guid, including Guid.Empty,
        // trusting the one caller to have checked first. The entity now rejects the cheap case itself.
        CrmLead lead = CreateLead();

        Result result = lead.Convert(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.InvalidClientId, result.Error);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Null(lead.ConvertedClientId);
    }

    [Fact]
    public void Convert_WhenAlreadyConverted_Fails()
    {
        CrmLead lead = CreateLead();
        Assert.True(lead.Convert(Guid.NewGuid()).IsSuccess);

        Result result = lead.Convert(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.AlreadyConverted, result.Error);
    }

    [Fact]
    public void Update_WhenAlreadyConverted_Fails()
    {
        CrmLead lead = CreateLead();
        Assert.True(lead.Convert(Guid.NewGuid()).IsSuccess);

        Result result = lead.Update("A", "B", (LeadSource)2, Guid.NewGuid(), null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.AlreadyConverted, result.Error);
    }

    // F1: a blank email or phone means "none".

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankEmailOrPhone_StoresNull(string blank)
    {
        CrmLead lead = CrmLead.Create("Jane", "Doe", (LeadSource)1, Guid.NewGuid(), email: blank, phone: blank).Value!;

        Assert.Null(lead.Email);
        Assert.Null(lead.Phone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankEmailOrPhone_StoresNull(string blank)
    {
        CrmLead lead = CrmLead.Create("Jane", "Doe", (LeadSource)1, Guid.NewGuid(), "a@b.co", "123").Value!;

        Result result = lead.Update("Jane", "Doe", (LeadSource)1, lead.OwnerId, blank, blank, null);

        Assert.True(result.IsSuccess);
        Assert.Null(lead.Email);
        Assert.Null(lead.Phone);
    }

    [Fact]
    public void Create_And_Update_TrimEmailAndPhone()
    {
        CrmLead lead = CrmLead.Create("Jane", "Doe", (LeadSource)1, Guid.NewGuid(), "  a@b.co  ", " 123 ").Value!;
        Assert.Equal("a@b.co", lead.Email);
        Assert.Equal("123", lead.Phone);

        Assert.True(lead.Update("Jane", "Doe", (LeadSource)1, lead.OwnerId, "  c@d.co ", "  456  ", null).IsSuccess);
        Assert.Equal("c@d.co", lead.Email);
        Assert.Equal("456", lead.Phone);
    }
}
