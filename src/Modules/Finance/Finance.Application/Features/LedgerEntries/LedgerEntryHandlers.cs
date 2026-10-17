using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Application.Common;
using Finance.Contracts.LedgerEntries;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Features.LedgerEntries;

internal sealed class PostLedgerEntryCommandValidator : AbstractValidator<PostLedgerEntryCommand>
{
    public PostLedgerEntryCommandValidator()
    {
        RuleFor(x => x.Request.ProjectId).NotEmpty();
        RuleFor(x => x.RecordedById).NotEmpty();
        RuleFor(x => x.Request.Amount).GreaterThan(0);
        RuleFor(x => x.Request.VendorName).MaximumLength(128);
        RuleFor(x => x.Request.Reference).MaximumLength(128);
        RuleFor(x => x.Request.Note).MaximumLength(512);
    }
}

/// <summary>
/// Ported from <c>../backend/Domain/Services/LedgerEntryDomainService.PostCoreAsync</c>. The single
/// write path for every financial movement in the module.
///
/// <para><b>Order matters and is the order below.</b> Dedupe, then construct (which enforces the
/// direction and party invariants), then the budget gate, then persist, then apply effects. Checking
/// the budget before construction would test an amount the entity might reject; applying effects before
/// persisting would move a budget line for an entry that never landed.</para>
///
/// <para><b>No explicit transaction</b>, unlike the backend — <c>DomainEventDispatchDecorator</c>
/// already wraps every command handler in one, and opening a second on the same connection throws. The
/// entry and its budget-line effects therefore still commit atomically, which is the property the
/// backend's transaction was there for.</para>
///
/// <para><b>The gate consequences leave by the outbox, not by a call.</b> The backend resolves Budget
/// gates from this same path — satisfying or re-pending them as task coverage and stage funding change,
/// and locking or unlocking the owner. Every one of those is a write into Projects' tables. Finance
/// instead computes the verdict (<c>FinanceGateVerdicts</c>) and publishes it; Projects acts on it.
/// See <c>PublishPostingConsequencesAsync</c>.</para>
/// </summary>
internal sealed class PostLedgerEntryCommandHandler(FinanceDbContext db)
    : ICommandHandler<PostLedgerEntryCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(PostLedgerEntryCommand command, CancellationToken ct = default)
    {
        PostLedgerEntryRequest r = command.Request;
        LedgerEntryType type = FinanceEnumMapping.ToDomain(r.Type);

        if (await IsDuplicateAsync(r.ProjectId, type, r.VariationId, r.Reference, ct))
        {
            return LedgerEntryErrors.DuplicatePosting;
        }

        Result<LedgerEntry> created = LedgerEntry.Create(
            projectId: r.ProjectId,
            amount: r.Amount,
            direction: FinanceEnumMapping.ToDomain(r.Direction),
            type: type,
            occurredOn: r.OccurredOn,
            recordedById: command.RecordedById,
            projectStageId: r.ProjectStageId,
            projectTaskId: r.ProjectTaskId,
            budgetLineId: r.BudgetLineId,
            method: FinanceEnumMapping.ToDomain(r.Method),
            clientId: r.ClientId,
            vendorName: r.VendorName,
            variationId: r.VariationId,
            reference: r.Reference,
            note: r.Note);

        if (created.IsFailure)
        {
            return created.Error;
        }

        LedgerEntry entry = created.Value!;

        // The budget gate. A commitment beyond what the line still has available is blocked until a
        // variation funds it -- this is the rule that makes AvailableAmount mean something rather than
        // being a number that drifts negative. Only commitments are gated: an actual cost records money
        // that has already moved, and refusing to record it would make the ledger disagree with reality.
        if (entry is { Type: LedgerEntryType.Commitment, BudgetLineId: Guid budgetLineId })
        {
            BudgetLine? line = await db.BudgetLines
                .FirstOrDefaultAsync(b => b.Id == budgetLineId && b.IsActive, ct);

            if (line is null)
            {
                return BudgetLineErrors.NotFound;
            }

            if (entry.Amount > line.AvailableAmount)
            {
                return LedgerEntryErrors.InsufficientBudget;
            }
        }

        db.LedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        await ApplyPostingEffectsAsync(db, entry, sign: +1, ct);
        await db.SaveChangesAsync(ct);

        // F1-08: the seam this handler reserved. The budget-line effects above are Finance's own; the
        // GATE consequences are Projects', so Finance publishes decided verdicts and Projects acts.
        await PublishPostingConsequencesAsync(db, entry, ct);
        await db.SaveChangesAsync(ct);

        return entry.Id;
    }

    /// <summary>
    /// Everything a posting tells other modules, published once for both the posting and the reversal
    /// path so the two can never diverge.
    ///
    /// <para><b>Which verdicts are published depends on the entry type, and the mapping is the backend's
    /// own.</b> An <c>ActualCost</c> changes what a task has spent, so it re-decides task coverage. A
    /// <c>ClientPayment</c> changes what a stage has collected, so it re-decides stage funding. A
    /// <c>Commitment</c> changes neither — it moves money that has not been spent and has not been
    /// received — which is why the backend resolves no gates for it either.</para>
    ///
    /// <para><b>It took a <c>bool reversal</c> until X-02</b>, read by nothing but the
    /// <c>PaymentPosted</c> event that task deleted. Posting and reversal are indistinguishable to
    /// everything that remains here, and correctly so: both change the sums the verdicts are computed
    /// from, and the verdict is what travels. The parameter went with its only reader rather than
    /// staying on as a hint that some branch below cares about the difference.</para>
    /// </summary>
    internal static Task PublishPostingConsequencesAsync(
        FinanceDbContext db, LedgerEntry entry, CancellationToken ct) => FinanceGateVerdicts.PublishAsync(
            db,
            projectTaskId: entry.Type == LedgerEntryType.ActualCost ? entry.ProjectTaskId : null,
            projectStageId: entry.Type == LedgerEntryType.ClientPayment ? entry.ProjectStageId : null,
            ct);

    /// <summary>
    /// The idempotence key: an entry is a duplicate when another non-reversal entry of the same type
    /// already names the same source. Only called when there IS a source — an entry with neither a
    /// variation nor a reference is not deduped at all, because nothing identifies it.
    ///
    /// <para><b>Reversals are excluded</b>, matching the backend. Without that, reversing an entry and
    /// re-posting it correctly would be refused as a duplicate of the reversal.</para>
    ///
    /// <para><b>DIVERGENCE: scoped to the project.</b> The backend's check is global, so two different
    /// projects both posting a commitment referenced <c>"MILESTONE-1"</c> collide and the second is
    /// rejected as a duplicate. A reference is free text whose meaning is local to its project; a
    /// variation id is globally unique and unaffected either way. Fixed rather than ported, and tested.</para>
    /// </summary>
    private Task<bool> IsDuplicateAsync(
        Guid projectId, LedgerEntryType type, Guid? variationId, string? reference, CancellationToken ct)
    {
        if (variationId is null && reference is null)
        {
            return Task.FromResult(false);
        }

        return db.LedgerEntries.AnyAsync(
            e => e.ProjectId == projectId
                 && e.Type == type
                 && e.ReversalOfId == null
                 && (variationId == null || e.VariationId == variationId)
                 && (reference == null || e.Reference == reference),
            ct);
    }

    /// <summary>
    /// Replays a posting's effect on the budget line, with <paramref name="sign"/> <c>+1</c> for a
    /// posting and <c>-1</c> for a reversal. One method for both directions, so a reversal can never
    /// undo something differently from how it was done.
    ///
    /// <para><b>The <c>ActualCost</c> case is the commitment-to-actual handoff</b> and is one operation
    /// because it is one fact: money that was committed has now been spent.
    /// <c>BudgetLine.ApplyActualCost</c> raises Actual and lowers Committed together — splitting it is
    /// the classic way to double-count a spend against a budget forever.</para>
    ///
    /// <para>This method moves budget lines only. The gate consequences are published separately by
    /// <see cref="PublishPostingConsequencesAsync"/>, which both call sites invoke immediately after —
    /// kept apart because one is a Finance-owned write and the other is a cross-module announcement.</para>
    /// </summary>
    internal static async Task ApplyPostingEffectsAsync(
        FinanceDbContext db, LedgerEntry entry, int sign, CancellationToken ct)
    {
        decimal delta = sign * entry.Amount;

        if (entry.BudgetLineId is not Guid budgetLineId)
        {
            return;
        }

        BudgetLine? line = await db.BudgetLines.FirstOrDefaultAsync(b => b.Id == budgetLineId && b.IsActive, ct);
        if (line is null)
        {
            return;
        }

        switch (entry.Type)
        {
            case LedgerEntryType.Commitment:
                line.ApplyCommitment(delta);
                break;

            case LedgerEntryType.ActualCost:
                line.ApplyActualCost(delta);
                break;

            case LedgerEntryType.ClientPayment:
                // Client payments move no budget line: they are money coming IN against an invoice, not
                // spend against an allocation. Their effect is on stage funding, which is F1-08's.
                break;
        }
    }
}

internal sealed class ReverseLedgerEntryCommandValidator : AbstractValidator<ReverseLedgerEntryCommand>
{
    public ReverseLedgerEntryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RecordedById).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(512);
    }
}

/// <summary>
/// Corrects a posting by writing its mirror, never by editing or deleting it. The mirror carries the
/// opposite direction and the same magnitude, so every signed sum in the module nets the pair to zero
/// without knowing anything about reversals.
///
/// <para><b>The original's effects are replayed with the sign negated</b> — the same
/// <c>ApplyPostingEffectsAsync</c> the posting used. That is what guarantees a reversal undoes exactly
/// what was done, including the commitment-to-actual handoff, which a hand-written inverse would get
/// wrong in one direction sooner or later.</para>
///
/// <para><b>One reversal per entry.</b> A second is refused with <c>AlreadyReversed</c> rather than
/// silently doubling the correction. Note this does not stop a reversal from itself being reversed —
/// that is a legitimate re-instatement and flips the direction back.</para>
/// </summary>
internal sealed class ReverseLedgerEntryCommandHandler(FinanceDbContext db)
    : ICommandHandler<ReverseLedgerEntryCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ReverseLedgerEntryCommand command, CancellationToken ct = default)
    {
        LedgerEntry? original = await db.LedgerEntries.FirstOrDefaultAsync(e => e.Id == command.Id && e.IsActive, ct);
        if (original is null)
        {
            return LedgerEntryErrors.NotFound;
        }

        if (await db.LedgerEntries.AnyAsync(e => e.ReversalOfId == command.Id, ct))
        {
            return LedgerEntryErrors.AlreadyReversed;
        }

        Result<LedgerEntry> mirror = LedgerEntry.CreateReversal(original, command.RecordedById, command.Note);
        if (mirror.IsFailure)
        {
            return mirror.Error;
        }

        db.LedgerEntries.Add(mirror.Value!);
        await db.SaveChangesAsync(ct);

        await PostLedgerEntryCommandHandler.ApplyPostingEffectsAsync(db, original, sign: -1, ct);
        await db.SaveChangesAsync(ct);

        // The ORIGINAL's scope decides which verdicts changed -- the mirror carries the same scope, but
        // reading it from the original keeps this identical to the effects call above it.
        await PostLedgerEntryCommandHandler.PublishPostingConsequencesAsync(db, original, ct);
        await db.SaveChangesAsync(ct);

        return mirror.Value!.Id;
    }
}

internal sealed class AllocateLedgerEntryCommandValidator : AbstractValidator<AllocateLedgerEntryCommand>
{
    public AllocateLedgerEntryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}

/// <summary>
/// Settles part or all of a payment against one invoice.
///
/// <para><b>The same money is bounded from both sides, and both bounds are needed.</b>
/// <c>LedgerAllocation.WouldExceedUnallocated</c> stops over-SPENDING a payment across several
/// invoices; <c>Invoice.ApplyPayment</c> stops over-PAYING an invoice from several payments. Neither
/// implies the other, and a port that keeps only one leaves the other kind of double-count possible.</para>
///
/// <para><b>DIVERGENCE: a commitment cannot be allocated.</b> The backend defines
/// <c>LedgerEntryErrors.NotAPayment</c> — "Only cleared client/vendor payment entries can be allocated
/// to invoices" — and then never raises it, so a Commitment can be allocated against an invoice and
/// mark it paid with money that has not moved. The rule is enforced here, narrowly: only
/// <c>Commitment</c> is refused. <c>ClientPayment</c> and <c>ActualCost</c> are both real movements —
/// the latter is how a vendor invoice gets paid — so excluding either would break a legitimate flow.</para>
/// </summary>
internal sealed class AllocateLedgerEntryCommandHandler(FinanceDbContext db)
    : ICommandHandler<AllocateLedgerEntryCommand>
{
    public async Task<Result> HandleAsync(AllocateLedgerEntryCommand command, CancellationToken ct = default)
    {
        LedgerEntry? entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.Id == command.Id && e.IsActive, ct);
        if (entry is null)
        {
            return LedgerEntryErrors.NotFound;
        }

        if (entry.Type == LedgerEntryType.Commitment)
        {
            return LedgerEntryErrors.NotAPayment;
        }

        decimal alreadyAllocated = await db.LedgerAllocations
            .Where(a => a.LedgerEntryId == command.Id && a.IsActive)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        if (LedgerAllocation.WouldExceedUnallocated(entry.Amount, alreadyAllocated, command.Amount))
        {
            return LedgerAllocationErrors.ExceedsUnallocated;
        }

        Invoice? invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == command.InvoiceId && i.IsActive, ct);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        Result<LedgerAllocation> allocation = LedgerAllocation.Create(command.Id, command.InvoiceId, command.Amount);
        if (allocation.IsFailure)
        {
            return allocation.Error;
        }

        // The invoice side is applied BEFORE the allocation row is added, so an over-payment refusal
        // leaves nothing to undo. Both land in the decorator's transaction either way; this just keeps
        // the failure path from depending on that.
        Result paid = invoice.ApplyPayment(command.Amount);
        if (paid.IsFailure)
        {
            return paid.Error;
        }

        db.LedgerAllocations.Add(allocation.Value!);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class GetLedgerEntryByIdQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetLedgerEntryByIdQuery, LedgerEntryResponse>
{
    public async Task<Result<LedgerEntryResponse>> HandleAsync(
        GetLedgerEntryByIdQuery query, CancellationToken ct = default)
    {
        LedgerEntryResponse? response = await db.LedgerEntries
            .Where(e => e.Id == query.Id && e.IsActive)
            .Select(e => new LedgerEntryResponse(
                e.Id, e.ProjectId, e.ProjectStageId, e.ProjectTaskId, e.BudgetLineId,
                e.Direction.ToString(), e.Type.ToString(), e.Amount,
                e.Method != null ? e.Method.ToString() : null,
                e.ClientId, e.VendorName, e.VariationId, e.ReversalOfId,
                e.OccurredOn, e.Reference, e.Note, e.RecordedById, e.CreatedAtUtc, e.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);

        return response is null ? LedgerEntryErrors.NotFound : response;
    }
}

internal sealed class GetLedgerEntriesByProjectQueryValidator : AbstractValidator<GetLedgerEntriesByProjectQuery>
{
    public GetLedgerEntriesByProjectQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

internal sealed class GetLedgerEntriesByProjectQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetLedgerEntriesByProjectQuery, PagedResult<LedgerEntryListItemResponse>>
{
    public async Task<Result<PagedResult<LedgerEntryListItemResponse>>> HandleAsync(
        GetLedgerEntriesByProjectQuery query, CancellationToken ct = default)
    {
        IQueryable<LedgerEntry> q = db.LedgerEntries.Where(e => e.ProjectId == query.ProjectId && e.IsActive);

        if (query.Direction is { } direction)
        {
            LedgerDirection value = FinanceEnumMapping.ToDomain(direction);
            q = q.Where(e => e.Direction == value);
        }

        if (query.Type is { } type)
        {
            LedgerEntryType value = FinanceEnumMapping.ToDomain(type);
            q = q.Where(e => e.Type == value);
        }

        if (query.ProjectStageId is { } stageId)
        {
            q = q.Where(e => e.ProjectStageId == stageId);
        }

        if (query.ProjectTaskId is { } taskId)
        {
            q = q.Where(e => e.ProjectTaskId == taskId);
        }

        if (query.FromDate is { } from)
        {
            q = q.Where(e => e.OccurredOn >= from);
        }

        if (query.ToDate is { } to)
        {
            q = q.Where(e => e.OccurredOn <= to);
        }

        int total = await q.CountAsync(ct);

        List<LedgerEntryListItemResponse> items = await q
            .OrderByDescending(e => e.OccurredOn)
            .ThenByDescending(e => e.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(e => new LedgerEntryListItemResponse(
                e.Id, e.ProjectId, e.ProjectStageId, e.ProjectTaskId,
                e.Direction.ToString(), e.Type.ToString(), e.Amount, e.OccurredOn,
                e.ClientId, e.VendorName, e.Reference))
            .ToListAsync(ct);

        return new PagedResult<LedgerEntryListItemResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}

internal sealed class GetUnallocatedLedgerEntriesQueryValidator
    : AbstractValidator<GetUnallocatedLedgerEntriesQuery>
{
    public GetUnallocatedLedgerEntriesQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

/// <summary>
/// Payments with money still to allocate — the worklist for settling invoices.
///
/// <para>Reversals are excluded and so are entries that have BEEN reversed: neither has money left to
/// allocate, and offering either would let a user settle an invoice against a movement that has been
/// undone. The backend does not exclude them; this does, and says so.</para>
/// </summary>
internal sealed class GetUnallocatedLedgerEntriesQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetUnallocatedLedgerEntriesQuery, PagedResult<UnallocatedLedgerEntryResponse>>
{
    public async Task<Result<PagedResult<UnallocatedLedgerEntryResponse>>> HandleAsync(
        GetUnallocatedLedgerEntriesQuery query, CancellationToken ct = default)
    {
        IQueryable<Guid> reversedIds = db.LedgerEntries
            .Where(e => e.ReversalOfId != null)
            .Select(e => e.ReversalOfId!.Value);

        IQueryable<LedgerEntry> candidates = db.LedgerEntries
            .Where(e => e.ProjectId == query.ProjectId
                        && e.IsActive
                        && e.Type != LedgerEntryType.Commitment
                        && e.ReversalOfId == null
                        && !reversedIds.Contains(e.Id));

        var projected = candidates
            .Select(e => new
            {
                Entry = e,
                Allocated = db.LedgerAllocations
                    .Where(a => a.LedgerEntryId == e.Id && a.IsActive)
                    .Sum(a => (decimal?)a.Amount) ?? 0m,
            })
            .Where(x => x.Entry.Amount - x.Allocated > 0);

        int total = await projected.CountAsync(ct);

        List<UnallocatedLedgerEntryResponse> items = await projected
            .OrderByDescending(x => x.Entry.OccurredOn)
            .ThenByDescending(x => x.Entry.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(x => new UnallocatedLedgerEntryResponse(
                x.Entry.Id, x.Entry.ProjectId, x.Entry.Amount, x.Entry.Amount - x.Allocated,
                x.Entry.OccurredOn, x.Entry.ClientId, x.Entry.VendorName, x.Entry.Reference))
            .ToListAsync(ct);

        return new PagedResult<UnallocatedLedgerEntryResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}
