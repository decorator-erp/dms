using Sites.Domain;
using Xunit;

namespace Sites.UnitTests;

/// <summary>
/// <c>SiteRecord</c> and <c>SiteRecordValue</c> entity behavior. <b>New in this port — the backend has
/// no test for either type.</b>
///
/// <para><c>SiteRecordValue.Create</c> carries two of the sharpest invariants in the module (exactly one
/// value column populated, and the populated one matching the item's declared type) and had no unit
/// coverage at all: the backend's only value-path test drives the domain service, which constructs
/// values as a side effect of a larger orchestration and asserts nothing about them.</para>
/// </summary>
public class SiteRecordTests
{
    private static readonly Guid RecordId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    // ── SiteRecord ─────────────────────────────────────────────────────────────

    [Fact]
    public void Create_TrimsTheLabelAndDefaultsTheOptionalLinks()
    {
        var record = SiteRecord.Create(Guid.NewGuid(), Guid.NewGuid(), "  Front door  ").Value!;

        Assert.Equal("Front door", record.Label);
        Assert.Null(record.SpaceId);
        Assert.Null(record.CatalogItemId);
        Assert.Equal(0, record.SortOrder);
        Assert.Null(record.Note);
    }

    [Fact]
    public void Create_WithoutASiteOrModuleOrLabel_IsRefused()
    {
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteRecord.Create(Guid.Empty, Guid.NewGuid(), "Door").Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteRecord.Create(Guid.NewGuid(), Guid.Empty, "Door").Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteRecord.Create(Guid.NewGuid(), Guid.NewGuid(), "   ").Error);
    }

    [Fact]
    public void Create_WithANegativeSortOrder_IsRefused()
    {
        var result = SiteRecord.Create(Guid.NewGuid(), Guid.NewGuid(), "Door", sortOrder: -1);

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Update_ChangesEveryEditableFieldAndLeavesTheModuleAlone()
    {
        var moduleId = Guid.NewGuid();
        var record = SiteRecord.Create(Guid.NewGuid(), moduleId, "Door").Value!;
        var space = Guid.NewGuid();
        var catalogItem = Guid.NewGuid();

        Assert.True(record.Update("  Back door ", space, catalogItem, 3, "  chipped  ").IsSuccess);

        Assert.Equal("Back door", record.Label);
        Assert.Equal(space, record.SpaceId);
        Assert.Equal(catalogItem, record.CatalogItemId);
        Assert.Equal(3, record.SortOrder);
        Assert.Equal("chipped", record.Note);

        // The whole point of Update's signature: a record cannot change module.
        Assert.Equal(moduleId, record.ModuleId);
    }

    [Fact]
    public void Update_WithABlankLabelOrNegativeOrder_IsRefusedAndChangesNothing()
    {
        var record = SiteRecord.Create(Guid.NewGuid(), Guid.NewGuid(), "Door", sortOrder: 2).Value!;

        Assert.Equal(SiteErrors.InvalidData, record.Update("  ", null, null, 5, null).Error);
        Assert.Equal(SiteErrors.InvalidData, record.Update("Back door", null, null, -1, null).Error);

        Assert.Equal("Door", record.Label);
        Assert.Equal(2, record.SortOrder);
    }

    [Fact]
    public void Update_CanClearTheOptionalLinks()
    {
        var record = SiteRecord
            .Create(Guid.NewGuid(), Guid.NewGuid(), "Door", Guid.NewGuid(), Guid.NewGuid()).Value!;

        Assert.True(record.Update("Door", null, null, 0, null).IsSuccess);

        Assert.Null(record.SpaceId);
        Assert.Null(record.CatalogItemId);
    }

    // ── SiteRecordValue: exactly one populated ─────────────────────────────────

    [Fact]
    public void Value_WithNothingPopulated_IsRefused()
    {
        var result = SiteRecordValue.Create(RecordId, ItemId, SiteItemDataType.Number);

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Value_WithTwoColumnsPopulated_IsRefused()
    {
        var result = SiteRecordValue.Create(
            RecordId, ItemId, SiteItemDataType.Number, valueNumber: 90m, valueText: "ninety");

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    /// <summary>
    /// Whitespace-only text is normalized to null BEFORE the populated columns are counted, so it reads
    /// as unpopulated rather than as a populated Text value that stores blank. Getting this order wrong
    /// would let a blank string satisfy a required Text field.
    /// </summary>
    [Fact]
    public void Value_WithWhitespaceOnlyText_CountsAsUnpopulated()
    {
        var result = SiteRecordValue.Create(RecordId, ItemId, SiteItemDataType.Text, valueText: "   ");

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Fact]
    public void Value_WithoutARecordOrItem_IsRefused()
    {
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteRecordValue.Create(Guid.Empty, ItemId, SiteItemDataType.Bool, valueBool: true).Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteRecordValue.Create(RecordId, Guid.Empty, SiteItemDataType.Bool, valueBool: true).Error);
    }

    // ── SiteRecordValue: the populated column must match the declared type ─────

    [Fact]
    public void Value_Number_AcceptsANumberAndTrimsNothingElse()
    {
        var value = SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Number, valueNumber: 90.5m).Value!;

        Assert.Equal(90.5m, value.ValueNumber);
        Assert.Null(value.ValueText);
        Assert.Equal(RecordId, value.RecordId);
        Assert.Equal(ItemId, value.ItemId);
    }

    [Fact]
    public void Value_Text_IsTrimmed()
    {
        var value = SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Text, valueText: "  oak veneer  ").Value!;

        Assert.Equal("oak veneer", value.ValueText);
    }

    // Takes the underlying int for the reason given in SiteLifecycleTests: the enum is internal and a
    // public xUnit test method cannot name it.
    [Theory]
    [InlineData((int)SiteItemDataType.Bool)]
    [InlineData((int)SiteItemDataType.Select)]
    [InlineData((int)SiteItemDataType.Date)]
    [InlineData((int)SiteItemDataType.Text)]
    public void Value_ANumberAgainstANonNumberItem_IsATypeMismatch(int dataType)
    {
        var result = SiteRecordValue.Create(RecordId, ItemId, (SiteItemDataType)dataType, valueNumber: 90m);

        Assert.Equal(SiteErrors.ValueTypeMismatch, result.Error);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public void Value_AnOptionAgainstANumberItem_IsATypeMismatch()
    {
        var result = SiteRecordValue.Create(
            RecordId, ItemId, SiteItemDataType.Number, optionId: Guid.NewGuid());

        Assert.Equal(SiteErrors.ValueTypeMismatch, result.Error);
    }

    [Fact]
    public void Value_ADateAgainstABoolItem_IsATypeMismatch()
    {
        var result = SiteRecordValue.Create(
            RecordId, ItemId, SiteItemDataType.Bool, valueDate: new DateOnly(2026, 3, 1));

        Assert.Equal(SiteErrors.ValueTypeMismatch, result.Error);
    }

    [Fact]
    public void Value_EachDataTypeAcceptsItsOwnColumn()
    {
        var option = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 1);

        Assert.True(SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Number, valueNumber: 1m).IsSuccess);
        Assert.True(SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Text, valueText: "x").IsSuccess);
        Assert.True(SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Bool, valueBool: false).IsSuccess);
        Assert.True(SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Select, optionId: option).IsSuccess);
        Assert.True(SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Date, valueDate: date).IsSuccess);
    }

    /// <summary>
    /// <c>false</c> is a value. A <c>HasValue</c>-based populated count gets this right; a truthiness
    /// check would silently drop every "no" a surveyor recorded.
    /// </summary>
    [Fact]
    public void Value_Bool_FalseIsAValueNotAnAbsence()
    {
        var value = SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Bool, valueBool: false).Value!;

        Assert.False(value.ValueBool);
    }

    /// <summary>Likewise zero.</summary>
    [Fact]
    public void Value_Number_ZeroIsAValueNotAnAbsence()
    {
        var value = SiteRecordValue
            .Create(RecordId, ItemId, SiteItemDataType.Number, valueNumber: 0m).Value!;

        Assert.Equal(0m, value.ValueNumber);
    }

    [Fact]
    public void Value_AnUnknownDataType_IsATypeMismatchRatherThanAccepted()
    {
        var result = SiteRecordValue.Create(
            RecordId, ItemId, (SiteItemDataType)99, valueNumber: 1m);

        Assert.Equal(SiteErrors.ValueTypeMismatch, result.Error);
    }
}
