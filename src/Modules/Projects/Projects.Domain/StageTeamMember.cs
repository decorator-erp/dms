using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/StageTeamMember.cs</c>. <see cref="EmployeeId"/> is
/// a bare Guid → <c>hr.employees</c>, never a navigation.</summary>
internal sealed class StageTeamMember : Entity, IAuditable
{
    public Guid StageTeamId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public StageTeamMemberRole Role { get; private set; }

    public bool CanExecuteTasks => Role is StageTeamMemberRole.Responsible or StageTeamMemberRole.Contributor;

    private StageTeamMember()
    {
        // EF materialization.
    }

    public static StageTeamMember Create(Guid stageTeamId, Guid employeeId, StageTeamMemberRole role) =>
        new() { StageTeamId = stageTeamId, EmployeeId = employeeId, Role = role };

    public void ChangeRole(StageTeamMemberRole role) => Role = role;
}
