using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Employees;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        var employee = await db.Employees
            .Select(x => new {x.Id, x.IsActive })
            .SingleOrDefaultAsync(x => x.Id == command.EmployeeId, ct);

        if (employee is null)
        {
            return EmployeeErrors.NotFound;
        }

        if (employee.IsActive)
        {
            return EmployeeErrors.AlreadyActive;
        }

        int affectedRows = await db.Employees
            .Where(x => x.Id == command.EmployeeId)
            .ExecuteUpdateAsync(employee => employee.SetProperty(x => x.IsActive, true), ct);

        if (affectedRows < 0)
        { 
            return EmployeeErrors.FailedToActivate;
        }
        
        return Result.Success();
    }
}
