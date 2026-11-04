using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// FINDING-P1-1: sync cross-module calls are depth-1 (CLAUDE.md). <c>Crm.Contracts.IClientDirectory</c> is
/// read synchronously by Projects, Finance and the Api layer, so its implementation must not itself call
/// another module: Projects → CRM → Identity is an A→B→C chain. A caller that needs the client's display
/// name reads <c>ClientSummary.AppUserId</c> here and asks <c>Identity.Contracts.IUserDirectory</c> itself —
/// two depth-1 calls. This test is what keeps <c>ClientDirectory</c> free of Identity.
/// </summary>
public class ClientDirectoryDepthOneTests
{
    private const string ClientDirectory = "Crm.Infrastructure.Services.ClientDirectory";

    [Fact]
    public void ClientDirectory_DoesNotDependOnIdentityContracts()
    {
        System.Reflection.Assembly crmInfrastructure =
            ModuleAssemblies.All.Single(a => a.GetName().Name == "Crm.Infrastructure");

        // LIVENESS: the type exists under this name, so a rename cannot make the check vacuous.
        Assert.Contains(
            ModuleAssemblies.SafeGetTypes(crmInfrastructure),
            t => t.FullName == ClientDirectory);

        // A dependency taken inside an async method lands on the compiler-generated state machine, which is
        // nested in ClientDirectory — so match the outermost declaring type.
        List<string> dependents = [.. Types.InAssembly(crmInfrastructure)
            .That()
            .HaveDependencyOn("Identity.Contracts")
            .GetTypes()
            .Select(Outermost)
            .Select(t => t.FullName!)
            .Distinct()];

        Assert.False(
            dependents.Contains(ClientDirectory),
            "IClientDirectory is read synchronously by other modules; its implementation calling Identity.Contracts "
            + "makes every such read an A→B→C chain. Read ClientSummary.AppUserId and call IUserDirectory from the caller.");
    }

    private static Type Outermost(Type type)
    {
        Type current = type;
        while (current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }

        return current;
    }
}
