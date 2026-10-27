using Crm.Contracts;
using Hr.Contracts;
using Projects.Contracts;

namespace Crm.Infrastructure.Services;

/// <summary>
/// Who hears about a meeting: the employee who requested it, the employee who accepted it (B-5) and the
/// client's portal login while the client is active (M4), deduplicated.
/// Shared by every meeting notification and the reminder job, so they always agree.
/// </summary>
internal static class MeetingRecipients
{
    public static async Task<IReadOnlyList<string>> ResolveAsync(
        Guid projectId, Guid? requestedByEmployeeId, Guid? acceptedByEmployeeId,
        IHrDirectory hrDirectory, IProjectDirectory projectDirectory, IClientDirectory clientDirectory, CancellationToken ct)
    {
        Guid[] employeeIds = [.. new[] { requestedByEmployeeId, acceptedByEmployeeId }.OfType<Guid>().Distinct()];
        IReadOnlyList<EmployeeSummary> employees = employeeIds.Length == 0 ? [] : await hrDirectory.GetManyAsync(employeeIds, ct);

        ProjectSummary? project = await projectDirectory.GetProjectAsync(projectId, ct);
        ClientSummary? client = project is null ? null : await clientDirectory.GetAsync(project.ClientId, ct);

        // M4: a deactivated client (X-3) is cut off from the portal, so its login hears nothing either.
        string?[] candidates = [.. employees.Select(e => e.AppUserId), client is { IsActive: true } ? client.AppUserId : null];
        return [.. candidates.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>B-2: dedupe keys carry the scheduled instant, so a re-scheduled meeting is confirmed and
    /// reminded again, while an outbox retry of the same schedule still sends once.</summary>
    public static string Instant(DateTime scheduledAtUtc) => scheduledAtUtc.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
}
