using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using Finance.Application.Common;
using Finance.Contracts.Variations;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.Variations;

// ═══════════════════════════════════════════════════════════════════════════════
// Eleven features: seven state transitions and four reads.
//
// THE STRUCTURAL POINT OF THIS FILE. The backend's VariationDomainService does most of its work inside
// Projects' tables: raising a variation creates or re-links a Budget gate on the owning task or stage
// and stamps GateCondition.VariationId; approving it satisfies that gate and runs the unlock cascade;
// rejecting or cancelling marks it Rejected while deliberately leaving the owner Locked.
//
// Every one of those is a cross-module WRITE. Here they leave as integration events and Projects'
// own handlers do the writing -- which is why F1-00 added GateCondition.VariationId as a plain column
// with no foreign key, and why Finance never references a gate type.
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class RaiseVariationCommandValidator : AbstractValidator<RaiseVariationCommand>
{
    public RaiseVariationCommandValidator()
    {
        RuleFor(x => x.Request.ProjectId).NotEmpty();
        RuleFor(x => x.RequestedById).NotEmpty();
        RuleFor(x => x.Request.Reason).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Request.Description).MaximumLength(512);

        // The two kinds populate disjoint halves of the request. Validated here as well as in
        // Variation.Create so the caller gets a field-level message rather than one domain error for
        // three different mistakes; the entity stays the backstop for every other caller.
        When(x => x.Request.Kind == Finance.Contracts.VariationKind.ClientDifference, () =>
        {
            RuleFor(x => x.Request.ProjectTaskId).NotEmpty();
            RuleFor(x => x.Request.BudgetedAmount).NotNull().GreaterThanOrEqualTo(0);
            RuleFor(x => x.Request.ChosenAmount).NotNull();
        });

        When(x => x.Request.Kind == Finance.Contracts.VariationKind.InternalChange, () =>
        {
            RuleFor(x => x.Request.BudgetLineId).NotEmpty();
            RuleFor(x => x.Request.Amount).NotNull().GreaterThan(0);
        });
    }
}

/// <summary>
/// Raises a variation in <c>Draft</c> and announces it, so Projects can open or re-link the Budget gate
/// that this variation will eventually unlock.
///
/// <para><b>The gate owner is resolved here, not by the consumer</b>, and the rule is the backend's:
/// a task-scoped variation owns a task gate, and only a variation with no task owns a stage gate. That
/// mirrors <c>GateCondition</c>'s own XOR-owner invariant, so a consumer receiving both ids would have
/// to re-derive the same precedence and could get it wrong differently.</para>
///
/// <para><b>A variation with neither a task nor a stage announces nothing.</b> There is no gate for it
/// to unlock — it is a project-level budget change — and the backend skips the gate linkage in exactly
/// that case.</para>
/// </summary>
internal sealed class RaiseVariationCommandHandler(FinanceDbContext db, IProjectDirectory projects)
    : ICommandHandler<RaiseVariationCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RaiseVariationCommand command, CancellationToken ct = default)
    {
        var r = command.Request;

        if (await projects.GetProjectAsync(r.ProjectId, ct) is null)
            return VariationErrors.ProjectNotFound;

        if (r.BudgetLineId is Guid budgetLineId
            && !await db.BudgetLines.AnyAsync(b => b.Id == budgetLineId && b.IsActive, ct))
            return BudgetLineErrors.NotFound;

        var created = Variation.Create(
            FinanceEnumMapping.ToDomain(r.Kind), r.ProjectId, r.Reason, command.RequestedById,
            r.ProjectTaskId, r.BudgetLineId, r.BudgetedAmount, r.ChosenAmount, r.Amount,
            r.Description, r.ProjectStageId);

        if (created.IsFailure)
            return created.Error;

        var variation = created.Value!;
        db.Variations.Add(variation);

        // Task wins over stage, matching the backend's `stageScope = projectTaskId is null ? stageId : null`.
        var stageScope = variation.ProjectTaskId is null ? variation.ProjectStageId : null;

        if (variation.ProjectTaskId is not null || stageScope is not null)
        {
            db.Outbox().Enqueue(new Finance.Contracts.VariationRaisedIntegrationEvent(
                Guid.NewGuid(), variation.Id, variation.ProjectId, stageScope, variation.ProjectTaskId,
                DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync(ct);
        return variation.Id;
    }
}

internal sealed class SubmitVariationCommandValidator : AbstractValidator<SubmitVariationCommand>
{
    public SubmitVariationCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>Draft → Submitted. A client difference is stamped <c>SentToClientAt</c> by the entity; an
/// internal change never leaves the office and is not.</summary>
internal sealed class SubmitVariationCommandHandler(FinanceDbContext db)
    : ICommandHandler<SubmitVariationCommand>
{
    public async Task<Result> HandleAsync(SubmitVariationCommand command, CancellationToken ct = default)
    {
        var variation = await db.Variations.FirstOrDefaultAsync(v => v.Id == command.Id && v.IsActive, ct);
        if (variation is null)
            return VariationErrors.NotFound;

        var submitted = variation.Submit();
        if (submitted.IsFailure)
            return submitted.Error;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class ApproveVariationCommandValidator : AbstractValidator<ApproveVariationCommand>
{
    public ApproveVariationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ApprovedById).NotEmpty();
        RuleFor(x => x.SignedDocumentPath).MaximumLength(512);
    }
}

/// <summary>
/// Submitted → Approved, and for a client difference that approval <b>is</b> the signature.
///
/// <para><b>Approving publishes the event that satisfies the linked gate</b>, which on the Projects side
/// runs the ordinary unlock cascade. Finance does not know a gate exists; it announces that the
/// variation was approved and by whom.</para>
///
/// <para><b>Coverage is re-decided here too, and that is not redundant with the posting path.</b>
/// Approving a client difference changes the signed-difference sum, which is one of the two inputs to
/// task coverage — so a task that went over budget before the difference was signed becomes covered at
/// this moment, with no ledger posting involved to trigger it.</para>
/// </summary>
internal sealed class ApproveVariationCommandHandler(FinanceDbContext db)
    : ICommandHandler<ApproveVariationCommand>
{
    public async Task<Result> HandleAsync(ApproveVariationCommand command, CancellationToken ct = default)
    {
        var variation = await db.Variations.FirstOrDefaultAsync(v => v.Id == command.Id && v.IsActive, ct);
        if (variation is null)
            return VariationErrors.NotFound;

        var approved = variation.Approve(
            command.ApprovedById, command.SignedByClientId, command.SignedDocumentPath);

        if (approved.IsFailure)
            return approved.Error;

        db.Outbox().Enqueue(new Finance.Contracts.VariationApprovedIntegrationEvent(
            Guid.NewGuid(), variation.Id, variation.ProjectId, command.ApprovedById, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        await FinanceGateVerdicts.PublishAsync(db, variation.ProjectTaskId, projectStageId: null, ct);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class SignVariationCommandValidator : AbstractValidator<SignVariationCommand>
{
    public SignVariationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ApprovedById).NotEmpty();
        RuleFor(x => x.SignedByClientId).NotEmpty();
        RuleFor(x => x.SignedDocumentPath).NotEmpty().MaximumLength(512);
    }
}

/// <summary>
/// The client-signature entry point, and it is <b>the same transition as Approve</b> with the signature
/// arguments made mandatory rather than optional.
///
/// <para>Two commands for one transition because they carry different authority: <c>ManageVariation</c>
/// covers authoring, while <c>finance.variations.approve</c> and <c>finance.variations.sign</c> are
/// separately grantable (see <c>FinancePermissions</c>). Routing both through
/// <c>Variation.Approve</c> is what keeps the state machine single — a second transition method would be
/// a second place for the signature rules to drift.</para>
/// </summary>
internal sealed class SignVariationCommandHandler(FinanceDbContext db) : ICommandHandler<SignVariationCommand>
{
    public async Task<Result> HandleAsync(SignVariationCommand command, CancellationToken ct = default)
    {
        var variation = await db.Variations.FirstOrDefaultAsync(v => v.Id == command.Id && v.IsActive, ct);
        if (variation is null)
            return VariationErrors.NotFound;

        var approved = variation.Approve(
            command.ApprovedById, command.SignedByClientId, command.SignedDocumentPath);

        if (approved.IsFailure)
            return approved.Error;

        db.Outbox().Enqueue(new Finance.Contracts.VariationApprovedIntegrationEvent(
            Guid.NewGuid(), variation.Id, variation.ProjectId, command.ApprovedById, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        await FinanceGateVerdicts.PublishAsync(db, variation.ProjectTaskId, projectStageId: null, ct);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class ApplyVariationCommandValidator : AbstractValidator<ApplyVariationCommand>
{
    public ApplyVariationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RecordedById).NotEmpty();
    }
}

/// <summary>
/// Terminal step, and <b>the asymmetry here is the easiest thing in this module to "fix" into a
/// bug</b>.
///
/// <para>An <c>InternalChange</c> pushes its <c>Amount</c> into the budget line's
/// <c>AdjustmentAllocation</c>, raising the effective cap. A <c>ClientDifference</c> writes
/// <b>nothing</b>: the signed delta stays on the variation row and is read back by the task-coverage
/// check. Making the two symmetrical would double-count every client difference — once as a raised cap
/// and once as coverage against the same overrun.</para>
///
/// <para><b>Applying is idempotent-hostile by design.</b> <c>MarkApplied</c> refuses a second call,
/// because the budget-line adjustment it drives must happen exactly once. Both writes land in the
/// decorator's single transaction, so the adjustment and the status move together or not at all.</para>
/// </summary>
internal sealed class ApplyVariationCommandHandler(FinanceDbContext db) : ICommandHandler<ApplyVariationCommand>
{
    public async Task<Result> HandleAsync(ApplyVariationCommand command, CancellationToken ct = default)
    {
        var variation = await db.Variations.FirstOrDefaultAsync(v => v.Id == command.Id && v.IsActive, ct);
        if (variation is null)
            return VariationErrors.NotFound;

        if (variation.Status != VariationStatus.Approved)
            return VariationErrors.NotApproved;

        if (variation.Kind == VariationKind.InternalChange && variation.BudgetLineId is Guid budgetLineId)
        {
            var line = await db.BudgetLines.FirstOrDefaultAsync(b => b.Id == budgetLineId && b.IsActive, ct);
            if (line is null)
                return BudgetLineErrors.NotFound;

            line.ApplyAdjustment(variation.Amount);
        }

        var applied = variation.MarkApplied();
        if (applied.IsFailure)
            return applied.Error;

        await db.SaveChangesAsync(ct);

        // An InternalChange has just raised the cap, so a task that was over budget may now be covered.
        // Published for both kinds: a ClientDifference already counted at Approve, and re-publishing an
        // unchanged verdict is a no-op on the consumer.
        await FinanceGateVerdicts.PublishAsync(db, variation.ProjectTaskId, projectStageId: null, ct);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class RejectVariationCommandValidator : AbstractValidator<RejectVariationCommand>
{
    public RejectVariationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RejectedById).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(512);
    }
}

internal sealed class RejectVariationCommandHandler(FinanceDbContext db) : ICommandHandler<RejectVariationCommand>
{
    public async Task<Result> HandleAsync(RejectVariationCommand command, CancellationToken ct = default)
    {
        var variation = await db.Variations.FirstOrDefaultAsync(v => v.Id == command.Id && v.IsActive, ct);
        if (variation is null)
            return VariationErrors.NotFound;

        var rejected = variation.Reject(command.RejectedById, command.Note);
        if (rejected.IsFailure)
            return rejected.Error;

        db.Outbox().Enqueue(new Finance.Contracts.VariationClosedIntegrationEvent(
            Guid.NewGuid(), variation.Id, variation.ProjectId, Cancelled: false, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class CancelVariationCommandValidator : AbstractValidator<CancelVariationCommand>
{
    public CancelVariationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(512);
    }
}

/// <summary>Cancel and Reject differ only in who ends it and whether a decider is stamped — both are
/// legal from Draft or Submitted only, and both close the linked gate the same way.</summary>
internal sealed class CancelVariationCommandHandler(FinanceDbContext db) : ICommandHandler<CancelVariationCommand>
{
    public async Task<Result> HandleAsync(CancelVariationCommand command, CancellationToken ct = default)
    {
        var variation = await db.Variations.FirstOrDefaultAsync(v => v.Id == command.Id && v.IsActive, ct);
        if (variation is null)
            return VariationErrors.NotFound;

        var cancelled = variation.Cancel(command.Note);
        if (cancelled.IsFailure)
            return cancelled.Error;

        db.Outbox().Enqueue(new Finance.Contracts.VariationClosedIntegrationEvent(
            Guid.NewGuid(), variation.Id, variation.ProjectId, Cancelled: true, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── Reads ──────────────────────────────────────────────────────────────────────

internal sealed class GetVariationByIdQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetVariationByIdQuery, VariationResponse>
{
    public async Task<Result<VariationResponse>> HandleAsync(
        GetVariationByIdQuery query, CancellationToken ct = default)
    {
        var response = await db.Variations
            .AsNoTracking()
            .Where(v => v.Id == query.Id && v.IsActive)
            .Select(v => new VariationResponse(
                v.Id, v.Kind.ToString(), v.ProjectId, v.ProjectTaskId, v.ProjectStageId, v.BudgetLineId,
                v.Reason, v.Description, v.Amount, v.BudgetedAmount, v.ChosenAmount, v.Status.ToString(),
                v.RequestedById, v.ApprovedById, v.ApprovedAt, v.SentToClientAt, v.SignedByClientId,
                v.SignedAt, v.SignedDocumentPath, v.ResolutionNote, v.CreatedAtUtc, v.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);

        return response is null ? VariationErrors.NotFound : response;
    }
}

internal sealed class GetVariationsByProjectQueryValidator : AbstractValidator<GetVariationsByProjectQuery>
{
    public GetVariationsByProjectQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

internal sealed class GetVariationsByProjectQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetVariationsByProjectQuery, PagedResult<VariationListItemResponse>>
{
    public async Task<Result<PagedResult<VariationListItemResponse>>> HandleAsync(
        GetVariationsByProjectQuery query, CancellationToken ct = default)
    {
        var q = db.Variations.AsNoTracking().Where(v => v.ProjectId == query.ProjectId && v.IsActive);

        if (query.Kind is { } kind)
        {
            var value = FinanceEnumMapping.ToDomain(kind);
            q = q.Where(v => v.Kind == value);
        }

        if (query.Status is { } status)
        {
            var value = FinanceEnumMapping.ToDomain(status);
            q = q.Where(v => v.Status == value);
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(v => v.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(VariationReads.ListItem)
            .ToListAsync(ct);

        return new PagedResult<VariationListItemResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}

internal sealed class GetVariationsByStageQueryValidator : AbstractValidator<GetVariationsByStageQuery>
{
    public GetVariationsByStageQueryValidator() => RuleFor(x => x.StageId).NotEmpty();
}

internal sealed class GetVariationsByStageQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetVariationsByStageQuery, IReadOnlyList<VariationListItemResponse>>
{
    public async Task<Result<IReadOnlyList<VariationListItemResponse>>> HandleAsync(
        GetVariationsByStageQuery query, CancellationToken ct = default)
    {
        var items = await db.Variations
            .AsNoTracking()
            .Where(v => v.ProjectStageId == query.StageId && v.IsActive)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(VariationReads.ListItem)
            .ToListAsync(ct);

        return items;
    }
}

internal sealed class GetVariationsByTaskQueryValidator : AbstractValidator<GetVariationsByTaskQuery>
{
    public GetVariationsByTaskQueryValidator() => RuleFor(x => x.TaskId).NotEmpty();
}

internal sealed class GetVariationsByTaskQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetVariationsByTaskQuery, IReadOnlyList<VariationListItemResponse>>
{
    public async Task<Result<IReadOnlyList<VariationListItemResponse>>> HandleAsync(
        GetVariationsByTaskQuery query, CancellationToken ct = default)
    {
        var items = await db.Variations
            .AsNoTracking()
            .Where(v => v.ProjectTaskId == query.TaskId && v.IsActive)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(VariationReads.ListItem)
            .ToListAsync(ct);

        return items;
    }
}

/// <summary>The list row, written once for the three list queries. Note this is
/// <c>Finance.Contracts.Variations.VariationListItemResponse</c> — a different record from the
/// same-named one in <c>Finance.Contracts.Financial</c>, which the read slice uses.</summary>
internal static class VariationReads
{
    public static readonly System.Linq.Expressions.Expression<Func<Variation, VariationListItemResponse>>
        ListItem = v => new VariationListItemResponse(
            v.Id, v.Kind.ToString(), v.ProjectId, v.ProjectTaskId, v.ProjectStageId, v.Amount,
            v.Status.ToString(), v.CreatedAtUtc);
}
