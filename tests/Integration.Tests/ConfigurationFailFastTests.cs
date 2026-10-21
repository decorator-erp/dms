using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// PF-S1: <c>appsettings.json</c> no longer commits a JWT signing key, so a host started without one
/// must refuse to start, with the message that says how to set it. Before PF-S1 the committed value meant
/// this check in <c>Api.DI</c> never fired.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConfigurationFailFastTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_host_with_an_empty_JWT_key_fails_fast()
    {
        WebApplicationFactory<Program> host = fixture.WithWebHostBuilder(builder =>
            builder.UseSetting("JwtSettings:Key", ""));

        Exception? thrown = Record.Exception(() => host.Services);

        try
        {
            await host.DisposeAsync();
        }
        catch (InvalidOperationException)
        {
            // The host never built, so there is nothing for DisposeAsync to stop.
        }

        Assert.NotNull(thrown);
        Assert.Contains(Chain(thrown!), e =>
            e is InvalidOperationException
            && e.Message.StartsWith("JwtSettings:Key is not configured", StringComparison.Ordinal));
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
