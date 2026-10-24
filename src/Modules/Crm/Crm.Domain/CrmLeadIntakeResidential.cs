using SharedKernel;

namespace Crm.Domain;

/// <summary>Residential sector block, ported from the backend's <c>ResidentialIntakeData</c>.</summary>
internal sealed record ResidentialIntakeData(
    UnitType? UnitType = null,
    UnitCondition? UnitCondition = null,
    bool? IsLicensed = null,
    bool? IsRented = null,
    FinishingPurpose? FinishingPurpose = null);

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmLeadIntakeResidential.cs</c> — 1:1 child of the
/// intake, may exist only while <see cref="CrmLeadIntake.Sector"/> is <see cref="ProjectSector.Residential"/>.
/// Column names deliberately match the design-brief head so seeding the project's first brief is a straight
/// column-to-column copy (see <c>CrmDesignBrief.SeedFromIntake</c>, G9-04).
/// </summary>
internal sealed class CrmLeadIntakeResidential : Entity
{
    public Guid LeadIntakeId { get; private set; }

    public UnitType? UnitType { get; private set; }
    public UnitCondition? UnitCondition { get; private set; }
    public bool? IsLicensed { get; private set; }
    public bool? IsRented { get; private set; }
    public FinishingPurpose? FinishingPurpose { get; private set; }

    private CrmLeadIntakeResidential() { }

    public static CrmLeadIntakeResidential Create(Guid leadIntakeId, ResidentialIntakeData data)
    {
        var child = new CrmLeadIntakeResidential { LeadIntakeId = leadIntakeId };
        child.Apply(data);
        return child;
    }

    public void Update(ResidentialIntakeData data) => Apply(data);

    private void Apply(ResidentialIntakeData d)
    {
        UnitType = d.UnitType;
        UnitCondition = d.UnitCondition;
        IsLicensed = d.IsLicensed;
        IsRented = d.IsRented;
        FinishingPurpose = d.FinishingPurpose;
    }
}
