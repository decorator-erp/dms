using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmLeadTests
{
    private static CrmLead CreateLead() =>
        CrmLead.Create("Jane", "Doe", (LeadSource)1, Guid.NewGuid()).Value!;

    [Fact]
    public void Create_WithValidData_StartsAsNew()
    {
        var result = CrmLead.Create("Jane", "Doe", (LeadSource)1, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal((LeadStatus)1, result.Value!.Status);
        Assert.Null(result.Value.ConvertedClientId);
    }

    [Theory]
    [InlineData("", "Doe")]
    [InlineData("Jane", "")]
    [InlineData(null, "Doe")]
    public void Create_WithoutRequiredNames_Fails(string? firstName, string lastName)
    {
        var result = CrmLead.Create(firstName!, lastName, (LeadSource)1, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.NameRequired, result.Error);
    }

    [Fact]
    public void ChangeStatus_ToNonConvertedValue_Succeeds()
    {
        var lead = CreateLead();

        var result = lead.ChangeStatus((LeadStatus)2);

        Assert.True(result.IsSuccess);
        Assert.Equal((LeadStatus)2, lead.Status);
    }

    [Fact]
    public void ChangeStatus_ToConverted_IsRejected()
    {
        // Conversion happens only through Convert() so the client link is always set consistently.
        var lead = CreateLead();

        var result = lead.ChangeStatus(LeadStatus.Converted);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.InvalidStatusTransition, result.Error);
    }

    [Fact]
    public void Convert_WithValidClientId_Succeeds()
    {
        var lead = CreateLead();
        var clientId = Guid.NewGuid();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = lead.Convert(clientId, now);

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
        var lead = CreateLead();

        var result = lead.Convert(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.InvalidClientId, result.Error);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Null(lead.ConvertedClientId);
    }

    [Fact]
    public void Convert_WhenAlreadyConverted_Fails()
    {
        var lead = CreateLead();
        Assert.True(lead.Convert(Guid.NewGuid()).IsSuccess);

        var result = lead.Convert(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.AlreadyConverted, result.Error);
    }

    [Fact]
    public void Update_WhenAlreadyConverted_Fails()
    {
        var lead = CreateLead();
        Assert.True(lead.Convert(Guid.NewGuid()).IsSuccess);

        var result = lead.Update("A", "B", (LeadSource)2, Guid.NewGuid(), null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.AlreadyConverted, result.Error);
    }
}
