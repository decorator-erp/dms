using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using SharedKernel;

namespace Hr.Application.Features.Employees.Activate;

internal sealed class ActivateEmployeeCommandValidator : AbstractValidator<ActivateEmployeeCommand>
{
    public ActivateEmployeeCommandValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

internal sealed class ActivateEmployeeCommandHandler(HrDbContext db) : ICommandHandler<ActivateEmployeeCommand>
{
    public async Task<Result> HandleAsync(ActivateEmployeeCommand command, CancellationToken ct = default)
    {
        var employee = await db.Employees.FindAsync([command.EmployeeId], ct);
        if (employee is null)
            return EmployeeErrors.NotFound;

        if (employee.IsActive)
            return EmployeeErrors.AlreadyActive;

        employee.Activate();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
