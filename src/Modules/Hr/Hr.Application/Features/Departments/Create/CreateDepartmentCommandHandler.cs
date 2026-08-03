using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.Create;

internal sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

internal sealed class CreateDepartmentCommandHandler(HrDbContext db) : ICommandHandler<CreateDepartmentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateDepartmentCommand command, CancellationToken ct = default)
    {
        if (await db.Departments.AnyAsync(d => d.Name == command.Name, ct))
            return DepartmentErrors.DuplicatedName(command.Name);

        var department = Department.Create(command.Name, command.Description);
        db.Departments.Add(department);
        await db.SaveChangesAsync(ct);

        return department.Id;
    }
}
