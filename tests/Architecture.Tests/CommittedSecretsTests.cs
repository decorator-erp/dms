using System.Data.Common;
using System.Text.Json;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// PF-S1: no committed configuration file carries a value for a secret. <c>appsettings.json</c> loads in
/// every environment, so a committed value silently defeats the fail-fasts in <c>Api.DI</c> (JWT key) and
/// <c>AdminUserSeeder</c> (seed password), which fire only when the key is unset. The keys may stay, empty,
/// to document the shape. A failure names the file and the key, never the value.
/// </summary>
public sealed class CommittedSecretsTests
{
    private static readonly string[][] SecretKeys =
    [
        ["JwtSettings", "Key"],
        ["DMS_SEED_ADMIN_PASSWORD"],
    ];

    [Fact]
    public void No_committed_appsettings_file_carries_a_secret_value()
    {
        string apiDirectory = Path.Combine(SolutionRoot.Path, "src", "Api");
        string[] files = Directory.GetFiles(apiDirectory, "appsettings*.json");
        Assert.NotEmpty(files);

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        List<string> violations = [];

        foreach (string file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), options);
            foreach (string[] path in SecretKeys)
            {
                if (TryFind(document.RootElement, path, out JsonElement value) && HasValue(value))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(SolutionRoot.Path, file)}: {string.Join(':', path)} has a value. "
                        + "Keep it empty here; supply it from user-secrets (Development) or an environment variable.");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>L-6 (CRM production review): the base <c>appsettings.json</c> loads in every environment, so
    /// no connection string in it may carry a password. The local docker value lives in
    /// <c>appsettings.Development.json</c>; every other environment supplies
    /// <c>ConnectionStrings__DefaultConnection</c>. A failure names the key, never the value.</summary>
    [Fact]
    public void The_base_appsettings_file_carries_no_connection_string_password()
    {
        string file = Path.Combine(SolutionRoot.Path, "src", "Api", "appsettings.json");
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var document = JsonDocument.Parse(File.ReadAllText(file), options);
        List<string> violations = [];

        if (TryFind(document.RootElement, ["ConnectionStrings"], out JsonElement connectionStrings)
            && connectionStrings.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty connectionString in connectionStrings.EnumerateObject())
            {
                var builder = new DbConnectionStringBuilder { ConnectionString = connectionString.Value.GetString() ?? string.Empty };
                if (builder.TryGetValue("Password", out object? password) && !string.IsNullOrWhiteSpace(password?.ToString()))
                {
                    violations.Add($"src/Api/appsettings.json: ConnectionStrings:{connectionString.Name} has a password. "
                        + "Keep it empty here; Development reads appsettings.Development.json, every other environment "
                        + "ConnectionStrings__" + connectionString.Name + ".");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    // Configuration keys are case-insensitive, so the lookup is too: a "jwtSettings" section must not slip by.
    private static bool TryFind(JsonElement element, string[] path, out JsonElement found)
    {
        found = element;
        foreach (string segment in path)
        {
            if (found.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            bool matched = false;
            foreach (JsonProperty property in found.EnumerateObject())
            {
                if (string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    found = property.Value;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        _ => true,
    };
}
