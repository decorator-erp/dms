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
