using Crm.Domain;
using SharedKernel;
using Xunit;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using ProjectSector = Crm.Contracts.ProjectSector;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    /// <summary>D-5: the intake's residential answers (licensed, rented, finishing purpose) seed the first
    /// brief only when that brief's sector is Residential or not set. A Medical brief keeps them blank even
    /// when the converted lead's intake had them.</summary>
    [Fact]
    public async Task D5_A_non_residential_first_brief_gets_no_residential_seed()
    {
        BriefTestProject p = await SeedProjectWithConvertedLeadAsync("d5-medical", SeedingIntake);

        Result<Guid> created = await CreateBriefAsync(p, p.StageId,
            new CrmBriefHeadData(UnitType.Apartment, UnitCondition.SemiFinished, Sector: ProjectSector.Medical));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(created.Value);
        Assert.Equal(Domain.ProjectSector.Medical, brief.Sector);
        Assert.Null(brief.IsLicensed);
        Assert.Null(brief.IsRented);
        Assert.Null(brief.FinishingPurpose);
        Assert.Equal(140m, brief.AreaSqm);
    }
}
