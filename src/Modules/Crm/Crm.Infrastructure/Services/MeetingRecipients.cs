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
    /// <summary>One meeting's recipient inputs, for <see cref="ResolveManyAsync"/>.</summary>
    public sealed record Subject(Guid MeetingId, Guid ProjectId, Guid? RequestedByEmployeeId, Guid? AcceptedByEmployeeId);

    public static async Task<IReadOnlyList<string>> ResolveAsync(
        Guid projectId, Guid? requestedByEmployeeId, Guid? acceptedByEmployeeId,
        IHrDirectory hrDirectory, IProjectDirectory projectDirectory, IClientDirectory clientDirectory, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> resolved = await ResolveManyAsync(
            [new Subject(Guid.Empty, projectId, requestedByEmployeeId, acceptedByEmployeeId)],
            hrDirectory, projectDirectory, clientDirectory, ct);
        return resolved[Guid.Empty];
    }

    /// <summary>B-13 (MT-N4): a whole batch in three bulk lookups (employees, projects, clients) instead
    /// of three per meeting.</summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ResolveManyAsync(
        IReadOnlyCollection<Subject> meetings,
        IHrDirectory hrDirectory, IProjectDirectory projectDirectory, IClientDirectory clientDirectory, CancellationToken ct)
    {
        Guid[] employeeIds = [.. meetings.SelectMany(m => new[] { m.RequestedByEmployeeId, m.AcceptedByEmployeeId }).OfType<Guid>().Distinct()];
        Dictionary<Guid, EmployeeSummary> employees = employeeIds.Length == 0
            ? []
            : (await hrDirectory.GetManyAsync(employeeIds, ct)).ToDictionary(e => e.Id);

        var projects = (await projectDirectory.GetProjectsAsync(
            [.. meetings.Select(m => m.ProjectId).Distinct()], ct)).ToDictionary(p => p.Id);
        var clients = (await clientDirectory.GetManyAsync(
            [.. projects.Values.Select(p => p.ClientId).Distinct()], ct)).ToDictionary(c => c.Id);

        return meetings.ToDictionary(m => m.MeetingId, m =>
        {
            ClientSummary? client = projects.TryGetValue(m.ProjectId, out ProjectSummary? project)
                ? clients.GetValueOrDefault(project.ClientId)
                : null;

            // M4: a deactivated client (X-3) is cut off from the portal, so its login hears nothing either.
            string?[] candidates =
            [
                .. new[] { m.RequestedByEmployeeId, m.AcceptedByEmployeeId }.OfType<Guid>()
                    .Select(id => employees.GetValueOrDefault(id)?.AppUserId),
                client is { IsActive: true } ? client.AppUserId : null,
            ];
            return (IReadOnlyList<string>)[.. candidates.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct(StringComparer.Ordinal)];
        });
    }

    /// <summary>B-2: dedupe keys carry the scheduled instant, so a re-scheduled meeting is confirmed and
    /// reminded again, while an outbox retry of the same schedule still sends once.</summary>
    public static string Instant(DateTime scheduledAtUtc) => scheduledAtUtc.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
}
