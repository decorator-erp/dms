using BuildingBlocks.Messaging;
using Sites.Contracts;
using Xunit;

namespace Sites.Infrastructure.Tests;

/// <summary>
/// Ported from the backend's <c>GuardSiteScopeCoverage()</c> — a startup check that reflects over every
/// site command/query and throws if one carries a <c>Guid SiteId</c> without implementing
/// <c>ISiteScoped</c>.
///
/// <para><b>Why this is worth porting.</b> The 404-not-403 guarantee that <c>SiteScopeDecorator</c>
/// provides is only as good as the set of messages that opt into it, and opting in is a single
/// interface on a record declaration — trivially forgettable, and its absence is invisible: the handler
/// works, the tests pass, and the route simply serves any site to any caller. The backend's own comment
/// records that the per-handler version of this check had been written <b>once out of twenty-one</b>
/// times, and that adding the guard immediately caught a real miss (<c>AddSiteRecordCommand</c>).</para>
///
/// <para><b>Ported as a test rather than a startup throw</b>, matching this repo's shape — rule 9 and
/// the module-boot assertions are all tests here, and a startup exception would fail the app at run
/// time rather than the build at check-in time. The trade-off is that it guards the Contracts assembly
/// rather than every message at composition; since this module's messages all live in
/// <c>Sites.Contracts</c>, that is the same set.</para>
///
/// <para><b>The definition layer's exemption is expressed as "has no SiteId", not as an allow-list.</b>
/// Its 20 messages are global master data with no site to scope by, so they never trip this check —
/// and, crucially, no maintainer can add a site-carrying message to that folder and quietly claim the
/// same exemption.</para>
/// </summary>
public class SiteScopeCoverageTests
{
    [Fact]
    public void EveryMessageCarryingASiteId_ImplementsISiteScoped()
    {
        string[] offenders = typeof(SitesPermissions).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(IsMessage)
            .Where(t => t.GetProperty("SiteId")?.PropertyType == typeof(Guid))
            .Where(t => !typeof(ISiteScoped).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The converse, so the check cannot be satisfied by making everything scoped. A message that
    /// implements the marker but has no site id would resolve <c>SiteId</c> to <c>Guid.Empty</c> and
    /// take the 404 branch on every call — broken in a way that looks like a permissions problem.
    /// </summary>
    [Fact]
    public void EveryISiteScopedMessage_ActuallyCarriesASiteId()
    {
        string[] offenders = typeof(SitesPermissions).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => typeof(ISiteScoped).IsAssignableFrom(t))
            .Where(t => t.GetProperty("SiteId")?.PropertyType != typeof(Guid))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// A guard that scans an empty set passes forever. This is the vacuity check —
    /// <c>MetaSelfEnforcementTests</c>' own concern, applied locally.
    /// </summary>
    [Fact]
    public void TheGuardActuallyScansMessages()
    {
        int messages = typeof(SitesPermissions).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Count(IsMessage);

        int scoped = typeof(SitesPermissions).Assembly.GetTypes()
            .Count(t => t is { IsAbstract: false, IsInterface: false } && typeof(ISiteScoped).IsAssignableFrom(t));

        // 29 commands + 12 queries in the backend, minus the deferred variance query [D8].
        Assert.Equal(40, messages);

        // 18 scoped: 5 survey commands + 1 survey query, 5 record commands + 4 record queries,
        // 2 document commands + 1 document query. The 22 that are not: the 20 definition messages
        // (global master data, no site), plus CreateSiteCommand (no site yet) and GetSitesQuery (lists
        // sites rather than reading one) -- both of which authorize themselves against the project.
        Assert.Equal(18, scoped);
    }

    private static bool IsMessage(Type type) =>
        typeof(IBaseCommand).IsAssignableFrom(type)
        || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
}
