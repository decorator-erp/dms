using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Services;
using SharedKernel;

namespace Hr.Application.Features.Departments.Delete;

internal sealed class DeleteDepartmentCommandValidator : AbstractValidator<DeleteDepartmentCommand>
{
    public DeleteDepartmentCommandValidator() => RuleFor(x => x.DepartmentId).NotEmpty();
}

/// <summary>Soft delete, matching the backend's own <c>DepartmentDomainService.DeleteAsync</c> —
/// <c>IsActive = false</c>, not a row removal.
///
/// <para>K2: deleting also ends every membership in the department and clears its head, in the same
/// transaction, where both used to be left pointing at a department nobody can see. A deleted department
/// is gone to every other read (K3's filter), so deleting it again is 404, not
/// <c>DepartmentAlreadyDeleted</c>; that error is unreachable now (recorded for Phase 5).</para></summary>
internal sealed class DeleteDepartmentCommandHandler(IDepartmentMembershipService memberships)
    : ICommandHandler<DeleteDepartmentCommand>
{
    public Task<Result> HandleAsync(DeleteDepartmentCommand command, CancellationToken ct = default) =>
        memberships.DeactivateDepartmentAsync(command.DepartmentId, ct);
}
