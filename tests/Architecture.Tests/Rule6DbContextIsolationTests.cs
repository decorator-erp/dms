using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 6: no type may reference a <see cref="DbContext"/> outside its own module. Checked via
/// constructor parameters, fields, and properties — the shapes handlers, services, and decorators
/// actually use to hold a DbContext in this codebase ("handlers take the module's DbContext directly",
/// no repositories). A DbContext constructed and used only within a single method body without ever
/// being stored is not caught — that isn't a pattern this codebase uses.
///
/// Vacuous today — no module <see cref="DbContext"/> exists yet. Becomes non-vacuous at G1-03
/// (<c>IdentityModuleDbContext</c>) once a second module's context exists for it to be checked against
/// (G2-03, <c>HrDbContext</c>).
/// </summary>
public class Rule6DbContextIsolationTests
{
    [Fact]
    public void NoTypeReferencesADbContextOutsideItsOwnModule()
    {
        IReadOnlyList<Assembly> allModuleAssemblies = ModuleAssemblies.All;

        var dbContextsByModule = allModuleAssemblies
            .SelectMany(a => ModuleAssemblies.SafeGetTypes(a)
                .Where(t => typeof(DbContext).IsAssignableFrom(t) && t != typeof(DbContext))
                .Select(t => (Module: ModuleAssemblies.ModuleOf(a.GetName().Name!), Type: t)))
            .ToList();

        // PC-03 liveness. The early return below is a silent exit: no module DbContext discovered means
        // no assertion runs and the rule reports success. Nine modules, nine contexts — the floor
        // makes the "vacuous today" comment above a statement this test can now falsify.
        Assert.True(dbContextsByModule.Count >= 9,
            $"Discovered only {dbContextsByModule.Count} module DbContexts, expected at least 9. "
            + "Reflection is not seeing them, so this rule checks nothing.");

        if (dbContextsByModule.Count == 0)
        {
            return;
        }

        foreach (Assembly assembly in allModuleAssemblies)
        {
            string ownModule = ModuleAssemblies.ModuleOf(assembly.GetName().Name!);
            var foreignDbContexts = dbContextsByModule
                .Where(x => x.Module != ownModule)
                .Select(x => x.Type)
                .ToHashSet();

            if (foreignDbContexts.Count == 0)
            {
                continue;
            }

            foreach (Type type in ModuleAssemblies.SafeGetTypes(assembly))
            {
                Assert.True(!ReferencesAny(type, foreignDbContexts),
                    $"{type.FullName} in {assembly.GetName().Name} references a DbContext outside its own module");
            }
        }
    }

    private static bool ReferencesAny(Type owner, HashSet<Type> targets)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        if (owner.GetConstructors(flags).SelectMany(c => c.GetParameters()).Any(p => targets.Contains(p.ParameterType)))
        {
            return true;
        }

        if (owner.GetFields(flags | BindingFlags.Static).Any(f => targets.Contains(f.FieldType)))
        {
            return true;
        }

        if (owner.GetProperties(flags).Any(p => targets.Contains(p.PropertyType)))
        {
            return true;
        }

        return false;
    }
}
