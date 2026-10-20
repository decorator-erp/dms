using Identity.Contracts;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Identity.Infrastructure.Services;

/// <inheritdoc cref="IUserSessionState"/>
internal sealed class UserSessionState(IdentityModuleDbContext db, IMemoryCache cache, IConfiguration configuration)
    : IUserSessionState
{
    public const string CacheSecondsKey = "Identity:SessionStateCacheSeconds";
    public const string SessionStateQueryTag = "identity:session-state";
    private const int DefaultCacheSeconds = 30;

    public async Task<UserSessionSnapshot?> GetAsync(string userId, CancellationToken ct) =>
        (await GetManyAsync([userId], ct)).SingleOrDefault();

    public async Task<IReadOnlyList<UserSessionSnapshot>> GetManyAsync(
        IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        var ttl = TimeSpan.FromSeconds(configuration.GetValue(CacheSecondsKey, DefaultCacheSeconds));
        List<UserSessionSnapshot> found = [];
        List<string> misses = [];

        foreach (string userId in userIds.Distinct())
        {
            if (ttl > TimeSpan.Zero && cache.TryGetValue(UserSessionCache.Key(userId), out UserSessionSnapshot? cached) && cached is not null)
            {
                found.Add(cached);
            }
            else
            {
                misses.Add(userId);
            }
        }

        if (misses.Count == 0)
        {
            return found;
        }

        // Tagged so a test that captures SQL can recognise this host-level, per-request read (it runs
        // during authentication, before any module's handler) and tell it apart from module code
        // touching identity.* (Integration.Tests' ApiFixture.CapturedSql).
        List<UserSessionSnapshot> loaded = await db.Users
            .TagWith(SessionStateQueryTag)
            .AsNoTracking()
            .Where(u => misses.Contains(u.Id))
            .Select(u => new UserSessionSnapshot(u.Id, u.SecurityStamp ?? string.Empty, u.IsActive))
            .ToListAsync(ct);

        // Only hits are cached. A missing user is not: the next request asks the database again, so a
        // user created a moment later is not refused for a whole TTL.
        if (ttl > TimeSpan.Zero)
        {
            foreach (UserSessionSnapshot snapshot in loaded)
            {
                cache.Set(UserSessionCache.Key(snapshot.UserId), snapshot, ttl);
            }
        }

        return [.. found, .. loaded];
    }

}

/// <summary>Lets the code that changes a user's security stamp drop their cached session state.</summary>
internal interface IUserSessionCache
{
    void Evict(string userId);
}

/// <summary>Singleton over the process's <see cref="IMemoryCache"/>, which is where
/// <see cref="UserSessionState"/> keeps its entries.</summary>
internal sealed class UserSessionCache(IMemoryCache cache) : IUserSessionCache
{
    public void Evict(string userId) => cache.Remove(Key(userId));

    internal static string Key(string userId) => $"identity:session:{userId}";
}
