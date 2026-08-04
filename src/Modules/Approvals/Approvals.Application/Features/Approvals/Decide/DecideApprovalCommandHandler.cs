using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Domain = Approvals.Domain;

namespace Approvals.Application.Features.Approvals.Decide;

internal sealed class DecideApprovalCommandValidator : AbstractValidator<DecideApprovalCommand>
{
    public DecideApprovalCommandValidator()
    {
        RuleFor(x => x.ApprovalId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000);
        RuleFor(x => x.Comment)
            .NotEmpty()
            .WithMessage("A comment is required for this decision.")
            .When(x => x.Decision is ApprovalDecision.Rejected or ApprovalDecision.ChangesRequested);
    }
}

/// <summary>
/// Ported semantics from the backend's <c>ApprovalDomainService.DecideAsync</c> (single assigned decider,
/// one-shot, no quorum) generalized per decision #1 (a set of eligible approvers) and #2/R-B
/// (<c>ApprovalsPermissions.DecideAny</c> replaces the backend's cross-module <c>isAdminOverride</c>).
/// Publishes <see cref="ApprovalDecidedIntegrationEvent"/> directly in this handler's own
/// <c>SaveChangesAsync</c> — not via the raised <c>ApprovalDecidedDomainEvent</c>'s automatic post-return
/// dispatch (<c>DomainEventDispatchDecorator</c> runs AFTER this handler returns and already committed,
/// so anything staged there would be a second, separate transaction — see G4-02's entity for why the
/// domain event still exists with no handler today: available for a future intra-module reaction, R2's
/// zero-handler no-op applies). Same direct-enqueue shape as <c>DeactivateEmployeeCommandHandler</c>
/// (G2-04, HR).
/// </summary>
internal sealed class DecideApprovalCommandHandler(ApprovalsDbContext db, ICurrentUser currentUser)
    : ICommandHandler<DecideApprovalCommand>
{
    public async Task<Result> HandleAsync(DecideApprovalCommand command, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
            return Domain.ApprovalErrors.Unauthenticated;

        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == command.ApprovalId, ct);
        if (approval is null)
            return Domain.ApprovalErrors.NotFound;

        var isOverride = currentUser.HasPermission(ApprovalsPermissions.DecideAny);
        var decision = ApprovalEnumMapping.ToDomain(command.Decision);

        var result = approval.Decide(currentUser.UserId, decision, command.Comment, isOverride);
        if (result.IsFailure)
            return result;

        db.Outbox().Enqueue(new ApprovalDecidedIntegrationEvent(
            Guid.NewGuid(), approval.Id, ApprovalEnumMapping.ToContracts(approval.SubjectType), approval.SubjectId,
            command.Decision, currentUser.UserId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
