using Api;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// FINDING-P1-13: the FINDING-P1-9 fail-fast must not block a passwordless Npgsql authentication that is
/// legitimate — a passfile, a client certificate, Kerberos/GSS, or <c>PGPASSWORD</c>/<c>PGPASSFILE</c> in the
/// environment. The check is called directly: a host that passed it would go on to connect to a server that
/// does not exist. No message may echo the connection string.
/// </summary>
[Collection(ConnectionStringCheckTests.ProcessEnvironment)]
public sealed class ConnectionStringCheckTests
{
    /// <summary>The PGPASSWORD test mutates the process environment, so this class runs alone.</summary>
    public const string ProcessEnvironment = "Process environment";

    private const string NoPassword = "Host=db.invalid;Port=5432;Database=dms_modular;Username=dms_app;Password=";

    private static string? NoEnvironment(string name) => null;

    [Fact]
    public void An_empty_password_with_nothing_else_fails_with_the_password_message()
    {
        string? problem = DI.ConnectionStringProblem(NoPassword, NoEnvironment);

        Assert.NotNull(problem);
        Assert.StartsWith("ConnectionStrings:DefaultConnection has no password outside Development.", problem, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("db.invalid", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_passes() =>
        Assert.Null(DI.ConnectionStringProblem("Host=db.invalid;Username=dms_app;Password=p", NoEnvironment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_connection_string_fails_with_its_own_message(string? connectionString)
    {
        string? problem = DI.ConnectionStringProblem(connectionString, NoEnvironment);

        Assert.NotNull(problem);
        Assert.StartsWith("ConnectionStrings:DefaultConnection is not configured outside Development.", problem, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connection_string_that_does_not_parse_fails_with_its_own_message_and_no_value()
    {
        const string unparseable = "Host=db.invalid;Username=dms_app;Password=s3cret;NotAnNpgsqlKeyword=1";

        string? problem = DI.ConnectionStringProblem(unparseable, NoEnvironment);

        Assert.NotNull(problem);
        Assert.StartsWith("ConnectionStrings:DefaultConnection could not be parsed outside Development.", problem, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("db.invalid", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("NotAnNpgsqlKeyword", problem, StringComparison.Ordinal);
    }

    /// <summary>Npgsql 10 has no <c>Integrated Security</c> keyword (it rejects it); Kerberos/GSS is chosen by
    /// the server, and the connection string says so by naming its Kerberos settings.</summary>
    [Theory]
    [InlineData(NoPassword + ";Kerberos Service Name=postgres")]
    [InlineData(NoPassword + ";Include Realm=true")]
    public void Kerberos_integrated_security_without_a_password_passes(string connectionString) =>
        Assert.Null(DI.ConnectionStringProblem(connectionString, NoEnvironment));

    [Fact]
    public void A_passfile_without_a_password_passes() =>
        Assert.Null(DI.ConnectionStringProblem(NoPassword + ";Passfile=/run/secrets/pgpass", NoEnvironment));

    [Fact]
    public void A_client_certificate_without_a_password_passes() =>
        Assert.Null(DI.ConnectionStringProblem(NoPassword + ";SSL Certificate=/run/secrets/client.crt", NoEnvironment));

    [Theory]
    [InlineData("PGPASSWORD")]
    [InlineData("PGPASSFILE")]
    public void The_libpq_environment_variables_pass(string variable) =>
        Assert.Null(DI.ConnectionStringProblem(NoPassword, name => name == variable ? "set" : null));

    [Fact]
    public void PGPASSWORD_in_the_real_process_environment_passes()
    {
        string? saved = Environment.GetEnvironmentVariable("PGPASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("PGPASSWORD", "set-by-test");
            Assert.Null(DI.ConnectionStringProblem(NoPassword, Environment.GetEnvironmentVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PGPASSWORD", saved);
        }

        Assert.Equal(saved, Environment.GetEnvironmentVariable("PGPASSWORD"));
    }
}

[CollectionDefinition(ConnectionStringCheckTests.ProcessEnvironment, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
