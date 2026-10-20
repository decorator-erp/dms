using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.Update;

internal sealed class UpdateEmployeeCommandValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();

        // N7: the PUT is a full replacement, so the two fields every employee must have are required.
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(200);

        // IsDefined as well as TryParse, as on create: "99" parses to an undefined ContractType, and
        // "0" or "-1" parse too. Before, update accepted all three and stored them.
        RuleFor(x => x.ContractType)
            .NotEmpty()
            .Must(v => Enum.TryParse(v, out ContractType parsed) && Enum.IsDefined(parsed))
            .WithMessage(x => $"'{x.ContractType}' is not a recognized contract type.");
    }
}

/// <summary>N7: replaces the employee's editable fields; a null optional field clears it. An inactive
/// employee is <c>employees.Inactive</c> (409), where it used to be a silent 204.</summary>
internal sealed class UpdateEmployeeCommandHandler(HrDbContext db) : ICommandHandler<UpdateEmployeeCommand>
{
    public async Task<Result> HandleAsync(UpdateEmployeeCommand command, CancellationToken ct = default)
    {
        // Ignores the filter so an inactive employee answers 409, not 404.
        Employee? employee = await db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == command.EmployeeId, ct);
        if (employee is null)
        {
            return EmployeeErrors.NotFound;
        }

        Result updated = employee.Update(
            jobTitle: command.JobTitle,
            contractType: Enum.Parse<ContractType>(command.ContractType),
            phoneNumber: command.PhoneNumber,
            emergencyContactPhone: command.EmergencyContactPhone,
            notes: command.Notes,
            endDate: command.EndDate);
        if (updated.IsFailure)
        {
            return updated;
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
