using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using FluentValidation.Results;
using Xunit;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using ProjectSector = Crm.Contracts.ProjectSector;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

/// <summary>Pure validator checks — no fixture, no Docker.</summary>
public sealed class BriefCreateValidatorTests
{
    private static CreateCrmDesignBriefCommand Command(CrmBriefHeadData data) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), data, "user-1");

    [Fact]
    public void Create_accepts_omitted_unit_details_so_the_first_brief_can_seed_them()
    {
        ValidationResult result = new CreateCrmDesignBriefCommandValidator().Validate(Command(new CrmBriefHeadData(default, default)));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Create_still_rejects_an_undefined_unit_type()
    {
        ValidationResult result = new CreateCrmDesignBriefCommandValidator().Validate(
            Command(new CrmBriefHeadData((UnitType)99, default)));

        Assert.Contains(result.Errors, e => e.PropertyName == "Data.UnitType");
    }

    [Fact]
    public void An_undefined_sector_is_rejected_on_create_and_on_update()
    {
        var head = new CrmBriefHeadData(UnitType.Villa, Crm.Contracts.UnitCondition.BareBrick, Sector: (ProjectSector)99);

        Assert.Contains(new CreateCrmDesignBriefCommandValidator().Validate(Command(head)).Errors, e => e.PropertyName == "Data.Sector");
        Assert.Contains(new CrmBriefHeadDataValidator().Validate(head).Errors, e => e.PropertyName == "Sector");
    }

    [Fact]
    public void Update_still_requires_the_unit_details()
    {
        ValidationResult result = new CrmBriefHeadDataValidator().Validate(new CrmBriefHeadData(default, default));

        Assert.Contains(result.Errors, e => e.PropertyName == "UnitType");
        Assert.Contains(result.Errors, e => e.PropertyName == "UnitCondition");
    }
}
