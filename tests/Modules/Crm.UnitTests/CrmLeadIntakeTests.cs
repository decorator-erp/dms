using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmLeadIntakeTests
{
    [Fact]
    public void Create_AppliesInitialData()
    {
        var data = new CrmLeadIntakeData(JobTitle: "Engineer", Sector: (ProjectSector)1);
        var leadId = Guid.NewGuid();

        var intake = CrmLeadIntake.Create(leadId, data);

        Assert.Equal(leadId, intake.LeadId);
        Assert.Equal("Engineer", intake.JobTitle);
        Assert.Equal((ProjectSector)1, intake.Sector);
    }

    [Fact]
    public void Update_WithValidData_Applies()
    {
        var intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData());

        var result = intake.Update(new CrmLeadIntakeData(JobTitle: "Architect"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Architect", intake.JobTitle);
    }

    [Fact]
    public void Update_WithExperienceButNoPriorUse_Fails()
    {
        var intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData());

        var result = intake.Update(new CrmLeadIntakeData(
            UsedDesignerBefore: false, DesignerExperience: (PastExperience)1));

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.DesignerExperienceWithoutUse, result.Error);
    }

    [Fact]
    public void Update_WithReasonButNoExperience_Fails()
    {
        var intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData());

        var result = intake.Update(new CrmLeadIntakeData(
            UsedDesignerBefore: true, DesignerExperienceReason: "it was fine"));

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.ExperienceReasonWithoutExperience, result.Error);
    }

    [Fact]
    public void AttachResidential_Then_DetachResidential_RoundTrips()
    {
        var intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData());
        var residential = CrmLeadIntakeResidential.Create(intake.Id, new ResidentialIntakeData());

        intake.AttachResidential(residential);
        Assert.Same(residential, intake.Residential);

        intake.DetachResidential();
        Assert.Null(intake.Residential);
    }
}

public class CrmLeadIntakeResidentialTests
{
    [Fact]
    public void Create_AppliesData()
    {
        var leadIntakeId = Guid.NewGuid();
        var data = new ResidentialIntakeData(UnitType: (UnitType)1, IsRented: true);

        var residential = CrmLeadIntakeResidential.Create(leadIntakeId, data);

        Assert.Equal(leadIntakeId, residential.LeadIntakeId);
        Assert.Equal((UnitType)1, residential.UnitType);
        Assert.True(residential.IsRented);
    }

    [Fact]
    public void Update_ReplacesData()
    {
        var residential = CrmLeadIntakeResidential.Create(Guid.NewGuid(), new ResidentialIntakeData());

        residential.Update(new ResidentialIntakeData(IsLicensed: true));

        Assert.True(residential.IsLicensed);
    }
}
