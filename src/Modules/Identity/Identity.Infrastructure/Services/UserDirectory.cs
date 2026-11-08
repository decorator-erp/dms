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
    /// <remarks>Filter BEFORE the projection. Filtering after it puts the predicate on
    /// <see cref="UserSummary"/>, which is not an entity type, and EF cannot translate that — it throws
    /// at runtime rather than failing to compile, so every caller breaks at once.
    /// <see cref="GetManyAsync"/> below has always had the clauses in this order.</remarks>
    public Task<UserSummary?> GetAsync(string userId, CancellationToken ct) =>
        userManager.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(
                u.Id,
                u.Email!,
                u.FirstName + " " + u.LastName,
                u.IsActive,
                u.PhoneNumber))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await userManager.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.FirstName + " " + u.LastName, u.IsActive, u.PhoneNumber))
            .ToListAsync(ct);
    }
}
