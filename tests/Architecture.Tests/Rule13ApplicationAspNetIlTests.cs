using Mono.Cecil;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Rule 13: no <c>*.Application</c> assembly uses a <c>Microsoft.AspNetCore*</c> type anywhere in its
/// IL.</b>
///
/// <para>Rule 2 checks what reflection can see: base types, interfaces, public properties, fields and
/// constructor parameters. It cannot see inside a method body, so a handler that took an
/// <c>IHttpContextAccessor</c> from <c>IServiceProvider</c>, or built an <c>IResult</c> in a local, passed
/// it. This reads the module's type-reference table, which lists every external type the IL names: in
/// signatures, method bodies, locals and generic arguments alike. Rule 2 stays as it is.</para>
///
/// <para>The one allowance is the one Rule 2 makes, for the reason recorded there:
/// <c>Microsoft.AspNetCore.Identity</c>'s data-model types, and only in <c>Identity.Application</c>, the
/// one module that models identity.</para>
/// </summary>
public class Rule13ApplicationAspNetIlTests
{
    private static readonly Dictionary<string, string> AllowedNamespacePrefixByAssembly = new(StringComparer.Ordinal)
    {
        ["Identity.Application"] = "Microsoft.AspNetCore.Identity",
    };

    [Fact]
    public void Rule13_ApplicationAssemblies_UseNoAspNetCoreTypeInTheirIl()
    {
        List<ModuleDefinition> applications = [.. IlAssemblies.WithSuffix(".Application")];

        // LIVENESS: nine modules, nine Application assemblies.
        Assert.True(applications.Count >= 9,
            $"Read only {applications.Count} *.Application assemblies, expected at least 9.");

        List<string> offenders = [.. applications.SelectMany(module =>
        {
            string name = module.Assembly.Name.Name;
            string? allowed = AllowedNamespacePrefixByAssembly.GetValueOrDefault(name);
            return module.GetTypeReferences()
                .Where(t => IlAssemblies.NamespaceOf(t).StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
                .Where(t => allowed is null || !IlAssemblies.NamespaceOf(t).StartsWith(allowed, StringComparison.Ordinal))
                .Select(t => $"{name} uses {t.FullName}");
        }).Distinct().Order(StringComparer.Ordinal)];

        Assert.True(offenders.Count == 0,
            "Application code reached for ASP.NET Core. Handlers return Result and the endpoint maps it; "
            + $"anything HTTP-shaped belongs in Infrastructure: {string.Join(", ", offenders)}");
    }
}
