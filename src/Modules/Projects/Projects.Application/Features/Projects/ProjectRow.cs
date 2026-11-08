using Crm.Contracts;
using Hr.Contracts;
using Identity.Contracts;
using Projects.Contracts;
using Projects.Contracts.Grants;
using Projects.Contracts.Projects;

namespace Projects.Application.Features.Projects;

/// <summary>A project as read from the database, before the caller's level is known. <see cref="ToResponse"/> is the
/// only place a <see cref="ProjectResponse"/> is built, and it needs the level from
/// <see cref="IProjectAccessLevelReader"/>.</summary>
internal sealed record ProjectRow(
    Guid Id, string Code, string Name, string? Description, ProjectStatus Status, WorkflowType WorkflowType,
    string Address, string? Notes, decimal ProgressPercentage,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, DateTime? ActualStartDate, DateTime? ActualEndDate,
    Guid ClientId, Guid OwnerId, bool IsActive, int StageCount)
{
    /// <summary>P1-A: <c>MyLevel</c> is <paramref name="myAccess"/>'s level. S6: the client's and owner's display
    /// names come from <see cref="ProjectRowNames"/>, null when either cannot be resolved.</summary>
    public ProjectResponse ToResponse(ProjectAccessResponse myAccess, ProjectRowNames names) =>
        new(Id, Code, Name, Description, Status, WorkflowType, Address, Notes, ProgressPercentage,
            EstimatedStartDate, EstimatedEndDate, ActualStartDate, ActualEndDate,
            ClientId, OwnerId, IsActive, StageCount, myAccess.Level, myAccess,
            names.ClientNames.GetValueOrDefault(ClientId), names.OwnerNames.GetValueOrDefault(OwnerId));
}

/// <summary>S6: the display names a page of <see cref="ProjectRow"/>s shows, read in a fixed number of batched
/// depth-1 calls whatever the page size — never one per row. The client's name is its login's display name
/// (<see cref="IClientDirectory.GetManyAsync"/>, then <see cref="IUserDirectory.GetManyAsync"/>, as the project page
/// resolves it — never a CRM call that itself reads Identity); the owner's is the employee's full name
/// (<see cref="IHrDirectory.GetProfilesAsync"/>, as the member and team lists resolve it).</summary>
internal sealed record ProjectRowNames(
    IReadOnlyDictionary<Guid, string?> ClientNames, IReadOnlyDictionary<Guid, string?> OwnerNames)
{
    public static async Task<ProjectRowNames> ResolveAsync(
        IReadOnlyCollection<ProjectRow> rows, IClientDirectory clientDirectory, IUserDirectory userDirectory,
        IHrDirectory hrDirectory, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return new ProjectRowNames(new Dictionary<Guid, string?>(), new Dictionary<Guid, string?>());
        }

        IReadOnlyList<ClientSummary> clients = await clientDirectory.GetManyAsync([.. rows.Select(r => r.ClientId).Distinct()], ct);
        Dictionary<string, string?> loginNames = clients.Count == 0
            ? []
            : (await userDirectory.GetManyAsync([.. clients.Select(c => c.AppUserId).Distinct()], ct))
                .ToDictionary(u => u.Id, u => u.DisplayName);
        var clientNames = clients.ToDictionary(c => c.Id, c => loginNames.GetValueOrDefault(c.AppUserId));

        var ownerNames = (await hrDirectory.GetProfilesAsync([.. rows.Select(r => r.OwnerId).Distinct()], ct))
            .ToDictionary(p => p.Id, p => (string?)p.FullName);

        return new ProjectRowNames(clientNames, ownerNames);
    }
}
