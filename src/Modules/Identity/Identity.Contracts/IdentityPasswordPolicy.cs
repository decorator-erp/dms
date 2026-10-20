namespace Identity.Contracts;

/// <summary>
/// N9 (B4): the password rules, in one place, for every caller that must agree on them. Identity's
/// <c>IdentityOptions</c> are configured from these values, and a module that creates accounts (HR's
/// employee create) checks <see cref="IsSatisfiedBy"/> first, so a weak password is answered with a
/// specific error instead of a generic provisioning failure.
/// </summary>
public static class IdentityPasswordPolicy
{
    public const int RequiredLength = 8;
    public const bool RequireDigit = true;
    public const bool RequireLowercase = true;
    public const bool RequireUppercase = true;
    public const bool RequireNonAlphanumeric = true;

    /// <summary>The same test ASP.NET Identity's default <c>PasswordValidator</c> applies with the
    /// options above (ASCII letter and digit classes; anything else is non-alphanumeric).</summary>
    public static bool IsSatisfiedBy(string? password) =>
        password is { Length: >= RequiredLength }
        && (!RequireDigit || password.Any(char.IsAsciiDigit))
        && (!RequireLowercase || password.Any(char.IsAsciiLetterLower))
        && (!RequireUppercase || password.Any(char.IsAsciiLetterUpper))
        && (!RequireNonAlphanumeric || password.Any(c => !char.IsAsciiLetterOrDigit(c)));
}
