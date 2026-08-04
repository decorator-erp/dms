using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/StageTeam.cs</c>. <see cref="DepartmentId"/>/
/// <see cref="HeadId"/> are bare Guids → <c>hr.departments</c>/<c>hr.employees</c>, never navigations.
/// <see cref="HeadId"/> is required (matches the backend's constructor guard).</summary>
internal sealed class StageTeam : Entity, IAuditable
{
    private readonly List<StageTeamMember> _members = [];

    public Guid ProjectStageId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid HeadId { get; private set; }
    public IReadOnlyList<StageTeamMember> Members => _members.AsReadOnly();

    private StageTeam()
    {
        // EF materialization.
    }

    public static StageTeam CreateProjectDepartmentTeam(Guid projectStageId, Guid departmentId, Guid headId)
    {
        if (headId == Guid.Empty)
            throw new ArgumentException("A stage team must have a head.", nameof(headId));

        return new StageTeam { ProjectStageId = projectStageId, DepartmentId = departmentId, HeadId = headId };
    }

    public static StageTeam CreateProjectStandaloneTeam(Guid projectStageId, Guid headId)
    {
        if (headId == Guid.Empty)
            throw new ArgumentException("A stage team must have a head.", nameof(headId));

        return new StageTeam { ProjectStageId = projectStageId, HeadId = headId };
    }

    public void ChangeHead(Guid headId)
    {
        if (headId == Guid.Empty)
            throw new ArgumentException("A stage team must have a head.", nameof(headId));

        HeadId = headId;
    }

    public Result<StageTeamMember> AddMember(Guid employeeId, StageTeamMemberRole role)
    {
        if (_members.Any(m => m.EmployeeId == employeeId))
            return TeamErrors.MemberAlreadyExists;

        var member = StageTeamMember.Create(Id, employeeId, role);
        _members.Add(member);
        return member;
    }
}
