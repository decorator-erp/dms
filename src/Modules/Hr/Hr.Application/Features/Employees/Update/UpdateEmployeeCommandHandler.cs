using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using SharedKernel;

namespace Hr.Application.Features.Employees.Update;

internal sealed class UpdateEmployeeCommandValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.JobTitle).MaximumLength(200).When(x => x.JobTitle is not null);
        RuleFor(x => x.ContractType)
            .Must(v => v is null || Enum.TryParse<ContractType>(v, out _))
            .WithMessage(x => $"'{x.ContractType}' is not a recognized contract type.");
    }
}

internal sealed class UpdateEmployeeCommandHandler(HrDbContext db) : ICommandHandler<UpdateEmployeeCommand>
{
    public async Task<Result> HandleAsync(UpdateEmployeeCommand command, CancellationToken ct = default)
    {
        Employee? employee = await db.Employees.FindAsync([command.EmployeeId], ct);
        if (employee is null)
        {
            return EmployeeErrors.NotFound;
        }

        employee.Update(
            jobTitle: command.JobTitle,
            contractType: command.ContractType is null ? null : Enum.Parse<ContractType>(command.ContractType),
            phoneNumber: command.PhoneNumber,
            emergencyContactPhone: command.EmergencyContactPhone,
            notes: command.Notes,
            endDate: command.EndDate);

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
