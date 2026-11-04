using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using Approvals.Infrastructure.Persistence;
using Approvals.Infrastructure.Services;
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
/// dispatch (<c>DomainEventDispatchDecorator</c> dispatches only AFTER this handler returns — inside the
/// decorator's transaction since G5-08, but in a later save of its own — see G4-02's entity for why the
/// domain event still exists with no handler today: available for a future intra-module reaction, R2's
/// zero-handler no-op applies). Same direct-enqueue shape as <c>DeactivateEmployeeCommandHandler</c>
/// (G2-04, HR).
///
/// <para>FINDING-P1-2: decided under <see cref="ApprovalLocks.DecideLockedAsync"/> — the row lock the portal's
/// <c>ApprovalDecider</c> takes — inside the transaction <c>DomainEventDispatchDecorator</c> opens around every
/// command (this handler opens none of its own; the decorator commits on success and rolls back on a failure
/// result). A concurrent portal decision or second override waits, then sees the committed decision and gets
/// <c>AlreadyDecided</c> (409) instead of overwriting it; a lost race that surfaces as an error instead
/// (serialization failure, deadlock, concurrency exception) maps to the same 409 (FINDING-P1-14).</para>
/// </summary>
internal sealed class DecideApprovalCommandHandler(ApprovalsDbContext db, ICurrentUser currentUser)
    : ICommandHandler<DecideApprovalCommand>
{
    public async Task<Result> HandleAsync(DecideApprovalCommand command, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
        {
            return Domain.ApprovalErrors.Unauthenticated;
        }

        string userId = currentUser.UserId;
        return await ApprovalLocks.DecideLockedAsync(db, command.ApprovalId, () => DecideAsync(command, userId, ct), ct);
    }

    private async Task<Result> DecideAsync(DecideApprovalCommand command, string userId, CancellationToken ct)
    {
        Domain.Approval? approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == command.ApprovalId, ct);
        if (approval is null)
        {
            return Domain.ApprovalErrors.NotFound;
        }

        bool isOverride = currentUser.HasPermission(ApprovalsPermissions.DecideAny);
        Domain.ApprovalDecision decision = ApprovalEnumMapping.ToDomain(command.Decision);

        Result result = approval.Decide(userId, decision, command.Comment, isOverride);
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ApprovalDecidedIntegrationEvent(
            Guid.NewGuid(), approval.Id, ApprovalEnumMapping.ToContracts(approval.SubjectType), approval.SubjectId,
            command.Decision, userId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
