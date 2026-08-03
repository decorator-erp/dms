using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 1: <c>*.Domain</c> may not reference <c>Microsoft.EntityFrameworkCore</c>.
/// Rule 2: <c>*.Application</c> and <c>*.Domain</c> may not reference <c>Microsoft.AspNetCore.*</c>.
///
/// Vacuous today — no <c>*.Domain</c>/<c>*.Application</c> assembly exists yet. Became non-vacuous at
/// G1-02 (Identity.Domain); G1-05 (Identity.Application) is next.
/// </summary>
public class Rule1And2Tests
{
    /// <summary>
    /// Rule 2's scoped exception: <c>Microsoft.AspNetCore.Identity</c> — <c>IdentityUser&lt;TKey&gt;</c>,
    /// <c>IdentityRole&lt;TKey&gt;</c>, <c>IdentityRoleClaim&lt;TKey&gt;</c>, etc. G1-02 explicitly
    /// requires <c>ApplicationUser : IdentityUser&lt;string&gt;</c>, and these types are plain data
    /// models with zero request-pipeline dependencies — the package that declares them
    /// (<c>Microsoft.Extensions.Identity.Stores</c>) depends only on logging/caching abstractions, no
    /// EF Core and no ASP.NET web pipeline (confirmed by reading its own nuspec). Rule 2 exists to keep
    /// <c>HttpContext</c>/MVC/routing out of Domain and Application — the motivating example, cited in
    /// PLAN.md itself, is <c>IHttpContextAccessor</c> ending up inside <c>ProjectAccessService</c> in
    /// the backend — not to forbid modeling identity, which is Identity.Domain's entire purpose.
    /// Everything else under <c>Microsoft.AspNetCore.*</c> stays forbidden.
    /// </summary>
    private const string AllowedAspNetCoreNamespace = "Microsoft.AspNetCore.Identity";

    [Fact]
    public void Rule1_DomainAssemblies_MustNotReferenceEfCore()
    {
        foreach (var assembly in ModuleAssemblies.WithSuffix(".Domain"))
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Microsoft.EntityFrameworkCore")
                .GetResult();

            ArchAssert.Passes(assembly, result);
        }
    }

    [Fact]
    public void Rule2_ApplicationAndDomainAssemblies_MustNotReferenceAspNetCore()
    {
        foreach (var assembly in ModuleAssemblies.WithSuffix(".Application").Concat(ModuleAssemblies.WithSuffix(".Domain")))
        {
            var offenders = ModuleAssemblies.SafeGetTypes(assembly)
                .SelectMany(ReferencedAspNetCoreNamespaces)
                .Where(ns => !ns.StartsWith(AllowedAspNetCoreNamespace, StringComparison.Ordinal))
                .Distinct()
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{assembly.GetName().Name} references forbidden ASP.NET Core namespaces: {string.Join(", ", offenders)}");
        }
    }

    private static IEnumerable<string> ReferencedAspNetCoreNamespaces(Type type)
    {
        var candidates = new List<Type?> { type.BaseType }
            .Concat(type.GetInterfaces())
            .Concat(type.GetProperties().Select(p => p.PropertyType))
            .Concat(type.GetFields().Select(f => f.FieldType))
            .Concat(type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType));

        return candidates
            .Where(t => t?.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true)
            .Select(t => t!.Namespace!);
    }
}
