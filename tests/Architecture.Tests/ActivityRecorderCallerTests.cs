using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// S11: <c>Notifications.Contracts.IActivityRecorder</c> writes Notifications' own rows. <c>CLAUDE.md</c> allows one
/// synchronous cross-module write (<c>INotificationService</c>) and this is not a second: another module records an
/// activity by enqueueing <c>ActivityRecordedIntegrationEvent</c> on its own outbox. So the recorder's only callers
/// are inside Notifications — its implementation, its registration and the event's consumer. A handler in another
/// module reaching for it directly would commit the activity outside its own transaction; this test is what says no.
/// </summary>
public class ActivityRecorderCallerTests
{
    private const string Recorder = "Notifications.Contracts.IActivityRecorder";

    [Fact]
    public void OnlyNotifications_DependsOnIActivityRecorder()
    {
        Assembly[] source = [.. ModuleAssemblies.All, Assembly.Load("Api")];

        List<string> dependents = [.. Types.InAssemblies(source)
            .That()
            .HaveDependencyOn(Recorder)
            .GetTypes()
            .Select(OutermostType)
            .Where(t => t.FullName != Recorder)
            .Select(t => t.FullName!)
            .Distinct()
            .Order(StringComparer.Ordinal)];

        // LIVENESS: a scan that found nothing would pass vacuously.
        Assert.Contains("Notifications.Infrastructure.Activities.ActivityRecorder", dependents);
        Assert.Contains("Notifications.Infrastructure.IntegrationEvents.ActivityRecordedIntegrationEventHandler", dependents);

        List<string> offenders = [.. dependents.Where(d => !d.StartsWith("Notifications.", StringComparison.Ordinal))];
        Assert.True(
            offenders.Count == 0,
            "IActivityRecorder is Notifications' own writer; another module records an activity through "
            + $"ActivityRecordedIntegrationEvent on its outbox. Offenders: {string.Join(", ", offenders)}");
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
