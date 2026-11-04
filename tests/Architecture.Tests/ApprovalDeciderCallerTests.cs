using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// B1.1 (C10): <c>Approvals.Contracts.IApprovalDecider</c> decides an approval <b>as a client</b> and trusts
/// its caller to have proven the client owns the subject. So it has exactly one caller — CRM's portal decide
/// command handler — besides its own implementation and registration. Any endpoint, any office handler, any
/// other module reaching for it would skip that ownership proof; this test is what says no.
/// </summary>
public class ApprovalDeciderCallerTests
{
    private const string Decider = "Approvals.Contracts.IApprovalDecider";

    private static readonly string[] Allowed =
    [
        "Approvals.Infrastructure.ApprovalsModule",
        "Approvals.Infrastructure.Services.ApprovalDecider",
        "Crm.Application.Features.Portal.DecideMyPortalApprovalCommandHandler",
    ];

    [Fact]
    public void OnlyTheCrmPortalDecideHandler_DependsOnIApprovalDecider()
    {
        Assembly[] source = [.. ModuleAssemblies.All, Assembly.Load("Api")];

        List<string> dependents = [.. Types.InAssemblies(source)
            .That()
            .HaveDependencyOn(Decider)
            .GetTypes()
            .Select(OutermostType)
            .Where(t => t.FullName != Decider)
            .Select(t => t.FullName!)
            .Distinct()
            .Order(StringComparer.Ordinal)];

        // LIVENESS: a scan that found nothing would pass vacuously.
        Assert.Contains("Approvals.Infrastructure.Services.ApprovalDecider", dependents);

        List<string> offenders = [.. dependents.Except(Allowed, StringComparer.Ordinal)];
        Assert.True(
            offenders.Count == 0,
            "IApprovalDecider decides as a client without checking who owns the subject; only CRM's portal "
            + $"decide command, which proves that first, may call it. Offenders: {string.Join(", ", offenders)}");
    }

    // A dependency taken inside an async method is recorded on the compiler-generated state machine, which
    // is nested in the type that wrote it.
    private static Type OutermostType(Type type)
    {
        Type current = type;
        while (current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }

        return current;
    }
}
