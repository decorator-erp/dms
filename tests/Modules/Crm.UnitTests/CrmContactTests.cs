using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmContactTests
{
    private static readonly Guid ClientId = Guid.NewGuid();

    private static CrmContact CreateContact(bool isPrimary = false) =>
        CrmContact.Create(ClientId, "Jane", "Doe", (ContactRole)1, isPrimary).Value!;

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        Result<CrmContact> result = CrmContact.Create(ClientId, "Jane", "Doe", (ContactRole)1, isPrimary: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientId, result.Value!.ClientId);
        Assert.Equal("Jane", result.Value.FirstName);
        Assert.Equal("Doe", result.Value.LastName);
        Assert.True(result.Value.IsPrimary);
    }

    [Theory]
    [InlineData("", "Doe")]
    [InlineData("Jane", "")]
    [InlineData(" ", "Doe")]
    [InlineData(null, "Doe")]
    public void Create_WithoutRequiredNames_Fails(string? firstName, string lastName)
    {
        Result<CrmContact> result = CrmContact.Create(ClientId, firstName!, lastName, (ContactRole)1, false);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmContactErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Update_WithValidData_Succeeds()
    {
        CrmContact contact = CreateContact();

        Result result = contact.Update("John", "Smith", (ContactRole)2, "a@b.com", "123", "456", "note");

        Assert.True(result.IsSuccess);
        Assert.Equal("John", contact.FirstName);
        Assert.Equal("Smith", contact.LastName);
        Assert.Equal("a@b.com", contact.Email);
    }

    [Fact]
    public void Update_WithoutRequiredNames_Fails()
    {
        CrmContact contact = CreateContact();

        Result result = contact.Update("", "Smith", (ContactRole)2, null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmContactErrors.NameRequired, result.Error);
        // Rejected update leaves the entity's prior state untouched.
        Assert.Equal("Jane", contact.FirstName);
    }

    [Fact]
    public void MarkPrimary_Then_ClearPrimary_TogglesIsPrimary()
    {
        CrmContact contact = CreateContact(isPrimary: false);

        contact.MarkPrimary();
        Assert.True(contact.IsPrimary);

        contact.ClearPrimary();
        Assert.False(contact.IsPrimary);
    }
}
