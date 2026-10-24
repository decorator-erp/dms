using SharedKernel;

namespace Crm.Domain;

/// <summary>All editable intake fields in one carrier, applied atomically by <see cref="CrmLeadIntake"/>.
/// Ported from the backend's <c>CrmLeadIntakeData</c>.</summary>
internal sealed record CrmLeadIntakeData(
    string? JobTitle = null,
    string? ClientAddress = null,
    ProjectSector? Sector = null,
    string? UnitAddress = null,
    decimal? AreaSqm = null,
    bool? UsedDesignerBefore = null,
    PastExperience? DesignerExperience = null,
    string? DesignerExperienceReason = null);

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmLeadIntake.cs</c>. Lead qualification intake — 1:1
/// with the lead, created lazily on first save. The client part feeds <see cref="Client.FillBlanksFromIntake"/>
/// at convert; the sector part seeds the design-brief draft head when the first requirement period
/// opens (G9-04's <c>CrmDesignBrief.SeedFromIntake</c>) — a copy-once, since the brief head becomes
/// the live truth afterwards and this row stays the immutable pre-client audit snapshot.
/// </summary>
internal sealed class CrmLeadIntake : Entity, IAuditable
{
    public Guid LeadId { get; private set; }

    public string? JobTitle { get; private set; }
    public string? ClientAddress { get; private set; }
    public ProjectSector? Sector { get; private set; }

    public string? UnitAddress { get; private set; }
    public decimal? AreaSqm { get; private set; }
    public bool? UsedDesignerBefore { get; private set; }
    public PastExperience? DesignerExperience { get; private set; }
    public string? DesignerExperienceReason { get; private set; }

    public CrmLeadIntakeResidential? Residential { get; private set; }

    private CrmLeadIntake() { }

    /// <summary>Same field rules as <see cref="Update"/> — the first save is not a way around them.</summary>
    public static Result<CrmLeadIntake> Create(Guid leadId, CrmLeadIntakeData data)
    {
        Result valid = Validate(data);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var intake = new CrmLeadIntake { LeadId = leadId };
        intake.Apply(data);
        return intake;
    }

    /// <summary>Edit-in-place. Sector switching (detaching a stale residential child before applying a
    /// new sector) is orchestrated by the Application handler (G9-09), not here — Domain has no
    /// authority to delete a related row it doesn't own the persistence of.</summary>
    public Result Update(CrmLeadIntakeData data)
    {
        Result valid = Validate(data);
        if (valid.IsFailure)
        {
            return valid;
        }

        Apply(data);
        return Result.Success();
    }

    internal void AttachResidential(CrmLeadIntakeResidential child) => Residential = child;

    internal void DetachResidential() => Residential = null;

    private static Result Validate(CrmLeadIntakeData data)
    {
        if (data.DesignerExperience is not null && data.UsedDesignerBefore != true)
        {
            return CrmLeadErrors.DesignerExperienceWithoutUse;
        }

        if (data.DesignerExperienceReason is not null && data.DesignerExperience is null)
        {
            return CrmLeadErrors.ExperienceReasonWithoutExperience;
        }

        return Result.Success();
    }

    private void Apply(CrmLeadIntakeData d)
    {
        JobTitle = d.JobTitle;
        ClientAddress = d.ClientAddress;
        Sector = d.Sector;
        UnitAddress = d.UnitAddress;
        AreaSqm = d.AreaSqm;
        UsedDesignerBefore = d.UsedDesignerBefore;
        DesignerExperience = d.DesignerExperience;
        DesignerExperienceReason = d.DesignerExperienceReason;
    }
}
