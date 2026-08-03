using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.Create;

/// <summary>
/// Ported from <c>../backend/Application/Features/Employees/Commands/Create/CreateEmployeeCommand.cs</c>,
/// narrowed per G2-04's own instruction: the backend's version creates the <c>ApplicationUser</c> AND
/// the <c>Employee</c> in one call, spanning what are now two modules' write models — the exact thing
/// this architecture exists to prevent. This command takes an EXISTING <see cref="CreateEmployeeCommand.AppUserId"/>
/// and has no name/email field at all; the two-step flow is <c>Identity.Register</c> then
/// <c>Hr.CreateEmployee</c>, with any single-call UX left to whatever endpoint composes them. Breaking
/// contract change from the backend — noted here and in the final report so the frontend can be updated.
/// </summary>
internal sealed class CreateEmployeeCommandValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeCommandValidator()
    {
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContractType).NotEmpty().Must(v => Enum.TryParse<ContractType>(v, out _))
            .WithMessage(x => $"'{x.ContractType}' is not a recognized contract type.");
    }
}

/// <summary>Validates <see cref="CreateEmployeeCommand.AppUserId"/> via
/// <see cref="IUserDirectory.GetAsync"/> — a contract call, never a join into <c>identity.</c>.</summary>
internal sealed class CreateEmployeeCommandHandler(HrDbContext db, IUserDirectory userDirectory)
    : ICommandHandler<CreateEmployeeCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateEmployeeCommand command, CancellationToken ct = default)
    {
        if (await userDirectory.GetAsync(command.AppUserId, ct) is null)
            return Error.BadRequest("employees.UnknownAppUser", $"No user found for id '{command.AppUserId}'.");

        if (await db.Employees.AnyAsync(e => e.AppUserId == command.AppUserId, ct))
            return Error.Conflict("employees.AlreadyAnEmployee", $"User '{command.AppUserId}' already has an employee record.");

        var employee = Employee.Create(
            appUserId: command.AppUserId,
            jobTitle: command.JobTitle,
            hireDate: command.HireDate,
            contractType: Enum.Parse<ContractType>(command.ContractType),
            phoneNumber: command.PhoneNumber,
            emergencyContactName: command.EmergencyContactName,
            emergencyContactPhone: command.EmergencyContactPhone,
            notes: command.Notes);

        db.Employees.Add(employee);
        db.Outbox().Enqueue(new EmployeeHiredIntegrationEvent(
            Guid.NewGuid(), employee.Id, employee.AppUserId, null, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        return employee.Id;
    }
}
