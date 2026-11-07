using FluentValidation;
using Sites.Contracts.Records;
using Sites.Contracts.Surveys;

namespace Sites.Application;

/// <summary>
/// S5: the column-width and enum-range limits shared by more than one command validator, so an
/// out-of-range number, an over-long text or an unknown enum number is a 400 naming the field rather
/// than a cast straight into the domain or an overflow at commit (500).
/// </summary>
internal static class SiteInputLimits
{
    /// <summary><c>numeric(8,2)</c> — <c>sites."AreaSqm"</c>. R1-8: checked as precision 8 / scale 2
    /// (<see cref="SiteUnitDataRequestValidator"/>), so at most 999999.99: a third decimal would let Postgres round a
    /// value under <see cref="MaxAreaSqm"/> up past the column.</summary>
    public const decimal MaxAreaSqm = 1_000_000m;

    /// <summary>Inside <c>numeric(18,4)</c> (values, item bounds) and <c>numeric(18,3)</c> (tolerance).</summary>
    public const decimal MaxMagnitude = 100_000_000_000_000m;

    /// <summary><c>site_record_values."ValueText"</c>'s width.</summary>
    public const int MaxValueText = 1024;
}

/// <summary>The unit facts on create and update — one validator so the two cannot drift.</summary>
internal sealed class SiteUnitDataRequestValidator : AbstractValidator<SiteUnitDataRequest>
{
    public SiteUnitDataRequestValidator()
    {
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(4000);
        RuleFor(x => x.UtilityMetersNote).MaximumLength(4000);
        RuleFor(x => x.UnitType).IsInEnum();
        RuleFor(x => x.UnitCondition).IsInEnum();
        RuleFor(x => x.BuildingKind).IsInEnum();
        RuleFor(x => x.Position).IsInEnum();
        RuleFor(x => x.ElevatorStatus).IsInEnum();
        RuleFor(x => x.AreaSqm).LessThan(SiteInputLimits.MaxAreaSqm)
            .PrecisionScale(8, 2, ignoreTrailingZeros: true); // R1-8: numeric(8,2) exactly, no rounding past it
    }
}

/// <summary>One captured value, on the single-record save and on every grid row.</summary>
internal sealed class SiteValueRequestValidator : AbstractValidator<SiteValueRequest>
{
    public SiteValueRequestValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.ValueNumber)
            .ExclusiveBetween(-SiteInputLimits.MaxMagnitude, SiteInputLimits.MaxMagnitude)
            .PrecisionScale(18, 4, ignoreTrailingZeros: true); // R1-8: numeric(18,4) exactly, no rounding past it
        RuleFor(x => x.ValueText).MaximumLength(SiteInputLimits.MaxValueText);
    }
}
