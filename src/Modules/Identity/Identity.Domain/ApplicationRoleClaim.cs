using System.Globalization;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Domain;

internal sealed class ApplicationRoleClaim : IdentityRoleClaim<string>, IAuditable
{
    /// <summary>
    /// Public, not private — ASP.NET Identity's own <c>RoleStore</c> constructs role claims via a
    /// <c>new()</c> generic constraint on <c>TRoleClaim</c> from a different assembly, which a private
    /// constructor cannot satisfy (a <c>TypeLoadException</c> at the generic-store construction site,
    /// discovered by actually running <see cref="IdentityModule.Register"/>, not by inspection). EF
    /// materialization also uses this constructor.
    /// </summary>
    public ApplicationRoleClaim() { }

    public string Group { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? Description { get; private set; }

    public static ApplicationRoleClaim Create(
        string roleId, string claimType, string claimValue, string? description = null)
    {
        string[] segments = claimValue.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries);
        string displayName = string.Join(" ", segments.Select(CapitalizeFirst));
        string group = segments.Length > 0 ? CapitalizeFirst(segments[0]) : claimValue;

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
