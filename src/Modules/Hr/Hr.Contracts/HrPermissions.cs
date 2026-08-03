namespace Hr.Contracts;

/// <summary>Ported verbatim from <c>../backend/Domain/Constants/DefaultPermissions.cs</c>'s
/// Employees/Departments entries — the actual strings already in use, not renamed or renumbered on the
/// way in.</summary>
public static class HrPermissions
{
    public const string EmployeesRead = "employees.read";
    public const string EmployeesViewOwn = "employees.view_own";
    public const string EmployeesUpdate = "employees.update";
    public const string EmployeesSetActive = "employees.set_active";
    public const string EmployeesCreate = "employees.create";
    public const string EmployeesDelete = "employees.delete";

    public const string DepartmentsRead = "departments.read";
    public const string DepartmentsViewUsers = "departments.view_users";
    public const string DepartmentsUpdate = "departments.update";
    public const string DepartmentsSetActive = "departments.set_active";
    public const string DepartmentsCreate = "departments.create";
    public const string DepartmentsDelete = "departments.delete";
    public const string DepartmentsAssignDepartmentHead = "departments.assign_department_head";
    public const string DepartmentsUnassignDepartmentHead = "departments.unassign_department_head";
    public const string DepartmentsAssignToUser = "departments.assign_to_user";
    public const string DepartmentsRemoveFromUser = "departments.remove_from_user";
    public const string DepartmentsMoveUser = "departments.move_user";
}
