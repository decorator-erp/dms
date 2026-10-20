using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.Create;

internal sealed class CreateEmployeeCommandValidator : AbstractValidator<CreateEmployeeCommand>
{
    /// <summary>Hire dates are allowed a year ahead (a signed contract starting next quarter is normal)
    /// and fifty years back. Before this there was no rule at all, so year 0001 was accepted.</summary>
    private static readonly DateOnly EarliestHireDate = new(DateTime.UtcNow.Year - 50, 1, 1);

    public CreateEmployeeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);

        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);

        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(200);

        RuleFor(x => x.ContractType).NotEmpty()
            .Must(v => Enum.TryParse(v, out ContractType parsed) && Enum.IsDefined(parsed))
            .WithMessage(x => $"'{x.ContractType}' is not a recognized contract type.");

        RuleFor(x => x.HireDate)
            .InclusiveBetween(EarliestHireDate, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)));

        RuleFor(x => x.PhoneNumber).MaximumLength(50);
        RuleFor(x => x.EmergencyContactPhone).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class CreateEmployeeCommandHandler(HrDbContext db, IUserProvisioning provisioning)
    : ICommandHandler<CreateEmployeeCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateEmployeeCommand command, CancellationToken ct = default)
    {
        Result<string> userResult = await provisioning.CreateAsync(
            new NewUserRequest(
                command.Email,
                command.Password,
                command.FirstName,
                command.LastName,
                command.PhoneNumber,
                UserKind.Employee,
                IdentityRoles.Employee),
            ct);

        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        string appUserId = userResult.Value!;

        // IgnoreQueryFilters: a login already linked to a DEACTIVATED employee is still taken. (In practice
        // provisioning has just created this login, so this is defensive; re-hiring someone is Activate,
        // since their login, and so their email, still exists.)
        if (await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.AppUserId == appUserId, ct))
        {
            return EmployeeErrors.InvalidUserId;
        }

        var employee = Employee.Create(
            appUserId: appUserId,
            jobTitle: command.JobTitle,
            hireDate: command.HireDate,
            contractType: Enum.Parse<ContractType>(command.ContractType),
            phoneNumber: command.PhoneNumber,
            emergencyContactPhone: command.EmergencyContactPhone,
            notes: command.Notes);

        db.Employees.Add(employee);
        db.Outbox().Enqueue(new EmployeeHiredIntegrationEvent(
            Guid.NewGuid(), employee.Id, appUserId, null, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return employee.Id;
    }
}
