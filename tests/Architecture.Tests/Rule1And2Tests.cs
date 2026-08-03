using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 1: <c>*.Domain</c> may not reference <c>Microsoft.EntityFrameworkCore</c>.
/// Rule 2: <c>*.Application</c> and <c>*.Domain</c> may not reference <c>Microsoft.AspNetCore.*</c>.
///
/// Vacuous today — no <c>*.Domain</c>/<c>*.Application</c> assembly exists yet. Becomes non-vacuous at
/// G1-02 (Identity.Domain) and G1-05 (Identity.Application).
/// </summary>
public class Rule1And2Tests
{
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
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Microsoft.AspNetCore")
                .GetResult();

            ArchAssert.Passes(assembly, result);
        }
    }
}
