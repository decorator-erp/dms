using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using SharedKernel;

namespace Hr.Application.Features.Departments.Update;

internal sealed class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

internal sealed class UpdateDepartmentCommandHandler(HrDbContext db) : ICommandHandler<UpdateDepartmentCommand>
{
    public async Task<Result> HandleAsync(UpdateDepartmentCommand command, CancellationToken ct = default)
    {
        var department = await db.Departments.FindAsync([command.DepartmentId], ct);
        if (department is null)
            return DepartmentErrors.NotFound;

        department.Update(command.Name, command.Description);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
