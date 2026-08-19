namespace Projects.Contracts;

/// <summary>
/// <para><see cref="OwnerId"/> added at F1-00b: a bare Guid → <c>hr.employees</c>, the same value
/// <c>Project.OwnerId</c> holds. Finance's <c>InvoiceDueJob</c> needs the project owner as a
/// notification recipient and cannot read <c>projects.projects</c> to get it; every other module's job
/// resolves its recipients from its own tables, which is precisely what a job living in Finance cannot
/// do for a Projects-owned field. Publishing the id here — rather than growing a bespoke read
/// interface — keeps the resolution on the existing directory surface, and the id is already published
/// in spirit: <c>ProjectDeadlineJob</c> reads the same column and resolves it the same way, through
/// <c>IHrDirectory.GetAsync(OwnerId).AppUserId</c>.</para>
/// <para>Required, not defaulted. A defaulted <c>Guid</c> would let a caller construct a summary whose
/// owner is <c>Guid.Empty</c>, which resolves to no employee and therefore silently to no recipient —
/// the failure mode being avoided. One construction site exists and it is updated in the same
/// commit.</para>
/// </summary>
public sealed record ProjectSummary(
    Guid Id, string Code, string Name, ProjectStatus Status, Guid ClientId, Guid OwnerId);

public sealed record StageSummary(Guid Id, Guid ProjectId, string Name, StageStatus Status);

public sealed record TaskSummary(
    Guid Id, Guid ProjectId, Guid? ProjectStageId, string Name, ProjectTaskStatus Status, Guid? AssignedToId);

/// <summary>Cross-module read surface for Projects-owned data. Every lookup has a batch sibling — no
/// exceptions, per <c>docs/adding-a-module.md</c>'s Contracts rule.</summary>
public interface IProjectDirectory
{
    Task<ProjectSummary?> GetProjectAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>M1-02: added to close the gap G9-14 recorded — <c>GetMyPortalMeetingsQuery</c>'s own
    /// doc comment says it resolves "the calling client's own projects," but until now
    /// <see cref="IProjectDirectory"/> had no client-scoped lookup, only by-id. This is the shape the
    /// Scope-boundaries table already named as natural.</summary>
    Task<IReadOnlyList<ProjectSummary>> GetProjectsByClientAsync(Guid clientId, CancellationToken ct);

    Task<IReadOnlyList<ProjectSummary>> GetProjectsByClientsAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken ct);

    Task<StageSummary?> GetStageAsync(Guid stageId, CancellationToken ct);
    Task<IReadOnlyList<StageSummary>> GetStagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<TaskSummary?> GetTaskAsync(Guid taskId, CancellationToken ct);
    Task<IReadOnlyList<TaskSummary>> GetTasksAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
