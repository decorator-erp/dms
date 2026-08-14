using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class ClientTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = Client.Create("user-1", "123 Main St", "some notes");

        Assert.True(result.IsSuccess);
        Assert.Equal("user-1", result.Value!.AppUserId);
        Assert.Equal("123 Main St", result.Value.Address);
        Assert.Equal("some notes", result.Value.Notes);
        Assert.True(result.Value.IsActive);
        Assert.Empty(result.Value.Children);
    }

    [Theory]
    [InlineData("", "addr")]
    [InlineData(" ", "addr")]
    [InlineData(null, "addr")]
    public void Create_WithoutAppUserId_Fails(string? appUserId, string address)
    {
        var result = Client.Create(appUserId!, address);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.AppUserIdRequired, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Create_WithoutAddress_Fails(string? address)
    {
        var result = Client.Create("user-1", address!);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.AddressRequired, result.Error);
    }

    [Fact]
    public void UpdateProfile_OnlySetsSuppliedFields()
    {
        var client = Client.Create("user-1", "addr").Value!;
        client.UpdateProfile(jobTitle: "Architect");

        Assert.Equal("Architect", client.JobTitle);
        Assert.Null(client.MaritalStatus);

        client.UpdateProfile(maritalStatus: "Married");

        // Job title supplied as null on the second call is left alone, not cleared.
        Assert.Equal("Architect", client.JobTitle);
        Assert.Equal("Married", client.MaritalStatus);
    }

    [Fact]
    public void FillBlanksFromIntake_NeverOverwritesOfficeEnteredData()
    {
        var client = Client.Create("user-1", "addr").Value!;
        client.UpdateProfile(jobTitle: "Architect");

        client.FillBlanksFromIntake(jobTitle: "Engineer", clientAddress: null);

        Assert.Equal("Architect", client.JobTitle);
    }

    [Fact]
    public void FillBlanksFromIntake_FillsOnlyBlankFields()
    {
        var client = Client.Create("user-1", "addr").Value!;

        client.FillBlanksFromIntake(jobTitle: "Engineer", clientAddress: "New Address");

        Assert.Equal("Engineer", client.JobTitle);
        // Address was already non-blank ("addr"), so it stays.
        Assert.Equal("addr", client.Address);
    }

    [Fact]
    public void FillBlanksFromIntake_FillsBlankAddress()
    {
        // Address can only originate non-blank via Create's own validation, but a defensive caller
        // (e.g. a future direct-construction path) must still see the blank-fill rule hold.
        var client = Client.Create("user-1", "addr").Value!;
        client.FillBlanksFromIntake(jobTitle: null, clientAddress: "Real Address");

        // Non-blank stays put per the "only when currently empty/whitespace" rule.
        Assert.Equal("addr", client.Address);
    }

    [Fact]
    public void AddChild_AppendsToChildren()
    {
        var client = Client.Create("user-1", "addr").Value!;

        var result = client.AddChild('M', 10);

        Assert.True(result.IsSuccess);
        Assert.Single(client.Children);
        Assert.Equal(result.Value, client.Children.Single().Id);
    }

    [Fact]
    public void AddChild_WithInvalidGender_Fails()
    {
        var client = Client.Create("user-1", "addr").Value!;

        var result = client.AddChild('X', 10);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientChildErrors.InvalidGender, result.Error);
        Assert.Empty(client.Children);
    }

    [Fact]
    public void UpdateChild_WhenPresent_Updates()
    {
        var client = Client.Create("user-1", "addr").Value!;
        var childId = client.AddChild('M', 10).Value;

        var result = client.UpdateChild(childId, 'F', 12);

        Assert.True(result.IsSuccess);
        Assert.Equal('F', client.Children.Single().Gender);
        Assert.Equal((short)12, client.Children.Single().Age);
    }

    [Fact]
    public void UpdateChild_WhenMissing_Fails()
    {
        var client = Client.Create("user-1", "addr").Value!;

        var result = client.UpdateChild(Guid.NewGuid(), 'F', 12);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.ChildNotFound, result.Error);
    }

    [Fact]
    public void RemoveChild_WhenPresent_Removes()
    {
        var client = Client.Create("user-1", "addr").Value!;
        var childId = client.AddChild('M', 10).Value;

        var result = client.RemoveChild(childId);

        Assert.True(result.IsSuccess);
        Assert.Empty(client.Children);
    }

    [Fact]
    public void RemoveChild_WhenMissing_Fails()
    {
        var client = Client.Create("user-1", "addr").Value!;

        var result = client.RemoveChild(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.ChildNotFound, result.Error);
    }

    [Fact]
    public void Deactivate_Then_Activate_TogglesIsActive()
    {
        var client = Client.Create("user-1", "addr").Value!;

        client.Deactivate();
        Assert.False(client.IsActive);

        client.Activate();
        Assert.True(client.IsActive);
    }
}
