using Crm.Contracts;
using Hr.Contracts;
using Projects.Contracts;

namespace Crm.Infrastructure.Services;

/// <summary>
/// Who hears about a meeting: the employee who requested it and the client's portal login, deduplicated.
/// Shared by every meeting notification and the reminder job, so they always agree.
/// </summary>
internal static class MeetingRecipients
{
    public static async Task<IReadOnlyList<string>> ResolveAsync(
        Guid projectId, Guid? requestedByEmployeeId,
        IHrDirectory hrDirectory, IProjectDirectory projectDirectory, IClientDirectory clientDirectory, CancellationToken ct)
    {
        EmployeeSummary? employee = requestedByEmployeeId is { } employeeId
            ? await hrDirectory.GetAsync(employeeId, ct)
            : null;

        ProjectSummary? project = await projectDirectory.GetProjectAsync(projectId, ct);
        ClientSummary? client = project is null ? null : await clientDirectory.GetAsync(project.ClientId, ct);

        string?[] candidates = [employee?.AppUserId, client?.AppUserId];
        return [.. candidates.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>B-2: dedupe keys carry the scheduled instant, so a re-scheduled meeting is confirmed and
    /// reminded again, while an outbox retry of the same schedule still sends once.</summary>
    public static string Instant(DateTime scheduledAtUtc) => scheduledAtUtc.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
}
