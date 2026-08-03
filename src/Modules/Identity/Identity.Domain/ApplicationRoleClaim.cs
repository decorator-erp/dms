using System.Globalization;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/ApplicationRoleClaim.cs</c>. One deliberate fix on the
/// way in: display-name derivation now uses <see cref="CultureInfo.InvariantCulture"/> instead of
/// <see cref="CultureInfo.CurrentCulture"/> — permission strings are ASCII identifiers
/// (<c>"employees.read"</c>), and culture-sensitive casing on those (e.g. the Turkish "i" problem) was
/// a latent bug in the original, not a deliberate feature.
/// </summary>
internal sealed class ApplicationRoleClaim : IdentityRoleClaim<string>, IAuditable
{
    public string Group { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>
    /// Public, not private — ASP.NET Identity's own <c>RoleStore</c> constructs role claims via a
    /// <c>new()</c> generic constraint on <c>TRoleClaim</c> from a different assembly, which a private
    /// constructor cannot satisfy (a <c>TypeLoadException</c> at the generic-store construction site,
    /// discovered by actually running <see cref="IdentityModule.Register"/>, not by inspection). EF
    /// materialization also uses this constructor.
    /// </summary>
    public ApplicationRoleClaim()
    {
    }

    public static ApplicationRoleClaim Create(
        string roleId, string claimType, string claimValue, string? description = null)
    {
        var segments = claimValue.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries);
        var displayName = string.Join(" ", segments.Select(CapitalizeFirst));
        var group = segments.Length > 0 ? CapitalizeFirst(segments[0]) : claimValue;

        return new ApplicationRoleClaim
        {
            RoleId = roleId,
            ClaimType = claimType,
            ClaimValue = claimValue,
            Group = group,
            DisplayName = displayName,
            Description = description ?? $"Allows the user to perform the '{displayName}' action.",
        };
    }

    private static string CapitalizeFirst(string value) =>
        value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
