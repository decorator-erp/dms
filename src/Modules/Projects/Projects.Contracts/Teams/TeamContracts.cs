using BuildingBlocks.Messaging;

namespace Projects.Contracts.Teams;

public sealed record StageTeamResponse(Guid Id, Guid ProjectStageId, Guid? DepartmentId, Guid HeadId);

public sealed record StageTeamMemberResponse(Guid Id, Guid StageTeamId, Guid EmployeeId, StageTeamMemberRole Role);

public sealed record AddStageTeamMemberCommand(
    Guid ProjectId, Guid StageTeamId, string UserId, Guid EmployeeId, StageTeamMemberRole Role)
    : ICommand<Guid>, IProjectScoped;

public sealed record ChangeStageTeamMemberRoleCommand(
    Guid ProjectId, Guid StageTeamMemberId, string UserId, StageTeamMemberRole Role)
    : ICommand, IProjectScoped;

public sealed record ChangeStageTeamHeadCommand(Guid ProjectId, Guid StageTeamId, string UserId, Guid HeadId)
    : ICommand, IProjectScoped;

public sealed record GetStageTeamQuery(Guid ProjectId, Guid StageTeamId, string UserId)
    : IQuery<StageTeamResponse>, IProjectScoped;

public sealed record GetStageTeamMembersQuery(Guid ProjectId, Guid StageTeamId, string UserId)
    : IQuery<IReadOnlyList<StageTeamMemberResponse>>, IProjectScoped;

/// <summary>E7: one row of the project's team list. <see cref="HeadName"/> is null when HR has no profile.</summary>
public sealed record ProjectTeamListItemResponse(
    Guid Id, Guid ProjectStageId, string StageName, int StageOrder, Guid? DepartmentId, Guid HeadId, string? HeadName,
    int MemberCount);

public sealed record GetProjectTeamsQuery(Guid ProjectId, string UserId, Guid? StageId = null)
    : IQuery<IReadOnlyList<ProjectTeamListItemResponse>>, IProjectScoped;

/// <summary>E7: a team made by hand on one of the project's stages — a department's team (headed by the
/// department head) or a standalone team with the given head. Exactly one of the two.</summary>
public sealed record CreateStageTeamCommand(Guid ProjectId, string UserId, Guid ProjectStageId, Guid? DepartmentId, Guid? HeadId)
    : ICommand<Guid>, IProjectScoped;

public sealed record RemoveStageTeamMemberCommand(Guid ProjectId, Guid StageTeamMemberId, string UserId)
    : ICommand, IProjectScoped;
