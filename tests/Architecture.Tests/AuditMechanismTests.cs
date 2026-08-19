using System.Reflection;
using System.Text.RegularExpressions;
using BuildingBlocks.Auditing;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>B18, closed by mechanism — assertion 3 of 3.</b>
///
/// <para>The other two live in <c>AuditInterceptorTests</c> and prove the audit row's entity name and
/// module are <i>derived</i> from EF metadata. Those are behavioural: they show the current
/// implementation reads the right thing. These are structural: they show there is no <i>other</i> way
/// to write an audit row, and therefore nothing for a future change to reintroduce.</para>
///
/// <para>B18 was not a typo. It was a shape: a hand-passed entity-name literal
/// (<c>LogActionAsync(AuditAction.CrmCatalogItemMoved, "CrmCatalogItem", …)</c>) checked against a
/// hand-maintained allow-list, where a mismatch wrote nothing and said nothing. Deleting the defect
/// means deleting the shape — a 3-member enum with no unreachable values to choose wrongly from, and a
/// factory only the interceptor can reach.</para>
/// </summary>
public class AuditMechanismTests
{
    /// <summary>
    /// Three members, matching <c>MapAction(EntityState)</c>'s entire range. The backend's enum has
    /// ~400. Each extra one would be unreachable through the interceptor and would exist only to be
    /// passed by a caller — which is the API this design removes.
    /// </summary>
    [Fact]
    public void AuditAction_HasExactlyTheThreeMembersTheInterceptorCanProduce()
    {
        Assert.Equal(
            ["Create", "Delete", "Update"],
            Enum.GetNames<AuditAction>().Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// No caller outside BuildingBlocks can construct an audit row: the factory is <c>internal</c> and
    /// the constructor is private. Reflection rather than a source scan, because this is the property
    /// the compiler actually enforces.
    /// </summary>
    [Fact]
    public void AuditLog_ExposesNoPublicWayToConstructARow()
    {
        // PC-03 liveness: both assertions below are over reflected member lists, and both are satisfied
        // by a reflection call that returned nothing at all. These two lines prove the type is being
        // read — a non-public constructor exists, and public members exist to have been filtered.
        Assert.NotEmpty(typeof(AuditLog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotEmpty(typeof(AuditLog).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance));

        Assert.Empty(typeof(AuditLog).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var publicFactories = typeof(AuditLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(AuditLog))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(publicFactories);
    }

    /// <summary>
    /// Exactly one call site, and it is the interceptor. If a second appears, the "every value is
    /// derived" claim stops being checkable by reading one file — which is precisely how the backend's
    /// audit trail drifted.
    /// </summary>
    [Fact]
    public void AuditLogRecord_HasExactlyOneCallSite_AndItIsTheInterceptor()
    {
        var callSites = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(IsNotBuildOutput)
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"AuditLog\.Record\s*\("))
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.Equal(["AuditSaveChangesInterceptor.cs"], callSites);
    }

    /// <summary>
    /// Every module <c>DbContext</c> that maps the outbox also maps the audit table.
    ///
    /// <para>The interceptor throws when it finds auditable changes on a context that does not map
    /// <c>AuditLog</c>, so a module that forgets <c>ConfigureAudit</c> fails loudly rather than
    /// silently — but only once something in it is actually written. This turns that into a build-time
    /// answer, so the eighth module cannot ship un-audited and be discovered by a user.</para>
    /// </summary>
    [Fact]
    public void EveryModuleDbContext_ThatConfiguresTheOutbox_AlsoConfiguresAudit()
    {
        var contexts = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot.Path, "src", "Modules"), "*DbContext.cs", SearchOption.AllDirectories)
            .Where(IsNotBuildOutput)
            .Select(file => (File: file, Text: File.ReadAllText(file)))
            .Where(c => c.Text.Contains("ConfigureOutbox(", StringComparison.Ordinal))
            .ToList();

        // Non-vacuity: seven module contexts today, eight once Notifications lands. If this list is
        // ever empty the test below passes while proving nothing.
        Assert.True(contexts.Count >= 7, $"Expected at least 7 module DbContexts, found {contexts.Count}.");

        var missing = contexts
            .Where(c => !c.Text.Contains("ConfigureAudit(", StringComparison.Ordinal))
            .Select(c => Path.GetFileName(c.File))
            .ToList();

        Assert.True(missing.Count == 0,
            "These module DbContexts map the outbox but not audit_logs, so every write through them "
            + $"throws at the first auditable change: {string.Join(", ", missing)}");
    }

    private static bool IsNotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
