using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Hr.Infrastructure.Services;

/// <inheritdoc cref="IDepartmentMembershipService"/>
internal sealed class DepartmentMembershipService(HrDbContext db) : IDepartmentMembershipService
{
    public async Task<Result> AssignAsync(Guid employeeId, Guid departmentId, CancellationToken ct)
    {
        Result employee = await EnsureActiveEmployeeAsync(employeeId, ct);
        if (employee.IsFailure)
        {
            return employee;
        }

        if (!await db.Departments.AnyAsync(d => d.Id == departmentId, ct))
        {
            return DepartmentErrors.NotFound;
        }

        List<EmployeeDepartment> memberships = await MembershipsOfAsync(employeeId, ct);
        EmployeeDepartment? active = memberships.SingleOrDefault(m => m.IsActive);
        if (active is not null)
        {
            return active.DepartmentId == departmentId
                ? DepartmentErrors.AlreadyInDepartment
                : EmployeeErrors.AlreadyInAnotherDepartment(active.DepartmentId);
        }

        Join(memberships, employeeId, departmentId);
        return await SaveAsync(ct);
    }

    public async Task<Result> MoveAsync(Guid employeeId, Guid newDepartmentId, CancellationToken ct)
    {
        Result employee = await EnsureActiveEmployeeAsync(employeeId, ct);
        if (employee.IsFailure)
        {
            return employee;
        }

        if (!await db.Departments.AnyAsync(d => d.Id == newDepartmentId, ct))
        {
            return DepartmentErrors.NotFound;
        }

        List<EmployeeDepartment> memberships = await MembershipsOfAsync(employeeId, ct);
        EmployeeDepartment? active = memberships.SingleOrDefault(m => m.IsActive);
        if (active?.DepartmentId == newDepartmentId)
        {
            return DepartmentErrors.AlreadyInDepartment;
        }

        if (active is not null)
        {
            if (await HeadsAsync(employeeId, active.DepartmentId, ct))
            {
                return DepartmentErrors.RemoveHeadFirst;
            }

            // Leave first and save, so the one-active-membership index never sees two active rows.
            // The command's transaction makes leave + join one change.
            active.Remove();
            await db.SaveChangesAsync(ct);
        }

        Join(memberships, employeeId, newDepartmentId);
        return await SaveAsync(ct);
    }

    public async Task<Result> RemoveAsync(Guid employeeId, Guid departmentId, CancellationToken ct)
    {
        EmployeeDepartment? membership = await db.EmployeeDepartments.FirstOrDefaultAsync(
            m => m.EmployeeId == employeeId && m.DepartmentId == departmentId && m.IsActive, ct);
        if (membership is null)
        {
            return DepartmentErrors.UserNotInDepartment;
        }

        if (await HeadsAsync(employeeId, departmentId, ct))
        {
            return DepartmentErrors.RemoveHeadFirst;
        }

        membership.Remove();
        return await SaveAsync(ct);
    }

    public async Task<Result> AssignHeadAsync(Guid departmentId, Guid employeeId, CancellationToken ct)
    {
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (department is null)
        {
            return DepartmentErrors.NotFound;
        }

        if (department.DepartmentHeadId.HasValue)
        {
            return DepartmentErrors.AlreadyHaveHead;
        }

        Result employee = await EnsureActiveEmployeeAsync(employeeId, ct);
        if (employee.IsFailure)
        {
            return employee;
        }

        // A head is a member of the department they head: join it if they aren't in it, refuse if they
        // are in another one (heading this department is a move, not a second membership).
        List<EmployeeDepartment> memberships = await MembershipsOfAsync(employeeId, ct);
        EmployeeDepartment? active = memberships.SingleOrDefault(m => m.IsActive);
        if (active is not null && active.DepartmentId != departmentId)
        {
            return EmployeeErrors.AlreadyInAnotherDepartment(active.DepartmentId);
        }

        if (active is null)
        {
            Join(memberships, employeeId, departmentId);
        }

        department.AssignHead(employeeId);
        return await SaveAsync(ct);
    }

    public async Task<Result> UnassignHeadAsync(Guid departmentId, CancellationToken ct)
    {
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (department is null)
        {
            return DepartmentErrors.NotFound;
        }

        if (!department.DepartmentHeadId.HasValue)
        {
            return DepartmentErrors.DepartmentHeadNotAssign;
        }

        department.RemoveHead();
        return await SaveAsync(ct);
    }

    public async Task<Result> DeactivateEmployeeAsync(Employee employee, CancellationToken ct)
    {
        employee.Deactivate();

        List<Department> headed = await db.Departments.Where(d => d.DepartmentHeadId == employee.Id).ToListAsync(ct);
        foreach (Department department in headed)
        {
            department.RemoveHead();
        }

        return await SaveAsync(ct);
    }

    public async Task<Result> DeactivateDepartmentAsync(Guid departmentId, CancellationToken ct)
    {
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (department is null)
        {
            return DepartmentErrors.NotFound;
        }

        List<EmployeeDepartment> memberships = await db.EmployeeDepartments
            .Where(m => m.DepartmentId == departmentId && m.IsActive)
            .ToListAsync(ct);
        foreach (EmployeeDepartment membership in memberships)
        {
            membership.Remove();
        }

        department.RemoveHead();
        department.Deactivate();
        return await SaveAsync(ct);
    }

    /// <summary>NotFound for no such employee; <c>employees.Inactive</c> for a deactivated one. The filter
    /// is ignored so the two can be told apart.</summary>
    private async Task<Result> EnsureActiveEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        bool? isActive = await db.Employees
            .IgnoreQueryFilters()
            .Where(e => e.Id == employeeId)
            .Select(e => (bool?)e.IsActive)
            .FirstOrDefaultAsync(ct);

        return isActive switch
        {
            null => EmployeeErrors.NotFound,
            false => EmployeeErrors.Inactive,
            true => Result.Success(),
        };
    }

    private Task<List<EmployeeDepartment>> MembershipsOfAsync(Guid employeeId, CancellationToken ct) =>
        db.EmployeeDepartments.Where(m => m.EmployeeId == employeeId).ToListAsync(ct);

    private Task<bool> HeadsAsync(Guid employeeId, Guid departmentId, CancellationToken ct) =>
        db.Departments.AnyAsync(d => d.Id == departmentId && d.DepartmentHeadId == employeeId, ct);

    /// <summary>Rejoins through the pair's existing row if there is one; the composite key allows one.</summary>
    private void Join(List<EmployeeDepartment> memberships, Guid employeeId, Guid departmentId)
    {
        EmployeeDepartment? former = memberships.SingleOrDefault(m => m.DepartmentId == departmentId);
        if (former is not null)
        {
            former.Restore(DateTime.UtcNow);
        }
        else
        {
            db.EmployeeDepartments.Add(EmployeeDepartment.Create(employeeId, departmentId));
        }
    }

    /// <summary>
    /// A6: two assignments of one employee can both pass the "not in a department" check, and the second
    /// then hits the one-active-membership index. That is the same refusal the check gives, so it gets
    /// the same 409 rather than a 500. The current department can't be named here: the failed statement
    /// aborted the transaction.
    /// </summary>
    private async Task<Result> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EmployeeDepartmentConfiguration.ActiveMembershipIndex,
        })
        {
            return EmployeeErrors.AlreadyInAnotherDepartment(null);
        }
    }
}
