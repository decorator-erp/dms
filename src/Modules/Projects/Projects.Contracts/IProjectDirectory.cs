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
/// <remarks>A-15: <see cref="IsActive"/> is the project's soft-delete flag (<c>SetProjectActiveCommand</c>,
/// <c>DeleteProjectCommand</c>). Lookups still return inactive projects; callers decide what it means.</remarks>
public sealed record ProjectSummary(
    Guid Id, string Code, string Name, ProjectStatus Status, Guid ClientId, Guid OwnerId, bool IsActive);

/// <summary>crm-complete F2/F3: a project's progress and its active stage (the lowest-order Active stage;
/// null when none is active).</summary>
public sealed record ProjectProgressSummary(Guid ProjectId, decimal ProgressPercentage, Guid? ActiveStageId, string? ActiveStageName);

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

    /// <summary>crm-complete F2/F3: progress and active stage for a batch of projects, two statements in all.
    /// Projects that don't exist are absent.</summary>
    Task<IReadOnlyList<ProjectProgressSummary>> GetProgressAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct);

    Task<StageSummary?> GetStageAsync(Guid stageId, CancellationToken ct);
    Task<IReadOnlyList<StageSummary>> GetStagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>crm-complete F3: the open tasks (not completed or cancelled) assigned to any of
    /// <paramref name="employeeIds"/> on active projects, at most <paramref name="max"/>, in project/order.</summary>
    Task<IReadOnlyList<TaskSummary>> GetOpenTasksAssignedToAsync(IReadOnlyCollection<Guid> employeeIds, int max, CancellationToken ct);

    Task<TaskSummary?> GetTaskAsync(Guid taskId, CancellationToken ct);
    Task<IReadOnlyList<TaskSummary>> GetTasksAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
