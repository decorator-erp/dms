using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure.Services;

/// <inheritdoc cref="IPasswordSignIn"/>
internal sealed class PasswordSignIn(IServiceScopeFactory scopeFactory) : IPasswordSignIn
{
    // Hashed once per process with the real hasher, so the unknown-email path verifies a hash of the
    // same algorithm and iteration count as a real one, and costs the same time.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(null!, Guid.NewGuid().ToString("N")));

    public async Task<PasswordSignInResult> CheckAsync(string email, string password, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser? user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Burn the same work a real check would, so the response time doesn't reveal that the
            // email is unknown.
            userManager.PasswordHasher.VerifyHashedPassword(null!, DummyHash.Value, password);
            return new PasswordSignInResult(PasswordSignInOutcome.InvalidCredentials);
        }

        SignInManager<ApplicationUser> signInManager =
            scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

        // Checks lockout first (a locked account is reported without the password being looked at),
        // then the password; a failure counts towards lockout, a success resets the count.
        SignInResult result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return new PasswordSignInResult(PasswordSignInOutcome.LockedOut);
        }

        // An inactive account answers exactly like a wrong password: telling them apart would confirm
        // to a guesser that the password is right.
        return result.Succeeded && user.IsActive
            ? new PasswordSignInResult(PasswordSignInOutcome.Succeeded, user.Id)
            : new PasswordSignInResult(PasswordSignInOutcome.InvalidCredentials);
    }
}
