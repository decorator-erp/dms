using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// LC-2: <c>POST /api/clients</c> provisions a portal login, so the password must meet Identity's rule
/// (8+ characters with a digit, a lower-case and an upper-case letter and a symbol). Before this check a
/// password without a symbol reached provisioning and came back as the deliberately vague
/// <c>users.CreateFailed</c> — the lead-convert wizard showed "The user could not be created". HR's
/// employee create has had the same check since N9.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmClientPasswordTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("NoSymbol12345678")]
    [InlineData("nouppercase1!")]
    [InlineData("NOLOWERCASE1!")]
    [InlineData("NoDigitsHere!!")]
    public async Task A_weak_password_is_WeakPassword_not_a_vague_provisioning_failure(string password)
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/clients", Body(password));

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)response.StatusCode}: {body}");
        Assert.Contains("crm.clients.WeakPassword", body);
        Assert.DoesNotContain("users.CreateFailed", body);
    }

    [Fact]
    public async Task A_password_that_meets_the_rule_creates_the_client()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/clients", Body("Str0ng!Passw0rd"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static object Body(string password) => new
    {
        Email = $"lc2-{Guid.NewGuid():N}@example.com",
        Password = password,
        FirstName = "Ibrahim",
        LastName = "Mohamed",
        Address = "Kafr El Sheikh",
        PhoneNumber = "+201000000123",
    };
}
