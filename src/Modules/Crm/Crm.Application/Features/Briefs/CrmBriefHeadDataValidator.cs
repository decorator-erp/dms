using Crm.Contracts;
using FluentValidation;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;

namespace Crm.Application.Features.Briefs;

/// <summary>
/// The head's field rules, kept in step with <c>CrmDesignBriefConfiguration</c>'s column lengths so an
/// over-long value is a 400 here instead of a <c>DbUpdateException</c> (500) at save. Numbers that
/// cannot be real (a non-positive area, a negative floor or budget, zero units per floor) and enum
/// values outside their range are 400s too.
/// </summary>
internal sealed class CrmBriefHeadDataValidator : AbstractValidator<CrmBriefHeadData>
{
    private const HiringReason AllHiringReasons =
        HiringReason.ReliefFromCraftsmen | HiringReason.SaveMoneyAndTime | HiringReason.OrganizeAndComfort;

    /// <param name="unitDetailsMayBeOmitted">Create only: an omitted <c>UnitType</c>/<c>UnitCondition</c>
    /// (the JSON field absent, so the enum's default) is a blank the project's first brief fills from the
    /// converted lead's intake; <see cref="CreateCrmDesignBriefCommandHandler"/> still refuses it if
    /// nothing fills it.</param>
    public CrmBriefHeadDataValidator(bool unitDetailsMayBeOmitted = false)
    {
        RuleFor(x => x.Address).MaximumLength(512);
        RuleFor(x => x.VisitorsNote).MaximumLength(1000);
        RuleFor(x => x.DesignerExperienceReason).MaximumLength(1000);
        RuleFor(x => x.PreferredStyle).MaximumLength(256);
        RuleFor(x => x.PreferredColors).MaximumLength(256);
        RuleFor(x => x.ArchitecturalRequests).MaximumLength(2000);
        RuleFor(x => x.OtherNeeds).MaximumLength(2000);

        RuleFor(x => x.AreaSqm).GreaterThan(0m).When(x => x.AreaSqm.HasValue);
        RuleFor(x => x.FloorNo).GreaterThanOrEqualTo(0).When(x => x.FloorNo.HasValue);
        RuleFor(x => x.UnitsPerFloor).GreaterThanOrEqualTo(1).When(x => x.UnitsPerFloor.HasValue);
        RuleFor(x => x.ExpectedBudget).GreaterThanOrEqualTo(0m).When(x => x.ExpectedBudget.HasValue);

        RuleFor(x => x.UnitType).IsInEnum().When(x => !unitDetailsMayBeOmitted || x.UnitType != default);
        RuleFor(x => x.UnitCondition).IsInEnum().When(x => !unitDetailsMayBeOmitted || x.UnitCondition != default);
        RuleFor(x => x.BuildingKind).IsInEnum();
        RuleFor(x => x.Position).IsInEnum();
        RuleFor(x => x.ElevatorStatus).IsInEnum();
        RuleFor(x => x.DesignerExperience).IsInEnum();
        RuleFor(x => x.FurnitureStatus).IsInEnum();
        RuleFor(x => x.TimelinePressure).IsInEnum();
        RuleFor(x => x.FinishingPurpose).IsInEnum();
        RuleFor(x => x.Sector).IsInEnum();

        // A flags enum: any combination of the defined bits, nothing else.
        RuleFor(x => x.HiringReasons)
            .Must(r => (r & ~AllHiringReasons) == 0)
            .WithMessage("'Hiring Reasons' has a value outside the defined reasons.");
    }
}
