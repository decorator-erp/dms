using SharedKernel;

namespace Projects.Domain;

/// <summary>E1 (D1): an employee named on a project with an access level. Every member counts toward
/// project scope (<c>ProjectAccessGuard.AccessibleProjectIds</c>). <see cref="EmployeeId"/> and
/// <see cref="AddedById"/> are bare Guids → <c>hr.employees</c>, never navigations. One row per
/// (project, employee); optimistic concurrency on <c>xmin</c>.</summary>
internal sealed class ProjectMember : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public ProjectMemberAccess Access { get; private set; }
    public Guid AddedById { get; private set; }
    public DateTime AddedAtUtc { get; private set; }

    public bool CanManage => Access == ProjectMemberAccess.Manager;

    public bool CanExecuteTasks => Access is ProjectMemberAccess.Editor or ProjectMemberAccess.Manager;

    private ProjectMember()
    {
        // EF materialization.
    }

    public static ProjectMember Create(Guid projectId, Guid employeeId, ProjectMemberAccess access, Guid addedById) =>
        new()
        {
            ProjectId = projectId,
            EmployeeId = employeeId,
            Access = access,
            AddedById = addedById,
            AddedAtUtc = DateTime.UtcNow,
        };

    public void ChangeAccess(ProjectMemberAccess access) => Access = access;
}
