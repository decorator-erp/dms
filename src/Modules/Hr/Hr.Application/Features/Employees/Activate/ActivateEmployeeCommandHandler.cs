using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.Activate;

internal sealed class ActivateEmployeeCommandValidator : AbstractValidator<ActivateEmployeeCommand>
{
    public ActivateEmployeeCommandValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

/// <summary>
/// HR-1 (Aug 2.6). Activation is the mirror of deactivation: it flips <see cref="Employee.IsActive"/>
/// through the tracked entity (so the audit interceptor sees it, where the old <c>ExecuteUpdateAsync</c>
/// bypassed it) and stages <see cref="EmployeeActivatedIntegrationEvent"/> in the same save. Its handler
/// re-enables the login. Before this, re-activating an employee left their login disabled for good.
///
/// <para>It is also the re-hire path: creating a new employee for the same person fails on the email,
/// because the login still exists.</para>
/// </summary>
internal sealed class ActivateEmployeeCommandHandler(HrDbContext db) : ICommandHandler<ActivateEmployeeCommand>
{
    public async Task<Result> HandleAsync(ActivateEmployeeCommand command, CancellationToken ct = default)
    {
        // Ignores the filter: the employee being activated is, by definition, an inactive one.
        Employee? employee = await db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == command.EmployeeId, ct);
        if (employee is null)
        {
            return EmployeeErrors.NotFound;
        }

        if (employee.IsActive)
        {
            return EmployeeErrors.AlreadyActive;
        }

        employee.Activate();
        db.Outbox().Enqueue(new EmployeeActivatedIntegrationEvent(Guid.NewGuid(), employee.Id, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
