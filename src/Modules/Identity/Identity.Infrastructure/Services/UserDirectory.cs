using Identity.Contracts;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IUserDirectory"/> — the cross-module read contract other modules (HR, from
/// G2-04 onward) use instead of ever querying <c>identity.</c> tables directly. Declared at G1-01 but
/// never implemented until a real consumer needed it; HR's <c>CreateEmployeeCommand</c> validating an
/// <c>AppUserId</c> is the first caller.
/// </summary>
internal sealed class UserDirectory(UserManager<ApplicationUser> userManager) : IUserDirectory
{
    /// <summary>
    /// Queries <c>userManager.Users</c> directly with <c>AsNoTracking()</c> rather than
    /// <c>userManager.FindByIdAsync</c> — that call goes through the scoped <c>UserStore</c>'s own EF
    /// change tracker, so a second call within the SAME DI scope (a real pattern: one request, an
    /// earlier read, then a read-after-someone-else's-write check) would return the first call's
    /// tracked, now-stale snapshot instead of re-querying. Found via a test that deactivated a user
    /// through a completely different scope and then couldn't see it through this one.
    /// </summary>
    public async Task<UserSummary?> GetAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : ToSummary(user);
    }

    public async Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        return await userManager.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.FirstName + " " + u.LastName, u.IsActive))
            .ToListAsync(ct);
    }

    private static UserSummary ToSummary(ApplicationUser user) =>
        new(user.Id, user.Email!, $"{user.FirstName} {user.LastName}", user.IsActive);
}
