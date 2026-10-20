using SharedKernel;

namespace Hr.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/Department.cs</c>. No <c>DepartmentHead</c>
/// navigation property (unlike the backend) — <see cref="DepartmentHeadId"/> is a bare
/// <see cref="Guid"/> reference to another <see cref="Employee"/> row in this same module; Application
/// handlers join through <c>HrDbContext</c> directly when they need the head's own fields, rather than
/// the domain model exposing a lazy-loadable navigation for a case only a couple of queries need.</summary>
internal sealed class Department : Entity, IAuditable
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? DepartmentHeadId { get; private set; }

    private Department()
    {
        // EF materialization.
    }

    public static Department Create(string name, string? description = null) =>
        new() { Name = name, Description = description };

    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public void AssignHead(Guid employeeId) => DepartmentHeadId = employeeId;

    public void RemoveHead() => DepartmentHeadId = null;

    public void Deactivate() => SetActive(false);
}
