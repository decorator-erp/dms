using Crm.Domain;
using SharedKernel;
using Xunit;

namespace Crm.UnitTests;

public class CrmLeadIntakeTests
{
    [Fact]
    public void Create_AppliesInitialData()
    {
        var data = new CrmLeadIntakeData(JobTitle: "Engineer", Sector: (ProjectSector)1);
        var leadId = Guid.NewGuid();

        CrmLeadIntake intake = CrmLeadIntake.Create(leadId, data).Value!;

        Assert.Equal(leadId, intake.LeadId);
        Assert.Equal("Engineer", intake.JobTitle);
        Assert.Equal((ProjectSector)1, intake.Sector);
    }

    // --- LC-L5: intake text is stored trimmed, whitespace-only as null ---

    [Fact]
    public void Create_And_Update_StoreWhitespaceOnlyTextAsNull()
    {
        var blank = new CrmLeadIntakeData(
            JobTitle: "   ", ClientAddress: " ", UnitAddress: "\t", DesignerExperienceReason: "  ");

        CrmLeadIntake intake = CrmLeadIntake.Create(Guid.NewGuid(), blank).Value!;
        AssertAllNull(intake);

        intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData(JobTitle: "x")).Value!;
        Assert.True(intake.Update(blank).IsSuccess);
        AssertAllNull(intake);

        static void AssertAllNull(CrmLeadIntake i)
        {
            Assert.Null(i.JobTitle);
            Assert.Null(i.ClientAddress);
            Assert.Null(i.UnitAddress);
            Assert.Null(i.DesignerExperienceReason);
        }
    }

    [Fact]
    public void Create_And_Update_StorePaddedTextTrimmed()
    {
        var padded = new CrmLeadIntakeData(
            JobTitle: "  Engineer ", ClientAddress: " 1 Main St ", UnitAddress: "\t2 Side St  ",
            UsedDesignerBefore: true, DesignerExperience: (PastExperience)1, DesignerExperienceReason: "  fine  ");

        CrmLeadIntake intake = CrmLeadIntake.Create(Guid.NewGuid(), padded).Value!;
        AssertTrimmed(intake);

        intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData()).Value!;
        Assert.True(intake.Update(padded).IsSuccess);
        AssertTrimmed(intake);

        static void AssertTrimmed(CrmLeadIntake i)
        {
            Assert.Equal("Engineer", i.JobTitle);
            Assert.Equal("1 Main St", i.ClientAddress);
            Assert.Equal("2 Side St", i.UnitAddress);
            Assert.Equal("fine", i.DesignerExperienceReason);
        }
    }

    [Fact]
    public void Create_WithAWhitespaceReasonAndNoExperience_IsNotAReasonWithoutExperience()
    {
        Result<CrmLeadIntake> result = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData(
            UsedDesignerBefore: false, DesignerExperienceReason: "   "));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Null(result.Value!.DesignerExperienceReason);
    }

    [Fact]
    public void Create_WithExperienceButNoPriorUse_Fails()
    {
        Result<CrmLeadIntake> result = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData(
            UsedDesignerBefore: false, DesignerExperience: (PastExperience)1));

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.DesignerExperienceWithoutUse, result.Error);
    }

    [Fact]
    public void Create_WithReasonButNoExperience_Fails()
    {
        Result<CrmLeadIntake> result = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData(
            UsedDesignerBefore: true, DesignerExperienceReason: "it was fine"));

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.ExperienceReasonWithoutExperience, result.Error);
    }

    [Fact]
    public void Update_WithValidData_Applies()
    {
        CrmLeadIntake intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData()).Value!;

        Result result = intake.Update(new CrmLeadIntakeData(JobTitle: "Architect"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Architect", intake.JobTitle);
    }

    [Fact]
    public void Update_WithExperienceButNoPriorUse_Fails()
    {
        CrmLeadIntake intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData()).Value!;

        Result result = intake.Update(new CrmLeadIntakeData(
            UsedDesignerBefore: false, DesignerExperience: (PastExperience)1));

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.DesignerExperienceWithoutUse, result.Error);
    }

    [Fact]
    public void Update_WithReasonButNoExperience_Fails()
    {
        CrmLeadIntake intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData()).Value!;

        Result result = intake.Update(new CrmLeadIntakeData(
            UsedDesignerBefore: true, DesignerExperienceReason: "it was fine"));

        Assert.True(result.IsFailure);
        Assert.Equal(CrmLeadErrors.ExperienceReasonWithoutExperience, result.Error);
    }

    [Fact]
    public void AttachResidential_Then_DetachResidential_RoundTrips()
    {
        CrmLeadIntake intake = CrmLeadIntake.Create(Guid.NewGuid(), new CrmLeadIntakeData()).Value!;
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
