using Identity.Domain;
using SharedKernel;
using Xunit;

namespace Identity.UnitTests;

/// <summary>N7 (B9): the name HR's employee edit writes into Identity.</summary>
public class ApplicationUserNameTests
{
    private static ApplicationUser CreateUser() =>
        ApplicationUser.Create(Guid.NewGuid().ToString(), "x@example.com", "x@example.com", "Old", "Name", UserType.Employee);

    [Fact]
    public void UpdateName_trims_and_sets_both_names()
    {
        ApplicationUser user = CreateUser();

        Result result = user.UpdateName("  Jane ", " Doe  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane", user.FirstName);
        Assert.Equal("Doe", user.LastName);
    }

    [Fact]
    public void UpdateName_to_the_same_name_succeeds()
    {
        ApplicationUser user = CreateUser();

        Assert.True(user.UpdateName("Old", "Name").IsSuccess);
        Assert.True(user.UpdateName("Old", "Name").IsSuccess);
    }

    [Theory]
    [InlineData("", "Doe")]
    [InlineData("   ", "Doe")]
    [InlineData("Jane", "")]
    public void UpdateName_refuses_an_empty_name_and_changes_nothing(string first, string last)
    {
        ApplicationUser user = CreateUser();

        Result result = user.UpdateName(first, last);

        Assert.Equal("users.InvalidName", result.Error.Code);
        Assert.Equal("Old", user.FirstName);
        Assert.Equal("Name", user.LastName);
    }

    [Fact]
    public void UpdateName_refuses_a_name_over_100_characters()
    {
        ApplicationUser user = CreateUser();

        Assert.Equal("users.InvalidName", user.UpdateName(new string('a', 101), "Doe").Error.Code);
        Assert.True(user.UpdateName(new string('a', 100), "Doe").IsSuccess);
    }
}
