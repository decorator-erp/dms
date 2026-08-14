using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmBriefHeadSnapshotTests
{
    private static CrmDesignBrief CreateBrief(string? address = null, decimal? areaSqm = null) =>
        CrmDesignBrief.Create(
            Guid.NewGuid(), Guid.NewGuid(), null,
            new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: address, AreaSqm: areaSqm));

    [Fact]
    public void Create_CapturesCurrentHeadState()
    {
        var brief = CreateBrief(address: "123 Main St", areaSqm: 120m);

        var snapshot = CrmBriefHeadSnapshot.Create(brief, round: 1);

        Assert.Equal(brief.Id, snapshot.BriefId);
        Assert.Equal(brief.ProjectId, snapshot.ProjectId);
        Assert.Equal(1, snapshot.Round);
        Assert.Equal("123 Main St", snapshot.Address);
        Assert.Equal(120m, snapshot.AreaSqm);
    }

    [Fact]
    public void DiffFrom_WithNoChanges_ReturnsEmpty()
    {
        var brief = CreateBrief(address: "Same");
        var round1 = CrmBriefHeadSnapshot.Create(brief, 1);
        var round2 = CrmBriefHeadSnapshot.Create(brief, 2);

        var diff = round2.DiffFrom(round1);

        Assert.Empty(diff);
    }

    [Fact]
    public void DiffFrom_DetectsChangedFields_AndOnlyThose()
    {
        var brief = CreateBrief(address: "Old Addr", areaSqm: 100m);
        var round1 = CrmBriefHeadSnapshot.Create(brief, 1);

        brief.UpdateHead(
            new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: "New Addr", AreaSqm: 100m), null);
        var round2 = CrmBriefHeadSnapshot.Create(brief, 2);

        var diff = round2.DiffFrom(round1);

        Assert.Contains(nameof(CrmBriefHeadSnapshot.Address), diff);
        Assert.DoesNotContain(nameof(CrmBriefHeadSnapshot.AreaSqm), diff);
    }

    [Fact]
    public void DiffFrom_DetectsMultipleChangedFields()
    {
        var brief = CreateBrief(address: "Old", areaSqm: 100m);
        var round1 = CrmBriefHeadSnapshot.Create(brief, 1);

        brief.UpdateHead(
            new CrmBriefHeadData((UnitType)2, (UnitCondition)2, Address: "New", AreaSqm: 200m), null);
        var round2 = CrmBriefHeadSnapshot.Create(brief, 2);

        var diff = round2.DiffFrom(round1);

        Assert.Contains(nameof(CrmBriefHeadSnapshot.UnitType), diff);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.UnitCondition), diff);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.Address), diff);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.AreaSqm), diff);
        Assert.Equal(4, diff.Count);
    }
}
