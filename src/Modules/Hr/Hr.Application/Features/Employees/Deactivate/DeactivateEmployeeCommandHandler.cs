using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using SharedKernel;

namespace Hr.Application.Features.Employees.Deactivate;

internal sealed class DeactivateEmployeeCommandValidator : AbstractValidator<DeactivateEmployeeCommand>
{
    public DeactivateEmployeeCommandValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

/// <summary>
/// The backend's <c>EmployeeDomainService.DeactivateAsync</c> wrote <c>ApplicationUser.IsActive = false</c>
/// directly through a second repository — a cross-module write into <c>identity.</c> that no longer
/// exists once Identity and HR are separate modules with separate schemas. Deactivating an employee must
/// still disable the login (a security requirement, not a behaviour nicety), so this writes
/// <see cref="Employee.IsActive"/> AND stages <see cref="EmployeeDeactivatedIntegrationEvent"/> through
/// the outbox in the SAME <c>SaveChangesAsync</c> — never a bare synchronous cross-module call, which
/// would be two transactions with no retry: if a direct call to Identity failed after this write
/// committed, the employee would be inactive, the login would still work, and nothing would ever try
/// again. An <c>IIntegrationEventHandler&lt;EmployeeDeactivatedIntegrationEvent&gt;</c> living in
/// <c>Hr.Infrastructure</c> (G2-04, this same task) calls <c>IUserAdministration.DeactivateAsync</c>;
/// <c>OutboxProcessor</c> retries until it succeeds, safe because that call is idempotent. The window
/// between this commit and that handler running is at most one poll interval — strictly better than a
/// permanent silent gap.
/// </summary>
internal sealed class DeactivateEmployeeCommandHandler(HrDbContext db) : ICommandHandler<DeactivateEmployeeCommand>
{
    public async Task<Result> HandleAsync(DeactivateEmployeeCommand command, CancellationToken ct = default)
    {
        Employee? employee = await db.Employees.FindAsync([command.EmployeeId], ct);
        if (employee is null)
        {
            return EmployeeErrors.NotFound;
        }

        if (!employee.IsActive)
        {
            return EmployeeErrors.AlreadyInactive;
        }

        employee.Deactivate();
        db.Outbox().Enqueue(new EmployeeDeactivatedIntegrationEvent(Guid.NewGuid(), employee.Id, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
