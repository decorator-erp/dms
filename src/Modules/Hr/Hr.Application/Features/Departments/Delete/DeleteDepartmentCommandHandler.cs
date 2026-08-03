using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using SharedKernel;

namespace Hr.Application.Features.Departments.Delete;

internal sealed class DeleteDepartmentCommandValidator : AbstractValidator<DeleteDepartmentCommand>
{
    public DeleteDepartmentCommandValidator() => RuleFor(x => x.DepartmentId).NotEmpty();
}

/// <summary>Soft delete, matching the backend's own <c>DepartmentDomainService.DeleteAsync</c> —
/// <c>IsActive = false</c>, not a row removal.</summary>
internal sealed class DeleteDepartmentCommandHandler(HrDbContext db) : ICommandHandler<DeleteDepartmentCommand>
{
    public async Task<Result> HandleAsync(DeleteDepartmentCommand command, CancellationToken ct = default)
    {
        var department = await db.Departments.FindAsync([command.DepartmentId], ct);
        if (department is null)
            return DepartmentErrors.NotFound;

        department.Deactivate();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
