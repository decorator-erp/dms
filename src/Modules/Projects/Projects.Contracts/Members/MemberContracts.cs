using BuildingBlocks.Messaging;

namespace Projects.Contracts.Members;

/// <summary>E1 (D1). Field names follow the frontend's <c>ProjectMember</c> model.
/// <see cref="EmployeeName"/> is null when HR has no profile for the id.</summary>
public sealed record ProjectMemberResponse(
    Guid Id, Guid ProjectId, Guid EmployeeId, ProjectMemberAccess Access, bool CanManage, bool CanExecuteTasks,
    string? EmployeeName, Guid AddedById, DateTime AddedAtUtc);

public sealed record GetProjectMembersQuery(Guid ProjectId, string UserId)
    : IQuery<IReadOnlyList<ProjectMemberResponse>>, IProjectScoped;

public sealed record AddProjectMemberCommand(Guid ProjectId, string UserId, Guid EmployeeId, ProjectMemberAccess Access)
    : ICommand<Guid>, IProjectScoped, IRequiresProjectManager;

public sealed record ChangeProjectMemberAccessCommand(Guid ProjectId, string UserId, Guid MemberId, ProjectMemberAccess Access)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record RemoveProjectMemberCommand(Guid ProjectId, string UserId, Guid MemberId)
    : ICommand, IProjectScoped, IRequiresProjectManager;
