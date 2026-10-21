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
