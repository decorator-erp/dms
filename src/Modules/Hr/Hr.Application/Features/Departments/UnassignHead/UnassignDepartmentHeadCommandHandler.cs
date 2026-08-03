using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using SharedKernel;

namespace Hr.Application.Features.Departments.UnassignHead;

internal sealed class UnassignDepartmentHeadCommandValidator : AbstractValidator<UnassignDepartmentHeadCommand>
{
    public UnassignDepartmentHeadCommandValidator() => RuleFor(x => x.DepartmentId).NotEmpty();
}

internal sealed class UnassignDepartmentHeadCommandHandler(HrDbContext db) : ICommandHandler<UnassignDepartmentHeadCommand>
{
    public async Task<Result> HandleAsync(UnassignDepartmentHeadCommand command, CancellationToken ct = default)
    {
        var department = await db.Departments.FindAsync([command.DepartmentId], ct);
        if (department is null)
            return DepartmentErrors.NotFound;

        if (!department.DepartmentHeadId.HasValue)
            return DepartmentErrors.DepartmentHeadNotAssign;

        department.RemoveHead();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
