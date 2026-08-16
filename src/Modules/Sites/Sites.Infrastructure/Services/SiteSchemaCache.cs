using Microsoft.Extensions.Caching.Memory;
using Sites.Contracts;
using Sites.Contracts.Definition;

namespace Sites.Infrastructure.Services;

/// <inheritdoc cref="ISiteSchemaCache"/>
internal sealed class SiteSchemaCache(IMemoryCache cache) : ISiteSchemaCache
{
    /// <summary>
    /// Bounded staleness for other instances. Definitions change monthly, so 45 seconds is invisible to
    /// a surveyor but short enough that a form edit cannot go unnoticed for a shift.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Bumped on every definition write. Part of the KEY rather than a cache eviction, so retiring the
    /// whole schema set is a single interlocked increment and the orphans fall out on their own TTL —
    /// there is no way to enumerate <see cref="IMemoryCache"/>'s keys, and per-entry eviction tokens
    /// would be far more machinery for the same effect.
    ///
    /// <para>Static: one counter per process, which is exactly its scope. Cross-instance staleness is
    /// what <see cref="Ttl"/> covers, and nothing else does.</para>
    /// </summary>
    private static long _version;

    public async Task<SiteModuleSchemaResponse?> GetOrAddAsync(
        string moduleCode,
        Func<CancellationToken, Task<SiteModuleSchemaResponse?>> factory,
        CancellationToken ct = default)
    {
        var key = $"site-schema:{moduleCode.Trim().ToLowerInvariant()}:{Interlocked.Read(ref _version)}";

        // A Lazy<Task<T>>, not the response. On a miss every concurrent caller would otherwise run the
        // factory -- a shift starting together, or the first form open after any definition write bumps
        // the version, puts one identical schema query per request onto the database. GetOrCreate is not
        // atomic, but the losers discard an UNSTARTED Lazy rather than an in-flight query, and every
        // caller awaits whichever Lazy won.
        var lazy = cache.GetOrCreate(key, entry =>
        {
            // Absolute, never sliding. This entry is read on every form open, so a sliding window would
            // never expire -- and the TTL exists precisely to bound how long another instance can serve
            // a schema this one has already changed.
            entry.AbsoluteExpirationRelativeToNow = Ttl;

            // CancellationToken.None deliberately: the build is shared, so honouring the first caller's
            // token would let whoever arrived first cancel the schema load for everyone waiting behind
            // them. Each caller still abandons its own await when its request ends.
            return new Lazy<Task<SiteModuleSchemaResponse?>>(
                () => factory(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);
        })!;

        SiteModuleSchemaResponse? schema;
        try
        {
            schema = await lazy.Value;
        }
        catch
        {
            // A failed build must not be what every later caller awaits for the next 45 seconds.
            cache.Remove(key);
            throw;
        }

        // A missing module is not cached: it is usually a typo in a URL, and caching it would keep
        // answering 404 for 45 seconds after someone creates the module they just mistyped.
        if (schema is null) cache.Remove(key);

        return schema;
    }

    public void Invalidate() => Interlocked.Increment(ref _version);
}
