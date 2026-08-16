using Sites.Domain;
using Sites.Domain.Definition;
using Xunit;

namespace Sites.UnitTests;

/// <summary>
/// Ported from <c>../backend/tests/Site.UnitTests/SiteDefinitionGovernanceTests.cs</c> and extended.
///
/// <para><b>What ported.</b> The backend's seven tests each pair a locked case with an unlocked one, so
/// a service that refused unconditionally could not pass — a good bar, kept. But all seven drive
/// <c>SiteDomainService</c> through repository mocks, and the thing they actually gate on
/// (<c>HasCapturedValuesAsync</c> / <c>ModuleHasRecordsAsync</c>) is a database probe. The
/// "is it locked" half is therefore an S1-06 Application test against a real database; what remains here
/// is the half the entity owns: whether the shape change is legal at all, and whether a system
/// definition refuses on its own terms.</para>
///
/// <para>Extended for the branches the backend never covered: <c>LimitNotLegalForType</c> in both
/// directions, the <c>MaxLength</c> ceiling, <c>ChangeCapturedShape</c>'s re-validation against the NEW
/// type, and the reorder defect.</para>
/// </summary>
public class SiteDefinitionGovernanceTests
{
    private static SiteItem MeasuredItem(decimal tolerance = 0.5m, string? unit = "cm") =>
        SiteItem.Create("width_cm", "Width", "العرض", SiteItemDataType.Number, unit, tolerance: tolerance)
            .Value!;

    private static SiteItem UnconstrainedItem() =>
        SiteItem.Create("survey_note", "Note", "ملاحظة", SiteItemDataType.Number).Value!;

    private static SiteModule Module(bool isSystem = false) =>
        SiteModule.Create("openings", "Openings", "الفتحات", isSystem: isSystem).Value!;

    // ── System definitions refuse on their own terms ───────────────────────────

    /// <summary>
    /// Both halves, with the in-use probe irrelevant: a system definition refuses regardless of whether
    /// anything references it. The backend proves this by stubbing the probe false; here the rule is on
    /// the entity, so there is no probe to stub.
    /// </summary>
    [Fact]
    public void SystemDefinitions_AreNeverDeletable_EvenWhenUnused()
    {
        Assert.Equal(SiteErrors.SystemModuleNotDeletable, Module(isSystem: true).EnsureDeletable().Error);
        Assert.Equal(
            SiteErrors.SystemItemNotDeletable,
            SiteItem.Create("width_cm", "W", "ع", SiteItemDataType.Number, isSystem: true)
                .Value!.EnsureDeletable().Error);
    }

    [Fact]
    public void NonSystemDefinitions_AreDeletableOnTheirOwnTerms()
    {
        Assert.True(Module().EnsureDeletable().IsSuccess);
        Assert.True(MeasuredItem().EnsureDeletable().IsSuccess);
    }

    /// <summary>
    /// The renamed error. The backend answers a system-module RENAME with
    /// <c>SystemModuleNotDeletable</c> — "System modules cannot be deleted." — telling the caller about
    /// an operation they did not attempt. Fixed at S1-02/S1-03; this asserts the fix rather than the
    /// original.
    /// </summary>
    [Fact]
    public void ChangeCode_OnASystemModule_SaysRenameNotDelete()
    {
        var module = Module(isSystem: true);

        var result = module.ChangeCode("openings_v2");

        Assert.Equal(SiteErrors.SystemDefinitionNotRenameable, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal("openings", module.Code);
    }

    [Fact]
    public void ChangeCode_OnANormalModule_Renames()
    {
        var module = Module();

        Assert.True(module.ChangeCode("  openings_v2 ").IsSuccess);
        Assert.Equal("openings_v2", module.Code);
    }

    [Fact]
    public void ChangeCode_ToABlank_IsRefused()
    {
        var module = Module();

        Assert.Equal(SiteErrors.InvalidData, module.ChangeCode("   ").Error);
        Assert.Equal("openings", module.Code);
    }

    // ── ChangeCapturedShape ────────────────────────────────────────────────────

    [Fact]
    public void ChangeCapturedShape_ChangesAllFourFrozenFields()
    {
        var item = UnconstrainedItem();

        Assert.True(item.ChangeCapturedShape("width_mm", SiteItemDataType.Number, "mm", 1m).IsSuccess);

        Assert.Equal("width_mm", item.Code);
        Assert.Equal(SiteItemDataType.Number, item.DataType);
        Assert.Equal("mm", item.Unit);
        Assert.Equal(1m, item.Tolerance);
    }

    /// <summary>
    /// The asymmetry between <c>Update</c> and <c>ChangeCapturedShape</c>: limits are re-checked against
    /// the NEW data type, so flipping a bounded Number to Text is refused rather than silently leaving
    /// bounds on a type that ignores them. This is precisely why the backend's own test fixture needed an
    /// <c>UnconstrainedItem</c> — and it never tested the constrained case that motivated it.
    /// </summary>
    [Fact]
    public void ChangeCapturedShape_ToATypeItsExistingBoundsAreIllegalFor_IsRefused()
    {
        var bounded = SiteItem
            .Create("width_cm", "Width", "العرض", SiteItemDataType.Number, "cm", minValue: 1m, maxValue: 500m)
            .Value!;

        var result = bounded.ChangeCapturedShape("width_txt", SiteItemDataType.Text, null, 0m);

        Assert.Equal(SiteErrors.LimitNotLegalForType, result.Error);
        Assert.Equal(400, result.Error.Status);
        Assert.Equal("width_cm", bounded.Code);
        Assert.Equal(SiteItemDataType.Number, bounded.DataType);
    }

    [Fact]
    public void ChangeCapturedShape_WithANegativeTolerance_IsRefused()
    {
        var item = UnconstrainedItem();

        Assert.Equal(
            SiteErrors.InvalidData,
            item.ChangeCapturedShape("width_cm", SiteItemDataType.Number, "cm", -1m).Error);
        Assert.Equal("survey_note", item.Code);
    }

    [Fact]
    public void CodeEquals_IgnoresCaseAndSurroundingSpace()
    {
        var item = MeasuredItem();

        Assert.True(item.CodeEquals("width_cm"));
        Assert.True(item.CodeEquals("  WIDTH_CM  "));
        Assert.False(item.CodeEquals("height_cm"));
        Assert.False(item.CodeEquals(null));
    }

    // ── ValidateLimits, in both directions ─────────────────────────────────────

    [Fact]
    public void Item_WithMinAboveMax_IsRefused()
    {
        var result = SiteItem.Create(
            "width_cm", "W", "ع", SiteItemDataType.Number, "cm", minValue: 500m, maxValue: 1m);

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void Item_WithAMaxLengthOutsideTheColumnsCeiling_IsRefused(int maxLength)
    {
        var result = SiteItem.Create(
            "note", "N", "م", SiteItemDataType.Text, maxLength: maxLength);

        Assert.Equal(SiteErrors.InvalidData, result.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    public void Item_AcceptsTheBoundaryMaxLengths(int maxLength)
    {
        var result = SiteItem.Create("note", "N", "م", SiteItemDataType.Text, maxLength: maxLength);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Numeric limits on a non-Number item. Untested in the backend, and the comment on the rule explains
    /// the stakes: a bound on a Select would be silently ignored by the validator, leaving its author
    /// believing a rule was in force.
    /// </summary>
    [Theory]
    [InlineData((int)SiteItemDataType.Text)]
    [InlineData((int)SiteItemDataType.Bool)]
    [InlineData((int)SiteItemDataType.Select)]
    [InlineData((int)SiteItemDataType.Date)]
    public void Item_WithNumericLimitsOnANonNumberType_IsRefused(int dataType)
    {
        var withMin = SiteItem.Create("x", "X", "س", (SiteItemDataType)dataType, minValue: 1m);
        var withMax = SiteItem.Create("x", "X", "س", (SiteItemDataType)dataType, maxValue: 9m);
        var withTolerance = SiteItem.Create("x", "X", "س", (SiteItemDataType)dataType, tolerance: 0.5m);

        Assert.Equal(SiteErrors.LimitNotLegalForType, withMin.Error);
        Assert.Equal(SiteErrors.LimitNotLegalForType, withMax.Error);
        Assert.Equal(SiteErrors.LimitNotLegalForType, withTolerance.Error);
    }

    [Theory]
    [InlineData((int)SiteItemDataType.Number)]
    [InlineData((int)SiteItemDataType.Bool)]
    [InlineData((int)SiteItemDataType.Select)]
    [InlineData((int)SiteItemDataType.Date)]
    public void Item_WithTextLimitsOnANonTextType_IsRefused(int dataType)
    {
        var withLength = SiteItem.Create("x", "X", "س", (SiteItemDataType)dataType, maxLength: 10);
        var withPattern = SiteItem.Create("x", "X", "س", (SiteItemDataType)dataType, regexPattern: "^a$");

        Assert.Equal(SiteErrors.LimitNotLegalForType, withLength.Error);
        Assert.Equal(SiteErrors.LimitNotLegalForType, withPattern.Error);
    }

    /// <summary>Zero tolerance is not "a tolerance" — it means exact, and must stay legal on every
    /// type, or no non-Number item could be created at all.</summary>
    [Theory]
    [InlineData((int)SiteItemDataType.Text)]
    [InlineData((int)SiteItemDataType.Bool)]
    [InlineData((int)SiteItemDataType.Select)]
    [InlineData((int)SiteItemDataType.Date)]
    public void Item_WithZeroToleranceOnANonNumberType_IsAllowed(int dataType)
    {
        var result = SiteItem.Create("x", "X", "س", (SiteItemDataType)dataType, tolerance: 0m);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Item_Update_ReValidatesAgainstTheCurrentType()
    {
        var text = SiteItem.Create("note", "N", "م", SiteItemDataType.Text).Value!;

        // A numeric bound is still illegal for Text on the update path.
        Assert.Equal(
            SiteErrors.LimitNotLegalForType,
            text.Update("N", "م", minValue: 1m, maxValue: null, maxLength: null, regexPattern: null).Error);

        // ...and a text limit is still legal.
        Assert.True(text.Update("N2", "م2", null, null, maxLength: 50, regexPattern: null).IsSuccess);
        Assert.Equal(50, text.MaxLength);
    }

    // ── ValidateValue (the pure rule moved off the domain service) ─────────────

    [Fact]
    public void ValidateValue_EnforcesNumericBounds()
    {
        var item = SiteItem
            .Create("width_cm", "W", "ع", SiteItemDataType.Number, "cm", minValue: 10m, maxValue: 500m)
            .Value!;

        Assert.Equal(SiteErrors.InvalidData, item.ValidateValue(9m, null, null, null).Error);
        Assert.Equal(SiteErrors.InvalidData, item.ValidateValue(501m, null, null, null).Error);
        Assert.True(item.ValidateValue(10m, null, null, null).IsSuccess);
        Assert.True(item.ValidateValue(500m, null, null, null).IsSuccess);
    }

    [Fact]
    public void ValidateValue_EnforcesMaxLengthAgainstTheTrimmedText()
    {
        var item = SiteItem.Create("note", "N", "م", SiteItemDataType.Text, maxLength: 5).Value!;

        Assert.Equal(SiteErrors.InvalidData, item.ValidateValue(null, "abcdef", null, null).Error);

        // Trimmed before measuring -- surrounding space is not content.
        Assert.True(item.ValidateValue(null, "  abcde  ", null, null).IsSuccess);
    }

    [Fact]
    public void ValidateValue_EnforcesTheRegexPattern()
    {
        var item = SiteItem
            .Create("code", "C", "ك", SiteItemDataType.Text, regexPattern: "^[A-Z]{2}$").Value!;

        Assert.True(item.ValidateValue(null, "AB", null, null).IsSuccess);
        Assert.Equal(SiteErrors.InvalidData, item.ValidateValue(null, "abc", null, null).Error);
    }

    /// <summary>
    /// A malformed admin pattern PASSES rather than rejects. Deliberate: the pattern is supplied through
    /// the form builder and evaluated on the surveyor's write path, so a typo must not block every
    /// submission for that field. Asserted so the behavior is a decision rather than a swallowed
    /// exception.
    /// </summary>
    [Fact]
    public void ValidateValue_WithAMalformedPattern_PassesRatherThanBlockingCapture()
    {
        var item = SiteItem.Create("code", "C", "ك", SiteItemDataType.Text, regexPattern: "([").Value!;

        Assert.True(item.ValidateValue(null, "anything", null, null).IsSuccess);
    }

    [Fact]
    public void ValidateValue_EnforcesOptionMembership()
    {
        var item = SiteItem.Create("leaf", "Leaf", "درفة", SiteItemDataType.Select).Value!;
        var mine = Guid.NewGuid();
        var someoneElses = Guid.NewGuid();

        Assert.True(item.ValidateValue(null, null, mine, new HashSet<Guid> { mine }).IsSuccess);

        Assert.Equal(
            SiteErrors.OptionNotOfItem,
            item.ValidateValue(null, null, someoneElses, new HashSet<Guid> { mine }).Error);

        // No options loaded at all is the same failure, not a pass.
        Assert.Equal(SiteErrors.OptionNotOfItem, item.ValidateValue(null, null, mine, null).Error);
    }

    [Fact]
    public void ValidateValue_WithNoLimits_AcceptsAnything()
    {
        var item = UnconstrainedItem();

        Assert.True(item.ValidateValue(-999m, null, null, null).IsSuccess);
    }

    // ── Options require Select ─────────────────────────────────────────────────

    [Fact]
    public void Option_Create_TrimsAndRequiresEveryName()
    {
        var itemId = Guid.NewGuid();

        var option = SiteItemOption.Create(itemId, "  single ", " Single ", " مفردة ", 2).Value!;

        Assert.Equal("single", option.Code);
        Assert.Equal("Single", option.NameEn);
        Assert.Equal("مفردة", option.NameAr);
        Assert.Equal(2, option.SortOrder);

        Assert.Equal(SiteErrors.InvalidData, SiteItemOption.Create(Guid.Empty, "c", "n", "ن").Error);
        Assert.Equal(SiteErrors.InvalidData, SiteItemOption.Create(itemId, " ", "n", "ن").Error);
        Assert.Equal(SiteErrors.InvalidData, SiteItemOption.Create(itemId, "c", " ", "ن").Error);
        Assert.Equal(SiteErrors.InvalidData, SiteItemOption.Create(itemId, "c", "n", " ").Error);
    }

    [Fact]
    public void Option_Update_LeavesTheCodeAlone()
    {
        var option = SiteItemOption.Create(Guid.NewGuid(), "single", "Single", "مفردة").Value!;

        Assert.True(option.Update("Single leaf", "درفة مفردة", 5).IsSuccess);

        Assert.Equal("single", option.Code);
        Assert.Equal("Single leaf", option.NameEn);
        Assert.Equal(5, option.SortOrder);
    }

    // ── SortOrder consistency (a backend asymmetry, made uniform) ──────────────

    /// <summary>
    /// The backend range-checks <c>sortOrder</c> on <c>SiteRecord</c> and <c>SiteModuleItem</c> but not
    /// on <c>SiteModule</c> or <c>SiteItemOption</c>. Nothing in its design distinguishes them, so the
    /// gap reads as an oversight; made uniform here and asserted so it stays that way.
    /// </summary>
    [Fact]
    public void EveryOrderedDefinition_RejectsANegativeSortOrder()
    {
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteModule.Create("m", "M", "م", sortOrder: -1).Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteItemOption.Create(Guid.NewGuid(), "c", "N", "ن", sortOrder: -1).Error);
        Assert.Equal(
            SiteErrors.InvalidData,
            SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: -1).Error);
    }

    // ── Link placement ─────────────────────────────────────────────────────────

    [Fact]
    public void Link_CarriesTheContextualFlagsNotTheItem()
    {
        var link = SiteModuleItem
            .Create(Guid.NewGuid(), Guid.NewGuid(), isRequired: true, isVisible: false, sortOrder: 3).Value!;

        Assert.True(link.IsRequired);
        Assert.False(link.IsVisible);
        Assert.Equal(3, link.SortOrder);

        // IsVisible and IsActive are independent -- "hidden but required" must stay representable.
        Assert.True(link.IsActive);
    }

    [Fact]
    public void Link_Deactivate_LeavesTheFlagsIntactSoReattachingRestoresThem()
    {
        var link = SiteModuleItem
            .Create(Guid.NewGuid(), Guid.NewGuid(), isRequired: true, sortOrder: 4).Value!;

        link.Deactivate();
        Assert.False(link.IsActive);

        link.Activate();
        Assert.True(link.IsActive);
        Assert.True(link.IsRequired);
        Assert.Equal(4, link.SortOrder);
    }

    // ── Reorder: the B19 fix ───────────────────────────────────────────────────

    [Fact]
    public void Reorder_AppliesEveryEntry()
    {
        var a = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 0).Value!;
        var b = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 1).Value!;

        var result = SiteModuleForm.Reorder([a, b], [(a.ItemId, 5), (b.ItemId, 2)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, a.SortOrder);
        Assert.Equal(2, b.SortOrder);
    }

    /// <summary>
    /// The defect this method exists to fix. The backend mutates as it iterates, so a bad entry LATER in
    /// the list leaves earlier links already reordered on the change tracker — invisible today only
    /// because the request ends before a save. Here the first link must be untouched.
    /// </summary>
    [Fact]
    public void Reorder_WithAnUnknownItemLater_LeavesEveryEarlierLinkUntouched()
    {
        var a = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 0).Value!;
        var b = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 1).Value!;

        var result = SiteModuleForm.Reorder([a, b], [(a.ItemId, 9), (Guid.NewGuid(), 3)]);

        Assert.Equal(SiteErrors.LinkNotFound, result.Error);
        Assert.Equal(0, a.SortOrder);
        Assert.Equal(1, b.SortOrder);
    }

    [Fact]
    public void Reorder_WithANegativeOrderLater_LeavesEveryEarlierLinkUntouched()
    {
        var a = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 0).Value!;
        var b = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 1).Value!;

        var result = SiteModuleForm.Reorder([a, b], [(a.ItemId, 9), (b.ItemId, -1)]);

        Assert.Equal(SiteErrors.InvalidData, result.Error);
        Assert.Equal(0, a.SortOrder);
        Assert.Equal(1, b.SortOrder);
    }

    [Fact]
    public void Reorder_CanPositionADetachedLinkSoReattachingLandsItThere()
    {
        var detached = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 0).Value!;
        detached.Deactivate();

        Assert.True(SiteModuleForm.Reorder([detached], [(detached.ItemId, 7)]).IsSuccess);

        Assert.Equal(7, detached.SortOrder);
        Assert.False(detached.IsActive);
    }

    [Fact]
    public void Reorder_WithAnEmptyRequest_ChangesNothing()
    {
        var a = SiteModuleItem.Create(Guid.NewGuid(), Guid.NewGuid(), sortOrder: 4).Value!;

        Assert.True(SiteModuleForm.Reorder([a], []).IsSuccess);
        Assert.Equal(4, a.SortOrder);
    }
}
