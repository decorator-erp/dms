using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class ClientChildTests
{
    [Theory]
    [InlineData('M', (short)0)]
    [InlineData('F', (short)120)]
    [InlineData('M', (short)15)]
    public void Create_WithValidData_Succeeds(char gender, short age)
    {
        Result<ClientChild> result = ClientChild.Create(gender, age);

        Assert.True(result.IsSuccess);
        Assert.Equal(gender, result.Value!.Gender);
        Assert.Equal(age, result.Value.Age);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Theory]
    [InlineData('X')]
    [InlineData('m')]
    [InlineData('Z')]
    public void Create_WithInvalidGender_Fails(char gender)
    {
        Result<ClientChild> result = ClientChild.Create(gender, 10);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientChildErrors.InvalidGender, result.Error);
    }

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)121)]
    public void Create_WithInvalidAge_Fails(short age)
    {
        Result<ClientChild> result = ClientChild.Create('M', age);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientChildErrors.InvalidAge, result.Error);
    }

    [Fact]
    public void Update_WithValidData_Succeeds()
    {
        ClientChild child = ClientChild.Create('M', 10).Value!;

        Result result = child.Update('F', 12);

        Assert.True(result.IsSuccess);
        Assert.Equal('F', child.Gender);
        Assert.Equal((short)12, child.Age);
    }

    [Fact]
    public void Update_WithInvalidData_FailsAndLeavesStateUnchanged()
    {
        ClientChild child = ClientChild.Create('M', 10).Value!;

        Result result = child.Update('X', 200);

        Assert.True(result.IsFailure);
        Assert.Equal('M', child.Gender);
        Assert.Equal((short)10, child.Age);
    }
}
