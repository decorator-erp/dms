using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts.BoqSnapshots;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.BoqSnapshots;

internal sealed class GenerateBoqSnapshotCommandValidator : AbstractValidator<GenerateBoqSnapshotCommand>
{
    public GenerateBoqSnapshotCommandValidator()
    {
        RuleFor(x => x.Request.ProjectId).NotEmpty();
        RuleFor(x => x.GeneratedById).NotEmpty();
        RuleFor(x => x.Request.Notes).MaximumLength(512);
    }
}

/// <summary>
/// Ported from <c>../backend/Domain/Services/BoqSnapshotDomainService.cs</c>. Builds one immutable,
/// versioned statement of a project's cost position from three sources, each contributing its own
/// family of lines.
///
/// <para><b>NOTHING HERE ROUNDS, and that absence is the ported rule.</b> The phase plan asked for "BOQ
/// generation's rounding rule" to be ported; there is none. Every one of the four header totals is a
/// plain <c>Sum(...)</c> in the backend (verified line by line at
/// <c>BoqSnapshotDomainService.cs:61-64</c>), and so is every line amount. The only rounding in the
/// whole Finance area is <c>InvoiceLine.Create</c>'s <c>Math.Round(quantity * unitPrice, 4)</c>, which
/// means a purchase total here is a sum of already-rounded line totals and is exact. Adding a rounding
/// step at this level would make the snapshot disagree with the invoices it is summarising.</para>
///
/// <para><b>The three line families</b>, matching the ordered <c>BoqSnapshotLine.Kind</c> derivation:
/// <list type="number">
/// <item><b>BudgetItem</b> — one per task that has an allocation. Budgeted is the task's
/// <i>effective</i> allocation (original estimate plus applied InternalChange adjustments), actual is
/// its ledger ActualCost, difference is the overrun floored at zero.</item>
/// <item><b>Purchase</b> — one per line of every non-cancelled vendor invoice.</item>
/// <item><b>Difference</b> — one per signed client-difference variation.</item>
/// </list></para>
///
/// <para><b>Where this diverges structurally from the backend, and why the result is identical.</b> The
/// backend starts from the task list (a Projects read), then asks Finance for each task's budget
/// snapshot and skips tasks whose budget is zero. Finance cannot enumerate another module's tasks, so
/// this starts from its OWN task-scoped budget lines and groups by <c>ProjectTaskId</c> — which yields
/// exactly the tasks that have an allocation, the same set the backend arrives at by filtering. The
/// only thing Projects is still needed for is the task NAME used as the line description, fetched in one
/// batched <c>GetTasksAsync</c> call rather than one lookup per task.</para>
///
/// <para><b>Signed differences are NOT filtered by stage</b>, even when a stage is supplied — ported
/// as-is from the backend, where the stage filter is applied to vendor invoices and to the task set but
/// not to variations. Recorded rather than silently "fixed": a stage-scoped snapshot showing the
/// project's whole signed-difference total is arguably wrong, but changing it changes reported money,
/// which is not something to do as a side effect of a port.</para>
///
/// <para><b>The version is read-then-derived, and the unique index is what makes that safe.</b> "The
/// next version" is a fact about the set rather than about any row, so two concurrent generations can
/// read the same maximum. The backend guards this with its own <c>RepeatableRead</c> transaction; this
/// handler opens NO transaction, because in this repo it does not have to and must not — every command
/// handler is already wrapped in one by <c>DomainEventDispatchDecorator</c>, and opening a second on the
/// same connection throws <c>"The connection is already in a transaction"</c>. That decorator uses the
/// default isolation level, so the read-then-derive really is unprotected by isolation here; the unique
/// index on <c>(ProjectId, Version)</c> is what turns a lost race into a failed insert rather than two
/// snapshots both claiming to be version 4. Stated plainly because the difference is real: the backend
/// prevents the collision, this detects it.</para>
/// </summary>
internal sealed class GenerateBoqSnapshotCommandHandler(FinanceDbContext db, IProjectDirectory projects)
    : ICommandHandler<GenerateBoqSnapshotCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(GenerateBoqSnapshotCommand command, CancellationToken ct = default)
    {
        var r = command.Request;

        // DIVERGENCE, same shape as CreateBudgetLineCommandHandler's: the backend never checks the
        // project, so an unknown id becomes a foreign-key violation rather than a 404.
        if (await projects.GetProjectAsync(r.ProjectId, ct) is null)
            return BoqSnapshotErrors.ProjectNotFound;

        var version = 1 + await db.BoqSnapshots
            .Where(b => b.ProjectId == r.ProjectId)
            .Select(b => (int?)b.Version)
            .MaxAsync(ct) ?? 1;

        var taskBudgets = await TaskBudgetsAsync(r.ProjectId, r.ProjectStageId, ct);
        var taskActuals = await TaskActualsAsync([.. taskBudgets.Keys], ct);

        var taskNames = taskBudgets.Count == 0
            ? []
            : (await projects.GetTasksAsync([.. taskBudgets.Keys], ct)).ToDictionary(t => t.Id, t => t.Name);

        var vendorLines = await VendorInvoiceLinesAsync(r.ProjectId, r.ProjectStageId, ct);
        var signedDifferences = await SignedDifferencesAsync(r.ProjectId, ct);

        var budgetItems = taskBudgets
            .Select(kv => new
            {
                TaskId = kv.Key,
                Budgeted = kv.Value,
                Actual = taskActuals.GetValueOrDefault(kv.Key),
            })
            .Select(x => new
            {
                x.TaskId,
                x.Budgeted,
                x.Actual,
                // TaskBudgetSnapshot.OverBudgetAmount, floored at zero: an underspend is not a
                // difference, it is simply money not yet spent.
                Difference = Math.Max(0m, x.Actual - x.Budgeted),
            })
            .ToList();

        var snapshot = BoqSnapshot.Create(
            projectId: r.ProjectId,
            type: FinanceEnumMapping.ToDomain(r.Type),
            version: version,
            generatedById: command.GeneratedById,
            budgetedTotal: budgetItems.Sum(t => t.Budgeted),
            purchaseTotal: vendorLines.Sum(l => l.LineTotal),
            differenceTotal: signedDifferences.Sum(v => v.Amount),
            actualTotal: budgetItems.Sum(t => t.Actual),
            projectStageId: r.ProjectStageId,
            notes: r.Notes);

        var lines = new List<BoqSnapshotLine>();

        foreach (var item in budgetItems)
            lines.Add(BoqSnapshotLine.Create(
                snapshot.Id,
                description: taskNames.GetValueOrDefault(item.TaskId) ?? string.Empty,
                budgetedAmount: item.Budgeted,
                actualAmount: item.Actual,
                differenceAmount: item.Difference,
                projectTaskId: item.TaskId));

        foreach (var line in vendorLines)
            lines.Add(BoqSnapshotLine.Create(
                snapshot.Id,
                description: line.Description,
                budgetedAmount: 0m,
                actualAmount: line.LineTotal,
                differenceAmount: 0m,
                quantity: line.Quantity,
                unit: line.Unit,
                projectTaskId: line.ProjectTaskId,
                invoiceLineId: line.Id));

        foreach (var difference in signedDifferences)
            lines.Add(BoqSnapshotLine.Create(
                snapshot.Id,
                description: difference.Reason,
                budgetedAmount: difference.BudgetedAmount ?? 0m,
                actualAmount: difference.ChosenAmount ?? 0m,
                differenceAmount: difference.Amount,
                projectTaskId: difference.ProjectTaskId,
                variationId: difference.Id));

        db.BoqSnapshots.Add(snapshot);
        db.BoqSnapshotLines.AddRange(lines);

        await db.SaveChangesAsync(ct);

        return snapshot.Id;
    }

    /// <summary>Effective allocation per task — original estimate plus applied adjustments — for every
    /// task in scope that actually has one. The <c>&gt; 0</c> filter is the backend's
    /// <c>if (budgeted &lt;= 0) continue;</c>, and it is what keeps an allocation-free task out of the
    /// BOQ rather than listing it as a zero line.</summary>
    private async Task<Dictionary<Guid, decimal>> TaskBudgetsAsync(
        Guid projectId, Guid? projectStageId, CancellationToken ct)
    {
        var q = db.BudgetLines.Where(b => b.ProjectId == projectId && b.IsActive && b.ProjectTaskId != null);

        if (projectStageId is Guid stageId)
            q = q.Where(b => b.ProjectStageId == stageId);

        var grouped = await q
            .GroupBy(b => b.ProjectTaskId!.Value)
            .Select(g => new
            {
                TaskId = g.Key,
                Budgeted = g.Sum(b => b.AllocatedAmount) + g.Sum(b => b.AdjustmentAllocation),
            })
            .Where(x => x.Budgeted > 0)
            .ToListAsync(ct);

        return grouped.ToDictionary(x => x.TaskId, x => x.Budgeted);
    }

    /// <summary>Ledger ActualCost per task, signed by direction so reversals cancel — the same
    /// arithmetic <c>RecalculateBudgetLineCommandHandler</c> uses, for the same reason.</summary>
    private async Task<Dictionary<Guid, decimal>> TaskActualsAsync(Guid[] taskIds, CancellationToken ct)
    {
        if (taskIds.Length == 0)
            return [];

        var grouped = await db.LedgerEntries
            .Where(e => e.ProjectTaskId != null
                        && taskIds.Contains(e.ProjectTaskId!.Value)
                        && e.Type == LedgerEntryType.ActualCost
                        && e.IsActive)
            .GroupBy(e => e.ProjectTaskId!.Value)
            .Select(g => new
            {
                TaskId = g.Key,
                Actual = g.Sum(e => e.Direction == LedgerDirection.Out ? e.Amount : -e.Amount),
            })
            .ToListAsync(ct);

        return grouped.ToDictionary(x => x.TaskId, x => x.Actual);
    }

    /// <summary>Cancelled invoices are excluded but PAID and unpaid alike are included: the BOQ reports
    /// what was purchased, not what was settled.</summary>
    private async Task<List<InvoiceLine>> VendorInvoiceLinesAsync(
        Guid projectId, Guid? projectStageId, CancellationToken ct)
    {
        var invoices = db.Invoices.Where(i =>
            i.ProjectId == projectId
            && i.Type == InvoiceType.VendorInvoice
            && i.Status != InvoiceStatus.Cancelled
            && i.IsActive);

        if (projectStageId is Guid stageId)
            invoices = invoices.Where(i => i.ProjectStageId == stageId);

        return await db.InvoiceLines
            .Where(l => invoices.Select(i => i.Id).Contains(l.InvoiceId))
            .OrderBy(l => l.CreatedAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>Approved OR Applied: a client difference counts from the moment it is signed, which is
    /// what approval means for that kind. Waiting for Applied would under-report every difference that
    /// has been signed but whose budget-line adjustment has not run — and for ClientDifference there is
    /// no adjustment to run at all, so it would under-report every one of them permanently.</summary>
    private Task<List<Variation>> SignedDifferencesAsync(Guid projectId, CancellationToken ct) =>
        db.Variations
            .Where(v => v.ProjectId == projectId
                        && v.Kind == VariationKind.ClientDifference
                        && (v.Status == VariationStatus.Approved || v.Status == VariationStatus.Applied)
                        && v.IsActive)
            .OrderBy(v => v.CreatedAtUtc)
            .ToListAsync(ct);
}

internal sealed class GetBoqSnapshotByIdQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetBoqSnapshotByIdQuery, BoqSnapshotResponse>
{
    public async Task<Result<BoqSnapshotResponse>> HandleAsync(
        GetBoqSnapshotByIdQuery query, CancellationToken ct = default)
    {
        var response = await BoqSnapshotReads.ByIdAsync(db, query.Id, ct);
        return response is null ? BoqSnapshotErrors.NotFound : response;
    }
}

internal sealed class GetLatestBoqSnapshotQueryValidator : AbstractValidator<GetLatestBoqSnapshotQuery>
{
    public GetLatestBoqSnapshotQueryValidator() => RuleFor(x => x.ProjectId).NotEmpty();
}

internal sealed class GetLatestBoqSnapshotQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetLatestBoqSnapshotQuery, BoqSnapshotResponse>
{
    public async Task<Result<BoqSnapshotResponse>> HandleAsync(
        GetLatestBoqSnapshotQuery query, CancellationToken ct = default)
    {
        var latestId = await db.BoqSnapshots
            .Where(b => b.ProjectId == query.ProjectId
                        && b.IsActive
                        && (query.ProjectStageId == null || b.ProjectStageId == query.ProjectStageId))
            .OrderByDescending(b => b.Version)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        if (latestId is null)
            return BoqSnapshotErrors.NotFound;

        var response = await BoqSnapshotReads.ByIdAsync(db, latestId.Value, ct);
        return response is null ? BoqSnapshotErrors.NotFound : response;
    }
}

internal sealed class GetBoqSnapshotsByProjectQueryValidator : AbstractValidator<GetBoqSnapshotsByProjectQuery>
{
    public GetBoqSnapshotsByProjectQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

internal sealed class GetBoqSnapshotsByProjectQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetBoqSnapshotsByProjectQuery, PagedResult<BoqSnapshotListItemResponse>>
{
    public async Task<Result<PagedResult<BoqSnapshotListItemResponse>>> HandleAsync(
        GetBoqSnapshotsByProjectQuery query, CancellationToken ct = default)
    {
        var q = db.BoqSnapshots.Where(b => b.ProjectId == query.ProjectId && b.IsActive);

        if (query.Type.HasValue)
        {
            var type = FinanceEnumMapping.ToDomain(query.Type.Value);
            q = q.Where(b => b.Type == type);
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(b => b.Version)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(b => new BoqSnapshotListItemResponse(
                b.Id, b.ProjectId, b.ProjectStageId, b.Type.ToString(), b.Version, b.GeneratedAt,
                b.BudgetedTotal, b.PurchaseTotal, b.DifferenceTotal, b.ActualTotal))
            .ToListAsync(ct);

        return new PagedResult<BoqSnapshotListItemResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}

/// <summary>The by-id read, shared by <c>GetBoqSnapshotById</c> and <c>GetLatestBoqSnapshot</c> — the
/// backend duplicates it and the two copies already differ (one filters <c>IsActive</c>, the other does
/// not). One projection, so they cannot drift again.</summary>
internal static class BoqSnapshotReads
{
    public static async Task<BoqSnapshotResponse?> ByIdAsync(
        FinanceDbContext db, Guid id, CancellationToken ct)
    {
        var header = await db.BoqSnapshots
            .Where(b => b.Id == id && b.IsActive)
            .Select(b => new
            {
                b.Id,
                b.ProjectId,
                b.ProjectStageId,
                b.Type,
                b.Version,
                b.GeneratedAt,
                b.GeneratedById,
                b.BudgetedTotal,
                b.PurchaseTotal,
                b.DifferenceTotal,
                b.ActualTotal,
                b.Notes,
                b.CreatedAtUtc,
                b.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (header is null)
            return null;

        var lines = await db.BoqSnapshotLines
            .Where(l => l.BoqSnapshotId == id)
            .OrderBy(l => l.CreatedAtUtc)
            .Select(l => new BoqSnapshotLineResponse(
                l.Id, l.BoqSnapshotId, l.Description, l.Quantity, l.Unit,
                l.BudgetedAmount, l.ActualAmount, l.DifferenceAmount,
                l.ProjectTaskId, l.InvoiceLineId, l.VariationId))
            .ToListAsync(ct);

        return new BoqSnapshotResponse(
            header.Id, header.ProjectId, header.ProjectStageId, header.Type.ToString(), header.Version,
            header.GeneratedAt, header.GeneratedById, header.BudgetedTotal, header.PurchaseTotal,
            header.DifferenceTotal, header.ActualTotal, header.Notes, lines,
            header.CreatedAtUtc, header.UpdatedAtUtc);
    }
}
