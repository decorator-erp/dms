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

    /// <summary>FINDING-P1-9 / P1-17: outside Development a host whose configuration never overrode the
    /// committed placeholder connection string must refuse to start, naming the variable to set and never
    /// echoing the value. The check reads no environment variable, so a runner that exports PGPASSWORD (or
    /// anything else) cannot make this pass or fail (R1c-4).</summary>
    [Fact]
    public async Task A_production_host_left_on_the_committed_connection_string_fails_fast_naming_the_variable()
    {
        WebApplicationFactory<Program> host = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DefaultConnection", Api.DI.CommittedDefaultConnection);
        });

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
        Exception failFast = Assert.Single(Chain(thrown!), e =>
            e is InvalidOperationException && e.Message.Contains("ConnectionStrings__DefaultConnection", StringComparison.Ordinal));
        Assert.Contains("committed placeholder", failFast.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Api.DI.CommittedDefaultConnection, failFast.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", failFast.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
