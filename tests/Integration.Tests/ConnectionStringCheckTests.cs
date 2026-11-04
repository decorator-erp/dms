using System.Text.Json;
using Api;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// FINDING-P1-17: outside Development the host refuses to start only when
/// <c>ConnectionStrings:DefaultConnection</c> is missing, does not parse, or is still the committed placeholder
/// from <c>appsettings.json</c> — i.e. nothing overrode it. How the overriding string authenticates (password,
/// passfile, <c>~/.pgpass</c>, client certificate, Unix socket, GSS) is Npgsql's business, not the check's, and
/// the check reads no environment variable. The check is called directly: a host that passed it would go on to
/// connect to a server that does not exist. No message may echo a value.
/// </summary>
[Collection(ConnectionStringCheckTests.ProcessEnvironment)]
public sealed class ConnectionStringCheckTests
{
    /// <summary>The PGPASSWORD test mutates the process environment, so this class runs alone.</summary>
    public const string ProcessEnvironment = "Process environment";

    [Fact]
    public void The_committed_placeholder_constant_is_the_value_appsettings_json_commits()
    {
        // The Api's appsettings.json is copied next to this assembly, as it is next to the Api's.
        string file = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var document = JsonDocument.Parse(File.ReadAllText(file), options);

        string? committed = document.RootElement
            .GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();

        Assert.Equal(DI.CommittedDefaultConnection, committed);
    }

    [Fact]
    public void The_committed_placeholder_fails_with_the_placeholder_message_and_no_value()
    {
        string? problem = DI.ConnectionStringProblem(DI.CommittedDefaultConnection);

        Assert.NotNull(problem);
        Assert.StartsWith(
            "ConnectionStrings:DefaultConnection still has the committed placeholder value outside Development; "
            + "set ConnectionStrings__DefaultConnection", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("5433", problem, StringComparison.Ordinal);
    }

    /// <summary>R1c-1: the committed value is refused however the environment is set up. Before P1-17 a
    /// PGPASSWORD exported for some other tool let it through.</summary>
    [Fact]
    public void The_committed_placeholder_fails_even_with_PGPASSWORD_in_the_process_environment()
    {
        string? saved = Environment.GetEnvironmentVariable("PGPASSWORD");
        string? problem;
        try
        {
            Environment.SetEnvironmentVariable("PGPASSWORD", "set-by-test");
            problem = DI.ConnectionStringProblem(DI.CommittedDefaultConnection);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PGPASSWORD", saved);
        }

        Assert.Equal(saved, Environment.GetEnvironmentVariable("PGPASSWORD"));
        Assert.NotNull(problem);
        Assert.Contains("committed placeholder", problem, StringComparison.Ordinal);
    }

    /// <summary>Compared parsed, so a re-spelling of the same placeholder is still the placeholder.</summary>
    [Theory]
    [InlineData("host=localhost; port=5433; database=dms_modular; username=postgres; password=")]
    [InlineData("Username=postgres;Database=dms_modular;Port=5433;Host=localhost")]
    public void A_respelling_of_the_committed_placeholder_still_fails(string connectionString)
    {
        string? problem = DI.ConnectionStringProblem(connectionString);

        Assert.NotNull(problem);
        Assert.Contains("committed placeholder", problem, StringComparison.Ordinal);
    }

    /// <summary>R1c-2: an overriding string with no password is legitimate — a Unix socket with peer auth,
    /// the default <c>~/.pgpass</c>, <c>PGSSLCERT</c>, GSS with the default service name. The check does not
    /// second-guess it.</summary>
    [Theory]
    [InlineData("Host=/var/run/postgresql;Database=dms_modular;Username=dms_app")]
    [InlineData("Host=db;Username=app")]
    [InlineData("Host=db.invalid;Port=5432;Database=dms_modular;Username=dms_app;Password=")]
    [InlineData("Host=db.invalid;Username=dms_app;Password=p")]
    [InlineData("Host=localhost;Port=5433;Database=dms_modular;Username=postgres;Password=p")]
    public void An_overriding_connection_string_passes_whatever_its_authentication(string connectionString) =>
        Assert.Null(DI.ConnectionStringProblem(connectionString));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_connection_string_fails_with_its_own_message(string? connectionString)
    {
        string? problem = DI.ConnectionStringProblem(connectionString);

        Assert.NotNull(problem);
        Assert.StartsWith("ConnectionStrings:DefaultConnection is not configured outside Development.", problem, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connection_string_that_does_not_parse_fails_with_its_own_message_and_no_value()
    {
        const string unparseable = "Host=db.invalid;Username=dms_app;Password=s3cret;NotAnNpgsqlKeyword=1";

        string? problem = DI.ConnectionStringProblem(unparseable);

        Assert.NotNull(problem);
        Assert.StartsWith("ConnectionStrings:DefaultConnection could not be parsed outside Development.", problem, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("db.invalid", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("NotAnNpgsqlKeyword", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_three_messages_are_distinct()
    {
        string?[] problems =
        [
            DI.ConnectionStringProblem(null),
            DI.ConnectionStringProblem("NotAnNpgsqlKeyword=1"),
            DI.ConnectionStringProblem(DI.CommittedDefaultConnection),
        ];

        Assert.All(problems, Assert.NotNull);
        Assert.Equal(3, problems.Distinct(StringComparer.Ordinal).Count());
        Assert.All(problems, p => Assert.Contains("ConnectionStrings__DefaultConnection", p, StringComparison.Ordinal));
    }
}

[CollectionDefinition(ConnectionStringCheckTests.ProcessEnvironment, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
