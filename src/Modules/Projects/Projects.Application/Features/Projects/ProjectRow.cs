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
    /// <summary>P1-A: <c>MyLevel</c> is <paramref name="myAccess"/>'s level.</summary>
    public ProjectResponse ToResponse(ProjectAccessResponse myAccess) =>
        new(Id, Code, Name, Description, Status, WorkflowType, Address, Notes, ProgressPercentage,
            EstimatedStartDate, EstimatedEndDate, ActualStartDate, ActualEndDate,
            ClientId, OwnerId, IsActive, StageCount, myAccess.Level, myAccess);
}
