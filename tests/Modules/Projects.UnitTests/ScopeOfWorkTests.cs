using Projects.Domain;
using SharedKernel;
using Xunit;

namespace Projects.UnitTests;

/// <summary>SOW-3: the scope-of-work aggregate's rules.</summary>
public class ScopeOfWorkTests
{
    private static ScopeItemPick Pick(decimal fee = 100m, string name = "Stage") =>
        new(Guid.NewGuid(), name, null, fee);

    private static ScopeOfWork ScopeWith(params decimal[] fees)
    {
        var scope = ScopeOfWork.Create(Guid.NewGuid());
        Assert.True(scope.AddItems([.. fees.Select(f => Pick(f))]).IsSuccess);
        return scope;
    }

    private static ScopeOfWork ConfirmableScope()
    {
        ScopeOfWork scope = ScopeWith(1000m, 500m);
        Assert.True(scope.UpdateHeader(30, null, 0m, null, null).IsSuccess);
        return scope;
    }

    [Fact]
    public void Create_starts_as_an_empty_draft()
    {
        var projectId = Guid.NewGuid();
        var scope = ScopeOfWork.Create(projectId);

        Assert.Equal(projectId, scope.ProjectId);
        Assert.Equal(ScopeOfWorkStatus.Draft, scope.Status);
        Assert.Empty(scope.Items);
        Assert.Equal(0m, scope.Total);
    }

    [Fact]
    public void AddItems_appends_at_the_end_numbered_from_one()
    {
        ScopeOfWork scope = ScopeWith(10m, 20m);
        Result<IReadOnlyList<ScopeOfWorkItem>> added = scope.AddItems([Pick(30m, "Third")]);

        Assert.True(added.IsSuccess);
        Assert.Equal([1, 2, 3], scope.Items.Select(i => i.Order));
        Assert.Equal("Third", scope.Items[2].Name);
    }

    [Fact]
    public void AddItems_refuses_a_source_already_picked_or_named_twice()
    {
        ScopeOfWork scope = ScopeWith(10m);
        Guid picked = scope.Items[0].SourceStageId;

        Assert.Equal(ScopeOfWorkErrors.DuplicateItem, scope.AddItems([new ScopeItemPick(picked, "Again", null, 1m)]).Error);
        var fresh = Guid.NewGuid();
        Assert.Equal(ScopeOfWorkErrors.DuplicateItem,
            scope.AddItems([new ScopeItemPick(fresh, "A", null, 1m), new ScopeItemPick(fresh, "B", null, 1m)]).Error);
        Assert.Single(scope.Items);
    }

    [Fact]
    public void AddItems_refuses_a_negative_fee_and_more_than_fifty_items()
    {
        var scope = ScopeOfWork.Create(Guid.NewGuid());
        Assert.Equal(ScopeOfWorkErrors.NegativeFee, scope.AddItems([Pick(-1m)]).Error);

        Assert.True(scope.AddItems([.. Enumerable.Range(0, ScopeOfWork.MaxItems).Select(_ => Pick())]).IsSuccess);
        Assert.Equal(ScopeOfWorkErrors.TooManyItems, scope.AddItems([Pick()]).Error);
        Assert.Equal(ScopeOfWork.MaxItems, scope.Items.Count);
    }

    [Fact]
    public void Total_is_the_subtotal_plus_the_adjustment()
    {
        ScopeOfWork scope = ScopeWith(1000m, 250.50m);
        Assert.True(scope.UpdateHeader(10, null, -200m, "Loyalty discount", null).IsSuccess);

        Assert.Equal(1250.50m, scope.Subtotal);
        Assert.Equal(1050.50m, scope.Total);
    }

    [Fact]
    public void UpdateItem_changes_name_description_and_fee_and_refuses_a_negative_fee_or_unknown_item()
    {
        ScopeOfWork scope = ScopeWith(10m);
        Guid itemId = scope.Items[0].Id;

        Assert.True(scope.UpdateItem(itemId, "Renamed", "Desc", 99m).IsSuccess);
        Assert.Equal(("Renamed", "Desc", 99m), (scope.Items[0].Name, scope.Items[0].Description, scope.Items[0].Fee));
        Assert.Equal(ScopeOfWorkErrors.NegativeFee, scope.UpdateItem(itemId, "X", null, -5m).Error);
        Assert.Equal(ScopeOfWorkErrors.ItemNotFound, scope.UpdateItem(Guid.NewGuid(), "X", null, 1m).Error);
    }

    [Fact]
    public void RemoveItem_renumbers_the_rest_keeping_their_order()
    {
        ScopeOfWork scope = ScopeWith(1m, 2m, 3m, 4m);
        Guid second = scope.Items[1].Id;

        Assert.True(scope.RemoveItem(second).IsSuccess);

        Assert.Equal([1, 2, 3], scope.Items.Select(i => i.Order));
        Assert.Equal([1m, 3m, 4m], scope.Items.Select(i => i.Fee));
        Assert.Equal(ScopeOfWorkErrors.ItemNotFound, scope.RemoveItem(second).Error);
    }

    [Fact]
    public void Reorder_applies_the_full_new_order()
    {
        ScopeOfWork scope = ScopeWith(1m, 2m, 3m);
        Guid[] ids = [.. scope.Items.Select(i => i.Id)];

        Assert.True(scope.Reorder([ids[2], ids[0], ids[1]]).IsSuccess);

        Assert.Equal([3m, 1m, 2m], scope.Items.Select(i => i.Fee));
        Assert.Equal([1, 2, 3], scope.Items.Select(i => i.Order));
    }

    [Fact]
    public void Reorder_refuses_anything_but_exactly_the_current_set()
    {
        ScopeOfWork scope = ScopeWith(1m, 2m, 3m);
        Guid[] ids = [.. scope.Items.Select(i => i.Id)];

        Assert.Equal(ScopeOfWorkErrors.InvalidOrder, scope.Reorder([ids[0], ids[1]]).Error);
        Assert.Equal(ScopeOfWorkErrors.InvalidOrder, scope.Reorder([ids[0], ids[1], ids[1]]).Error);
        Assert.Equal(ScopeOfWorkErrors.InvalidOrder, scope.Reorder([ids[0], ids[1], Guid.NewGuid()]).Error);
        Assert.Equal(ScopeOfWorkErrors.InvalidOrder, scope.Reorder([.. ids, Guid.NewGuid()]).Error);
        Assert.Equal([1m, 2m, 3m], scope.Items.Select(i => i.Fee));
    }

    [Fact]
    public void Confirm_requires_items_a_duration_a_note_for_an_adjustment_and_a_non_negative_total()
    {
        var empty = ScopeOfWork.Create(Guid.NewGuid());
        Assert.True(empty.UpdateHeader(10, null, 0m, null, null).IsSuccess);
        Assert.Equal(ScopeOfWorkErrors.Empty, empty.Confirm(Guid.NewGuid(), DateTime.UtcNow).Error);

        ScopeOfWork noDuration = ScopeWith(100m);
        Assert.Equal(ScopeOfWorkErrors.DurationRequired, noDuration.Confirm(Guid.NewGuid(), DateTime.UtcNow).Error);

        ScopeOfWork noNote = ScopeWith(100m);
        Assert.True(noNote.UpdateHeader(10, null, -10m, "   ", null).IsSuccess);
        Assert.Equal(ScopeOfWorkErrors.AdjustmentNoteRequired, noNote.Confirm(Guid.NewGuid(), DateTime.UtcNow).Error);

        ScopeOfWork negative = ScopeWith(100m);
        Assert.True(negative.UpdateHeader(10, null, -100.01m, "Too generous", null).IsSuccess);
        Assert.Equal(ScopeOfWorkErrors.NegativeTotal, negative.Confirm(Guid.NewGuid(), DateTime.UtcNow).Error);

        // A total of exactly zero is allowed.
        ScopeOfWork zero = ScopeWith(100m);
        Assert.True(zero.UpdateHeader(10, null, -100m, "Free of charge", null).IsSuccess);
        Assert.True(zero.Confirm(Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
    }

    [Fact]
    public void Confirm_records_who_and_when()
    {
        ScopeOfWork scope = ConfirmableScope();
        var by = Guid.NewGuid();
        var at = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.True(scope.Confirm(by, at).IsSuccess);

        Assert.Equal(ScopeOfWorkStatus.Confirmed, scope.Status);
        Assert.Equal(by, scope.ConfirmedById);
        Assert.Equal(at, scope.ConfirmedAtUtc);
    }

    [Fact]
    public void Every_mutation_after_confirm_is_Confirmed()
    {
        ScopeOfWork scope = ConfirmableScope();
        Guid itemId = scope.Items[0].Id;
        Assert.True(scope.Confirm(Guid.NewGuid(), DateTime.UtcNow).IsSuccess);

        Assert.Equal(ScopeOfWorkErrors.Confirmed, scope.UpdateHeader(5, null, 0m, null, null).Error);
        Assert.Equal(ScopeOfWorkErrors.Confirmed, scope.AddItems([Pick()]).Error);
        Assert.Equal(ScopeOfWorkErrors.Confirmed, scope.UpdateItem(itemId, "X", null, 1m).Error);
        Assert.Equal(ScopeOfWorkErrors.Confirmed, scope.RemoveItem(itemId).Error);
        Assert.Equal(ScopeOfWorkErrors.Confirmed, scope.Reorder([.. scope.Items.Select(i => i.Id)]).Error);
        Assert.Equal(ScopeOfWorkErrors.Confirmed, scope.SetCreatedStage(itemId, Guid.NewGuid()).Error);
        Assert.Equal(ScopeOfWorkErrors.Confirmed, scope.Confirm(Guid.NewGuid(), DateTime.UtcNow).Error);
    }

    [Fact]
    public void EstimatedEnd_is_the_planned_start_plus_the_duration()
    {
        ScopeOfWork scope = ScopeWith(1m);
        Assert.Null(scope.EstimatedEnd);

        Assert.True(scope.UpdateHeader(45, new DateOnly(2026, 12, 1), 0m, null, null).IsSuccess);

        Assert.Equal(new DateOnly(2027, 1, 15), scope.EstimatedEnd);
    }
}
