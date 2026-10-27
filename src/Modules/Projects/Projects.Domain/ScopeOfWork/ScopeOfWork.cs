using SharedKernel;

namespace Projects.Domain;

internal enum ScopeOfWorkStatus
{
    Draft = 1,
    Confirmed = 2,
}

/// <summary>One template stage to add: name, description and fee are copies, editable afterwards, so a later
/// template edit or delete never changes a scope that was already written.</summary>
internal sealed record ScopeItemPick(Guid SourceStageId, string Name, string? Description, decimal Fee);

/// <summary>
/// The scope of work (نطاق العمل) of one project: the template stages the office picked, each with an
/// editable name, description and fee, plus one duration, an optional planned start and an adjustment
/// (negative is a discount). Draft → Confirmed is one-way; every mutation after confirm is
/// <see cref="ScopeOfWorkErrors.Confirmed"/>. Confirm (the handler) materializes only the picked stages, in
/// item order, through the same instantiation code as a template.
///
/// <para><see cref="Items"/> are always numbered 1..n: append adds at the end, remove renumbers the rest,
/// reorder must name exactly the current set. <see cref="Subtotal"/> and <see cref="Total"/> are computed,
/// never stored.</para>
/// </summary>
internal sealed class ScopeOfWork : Entity, IAuditable
{
    public const int MaxItems = 50;

    private readonly List<ScopeOfWorkItem> _items = [];

    public Guid ProjectId { get; private set; }
    public ScopeOfWorkStatus Status { get; private set; }
    public int? DurationDays { get; private set; }
    public DateOnly? PlannedStartDate { get; private set; }
    public decimal AdjustmentAmount { get; private set; }
    public string? AdjustmentNote { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public Guid? ConfirmedById { get; private set; }

    public IReadOnlyList<ScopeOfWorkItem> Items => [.. _items.OrderBy(i => i.Order)];

    public decimal Subtotal => _items.Sum(i => i.Fee);

    public decimal Total => Subtotal + AdjustmentAmount;

    /// <summary>The planned start plus the duration; null until both are set.</summary>
    public DateOnly? EstimatedEnd =>
        PlannedStartDate is { } start && DurationDays is { } days ? start.AddDays(days) : null;

    private ScopeOfWork() { }

    public static ScopeOfWork Create(Guid projectId) =>
        new() { ProjectId = projectId, Status = ScopeOfWorkStatus.Draft };

    /// <summary>Replaces the whole header, as a PUT does. A blank note is stored as null.</summary>
    public Result UpdateHeader(
        int? durationDays, DateOnly? plannedStartDate, decimal adjustmentAmount, string? adjustmentNote, string? notes)
    {
        if (Status == ScopeOfWorkStatus.Confirmed)
        {
            return ScopeOfWorkErrors.Confirmed;
        }

        DurationDays = durationDays;
        PlannedStartDate = plannedStartDate;
        AdjustmentAmount = adjustmentAmount;
        AdjustmentNote = string.IsNullOrWhiteSpace(adjustmentNote) ? null : adjustmentNote.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        return Result.Success();
    }

    /// <summary>Appends <paramref name="picks"/> at the end, in the order given. All or nothing: a source
    /// already picked (or named twice), a negative fee or more than <see cref="MaxItems"/> items refuses the
    /// whole batch.</summary>
    public Result<IReadOnlyList<ScopeOfWorkItem>> AddItems(IReadOnlyList<ScopeItemPick> picks)
    {
        if (Status == ScopeOfWorkStatus.Confirmed)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItem>>(ScopeOfWorkErrors.Confirmed);
        }

        var sources = _items.Select(i => i.SourceStageId).ToHashSet();
        if (picks.Any(p => !sources.Add(p.SourceStageId)))
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItem>>(ScopeOfWorkErrors.DuplicateItem);
        }

        if (picks.Any(p => p.Fee < 0))
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItem>>(ScopeOfWorkErrors.NegativeFee);
        }

        if (_items.Count + picks.Count > MaxItems)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItem>>(ScopeOfWorkErrors.TooManyItems);
        }

        var added = new List<ScopeOfWorkItem>(picks.Count);
        foreach (ScopeItemPick pick in picks)
        {
            var item = ScopeOfWorkItem.Create(Id, pick.SourceStageId, pick.Name, pick.Description, _items.Count + 1, pick.Fee);
            _items.Add(item);
            added.Add(item);
        }

        return added;
    }

    public Result UpdateItem(Guid itemId, string name, string? description, decimal fee)
    {
        if (Status == ScopeOfWorkStatus.Confirmed)
        {
            return ScopeOfWorkErrors.Confirmed;
        }

        if (_items.FirstOrDefault(i => i.Id == itemId) is not { } item)
        {
            return ScopeOfWorkErrors.ItemNotFound;
        }

        if (fee < 0)
        {
            return ScopeOfWorkErrors.NegativeFee;
        }

        item.Update(name, description, fee);
        return Result.Success();
    }

    /// <summary>Removes one item and renumbers the rest 1..n, keeping their relative order.</summary>
    public Result RemoveItem(Guid itemId)
    {
        if (Status == ScopeOfWorkStatus.Confirmed)
        {
            return ScopeOfWorkErrors.Confirmed;
        }

        if (_items.FirstOrDefault(i => i.Id == itemId) is not { } item)
        {
            return ScopeOfWorkErrors.ItemNotFound;
        }

        _items.Remove(item);
        Renumber([.. _items.OrderBy(i => i.Order).Select(i => i.Id)]);
        return Result.Success();
    }

    /// <summary><paramref name="itemIds"/> is the full new order: exactly the current items, each once.</summary>
    public Result Reorder(IReadOnlyList<Guid> itemIds)
    {
        if (Status == ScopeOfWorkStatus.Confirmed)
        {
            return ScopeOfWorkErrors.Confirmed;
        }

        if (itemIds.Count != _items.Count || itemIds.Distinct().Count() != itemIds.Count
            || !itemIds.All(id => _items.Any(i => i.Id == id)))
        {
            return ScopeOfWorkErrors.InvalidOrder;
        }

        Renumber(itemIds);
        return Result.Success();
    }

    /// <summary>The rules confirm needs from the scope itself, checked before the handler does any work and
    /// again by <see cref="Confirm"/>.</summary>
    public Result EnsureConfirmable()
    {
        if (Status == ScopeOfWorkStatus.Confirmed)
        {
            return ScopeOfWorkErrors.Confirmed;
        }

        if (_items.Count == 0)
        {
            return ScopeOfWorkErrors.Empty;
        }

        if (DurationDays is null)
        {
            return ScopeOfWorkErrors.DurationRequired;
        }

        if (AdjustmentAmount != 0 && AdjustmentNote is null)
        {
            return ScopeOfWorkErrors.AdjustmentNoteRequired;
        }

        return Total < 0 ? ScopeOfWorkErrors.NegativeTotal : Result.Success();
    }

    /// <summary>Records the <c>ProjectStage</c> confirm created for an item. Only while still Draft: it is
    /// called by the confirm itself, just before <see cref="Confirm"/>.</summary>
    public Result SetCreatedStage(Guid itemId, Guid projectStageId)
    {
        if (Status == ScopeOfWorkStatus.Confirmed)
        {
            return ScopeOfWorkErrors.Confirmed;
        }

        if (_items.FirstOrDefault(i => i.Id == itemId) is not { } item)
        {
            return ScopeOfWorkErrors.ItemNotFound;
        }

        item.SetCreatedStage(projectStageId);
        return Result.Success();
    }

    public Result Confirm(Guid confirmedById, DateTime nowUtc)
    {
        Result confirmable = EnsureConfirmable();
        if (confirmable.IsFailure)
        {
            return confirmable;
        }

        Status = ScopeOfWorkStatus.Confirmed;
        ConfirmedById = confirmedById;
        ConfirmedAtUtc = nowUtc;
        return Result.Success();
    }

    private void Renumber(IReadOnlyList<Guid> orderedIds)
    {
        for (int i = 0; i < orderedIds.Count; i++)
        {
            _items.Single(item => item.Id == orderedIds[i]).SetOrder(i + 1);
        }
    }
}
