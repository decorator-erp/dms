using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel;

namespace BuildingBlocks.Auditing;

/// <summary>
/// Decides which property values never reach an audit row.
///
/// <para><b><see cref="AuditIgnoreAttribute"/> alone is not enough, and this is not a belt-and-braces
/// choice.</b> <c>ApplicationUser</c> implements <c>IAuditable</c>, so every login and every profile
/// edit produces an audit row — and its <c>PasswordHash</c>, <c>SecurityStamp</c> and
/// <c>ConcurrencyStamp</c> are declared on Microsoft's <c>IdentityUser&lt;string&gt;</c> base type,
/// which cannot be given an attribute. Without the name-based rules below, turning the interceptor on
/// would write password hashes into a queryable table on the first user write. The name list is the
/// mechanism that actually protects those three; the attribute is what protects properties this
/// codebase owns.</para>
///
/// <para>Ported from <c>backend/Infrastructure/Persistence/Auditing/AuditMetadata.cs</c>. The
/// fragment matching is deliberately broad — a false positive redacts a value that did not need
/// redacting, a false negative publishes a secret.</para>
/// </summary>
internal static class AuditRedaction
{
    internal const string RedactedValue = "***";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "SecurityStamp",
        "ConcurrencyStamp",
        "PasswordResetCode",
        "TwoFactorSecret",
    };

    private static readonly string[] SensitiveFragments = ["Token", "Password", "Secret"];

    internal static bool IsSensitive(IProperty property)
    {
        var name = property.Name;

        if (SensitiveNames.Contains(name))
            return true;

        if (SensitiveFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            return true;

        return property.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() is not null;
    }

    internal static object? Redact(IProperty property, object? value) =>
        IsSensitive(property) ? RedactedValue : value;
}
