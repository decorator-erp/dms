using Identity.Contracts;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Authentication;

/// <summary>
/// K4: the one place Identity's account policy is set. <c>IdentityModule.Register</c> applies it, and
/// the test fixtures that build Identity by hand apply the same method, so the two cannot drift.
///
/// <para><b>Lockout:</b> 5 failed attempts lock the account for 15 minutes. It applies to new users
/// (<c>AllowedForNewUsers</c>); <c>UserManager.CreateAsync</c> stamps <c>LockoutEnabled</c> from it, and
/// the K5 migration enables it on any existing row that predates this.</para>
/// <para><b>Password:</b> at least 8 characters with a digit, a lower-case letter, an upper-case letter
/// and a non-alphanumeric character. The values live in <see cref="IdentityPasswordPolicy"/>
/// (Identity.Contracts), so HR's employee create checks the same rule before provisioning (N9) and the
/// caller gets <c>employees.WeakPassword</c> instead of a generic provisioning failure. CRM's client
/// create checks it too (LC-2, <c>crm.clients.WeakPassword</c>).</para>
/// </summary>
internal static class IdentityPolicy
{
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public const int RequiredPasswordLength = IdentityPasswordPolicy.RequiredLength;

    public static void Configure(IdentityOptions options)
    {
        options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;
        options.Lockout.AllowedForNewUsers = true;

        options.Password.RequiredLength = RequiredPasswordLength;
        options.Password.RequireDigit = IdentityPasswordPolicy.RequireDigit;
        options.Password.RequireLowercase = IdentityPasswordPolicy.RequireLowercase;
        options.Password.RequireUppercase = IdentityPasswordPolicy.RequireUppercase;
        options.Password.RequireNonAlphanumeric = IdentityPasswordPolicy.RequireNonAlphanumeric;
    }
}
