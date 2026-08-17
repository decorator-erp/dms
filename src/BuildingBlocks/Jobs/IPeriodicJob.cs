namespace BuildingBlocks.Jobs;

/// <summary>
/// A scheduled job run by <see cref="PeriodicJobWorker"/> on every tick.
///
/// <para><b>Implementations must be idempotent.</b> The worker gives no at-most-once guarantee and
/// deliberately does not try to: a restart mid-sweep, an overlapping tick, or a manual flush all
/// re-run a job. Idempotence comes from the notification dedupe key, which
/// <c>INotificationService</c> stores per recipient, so re-running never duplicates a reminder. A job
/// that has a side effect other than notifying does not belong here.</para>
///
/// <para><b>A job reads only its own module's tables.</b> That is the whole reason this interface
/// lives in BuildingBlocks rather than in <c>Notifications.Contracts</c>: the backend's nine jobs each
/// read another area's tables, which here would force Notifications to depend on four modules. The
/// jobs moved to the modules that own their data instead, and the scheduling seam moved to the one
/// assembly everybody already depends on — so no module depends on Notifications for scheduling, and
/// nine background loops collapse into one timer. See <c>docs/low-resource-hosting.md</c> §3.</para>
///
/// <para>Registered per module with a Scrutor scan over that module's Application assembly, the same
/// define-in-one-module / implement-in-many shape as <c>IGateConditionTypeHandler</c>.</para>
/// </summary>
public interface IPeriodicJob
{
    /// <summary>Stable identity for logging, and the key <see cref="PeriodicJobWorker"/> uses to
    /// re-resolve this job in its own scope. Must be unique across every registered job.</summary>
    string Name { get; }

    Task RunAsync(CancellationToken ct);
}
