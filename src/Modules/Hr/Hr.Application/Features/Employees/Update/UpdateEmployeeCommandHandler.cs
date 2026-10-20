using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
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

        // Aug 3.6: the same widths as create (and the columns); longer used to reach the database.
        RuleFor(x => x.PhoneNumber).MaximumLength(50);
        RuleFor(x => x.EmergencyContactPhone).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(2000);

        // N7: the login's name, both or neither. Identity trims and checks it again (1..100).
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).When(x => x.LastName is not null);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100).When(x => x.FirstName is not null);
    }
}

/// <summary>N7: replaces the employee's editable fields; a null optional field clears it. An inactive
/// employee is <c>employees.Inactive</c> (409), where it used to be a silent 204.
///
/// <para><b>The name lives in Identity.</b> When the request carries one, it is applied through
/// <see cref="IUserAdministration.UpdateNameAsync"/> only after HR's own checks and save have succeeded.
/// That is a synchronous cross-module write, as N7 decided. If Identity refuses, the error is returned
/// and the command's transaction rolls HR's change back. The one window left is Identity committing and
/// then HR's commit failing; the name call is idempotent, so retrying the PUT repairs it.</para></summary>
internal sealed class UpdateEmployeeCommandHandler(HrDbContext db, IUserAdministration userAdministration)
    : ICommandHandler<UpdateEmployeeCommand>
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

        return command is { FirstName: { } first, LastName: { } last }
            ? await userAdministration.UpdateNameAsync(employee.AppUserId, first, last, ct)
            : Result.Success();
    }
}
