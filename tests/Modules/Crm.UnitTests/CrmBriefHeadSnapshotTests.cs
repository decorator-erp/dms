using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmBriefHeadSnapshotTests
{
    private static CrmDesignBrief CreateBrief(string? address = null, decimal? areaSqm = null) =>
        CrmDesignBrief.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: address, AreaSqm: areaSqm));

    private static List<string> Fields(IReadOnlyList<BriefFieldChange> changes) => [.. changes.Select(c => c.Field)];

    /// <summary>Snapshots are taken at send; the head is only editable between rounds, so tests drive it
    /// the way the real flow does (edit as Draft, snapshot, next edit).</summary>
    private static CrmDesignBrief BriefWithBudget(decimal budget, Guid? lastMeetingId = null)
    {
        CrmDesignBrief brief = CreateBrief(address: "Addr");
        brief.UpdateHead(
            new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: "Addr", ExpectedBudget: budget, IsLicensed: true),
            lastMeetingId);
        return brief;
    }

    [Fact]
    public void Create_CapturesCurrentHeadState()
    {
        CrmDesignBrief brief = CreateBrief(address: "123 Main St", areaSqm: 120m);

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
        CrmDesignBrief brief = CreateBrief(address: "Same");
        var round1 = CrmBriefHeadSnapshot.Create(brief, 1);
        var round2 = CrmBriefHeadSnapshot.Create(brief, 2);

        IReadOnlyList<string> diff = Fields(round2.DiffFrom(round1, BriefDiffAudience.Office));

        Assert.Empty(diff);
    }

    [Fact]
    public void DiffFrom_DetectsChangedFields_AndOnlyThose()
    {
        CrmDesignBrief brief = CreateBrief(address: "Old Addr", areaSqm: 100m);
        var round1 = CrmBriefHeadSnapshot.Create(brief, 1);

        brief.UpdateHead(
            new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: "New Addr", AreaSqm: 100m), null);
        var round2 = CrmBriefHeadSnapshot.Create(brief, 2);

        IReadOnlyList<string> diff = Fields(round2.DiffFrom(round1, BriefDiffAudience.Office));

        Assert.Contains(nameof(CrmBriefHeadSnapshot.Address), diff);
        Assert.DoesNotContain(nameof(CrmBriefHeadSnapshot.AreaSqm), diff);
    }

    [Fact]
    public void DiffFrom_DetectsMultipleChangedFields()
    {
        CrmDesignBrief brief = CreateBrief(address: "Old", areaSqm: 100m);
        var round1 = CrmBriefHeadSnapshot.Create(brief, 1);

        brief.UpdateHead(
            new CrmBriefHeadData((UnitType)2, (UnitCondition)2, Address: "New", AreaSqm: 200m), null);
        var round2 = CrmBriefHeadSnapshot.Create(brief, 2);

        IReadOnlyList<string> diff = Fields(round2.DiffFrom(round1, BriefDiffAudience.Office));

        Assert.Contains(nameof(CrmBriefHeadSnapshot.UnitType), diff);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.UnitCondition), diff);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.Address), diff);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.AreaSqm), diff);
        Assert.Equal(4, diff.Count);
    }

    [Fact]
    public void ClientDiff_NeverContainsExpectedBudget_OrLastMeetingId()
    {
        CrmDesignBrief brief = BriefWithBudget(1000m, Guid.NewGuid());
        var round1 = CrmBriefHeadSnapshot.Create(brief, 1);
        brief.UpdateHead(
            new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: "Moved", ExpectedBudget: 9999m, IsLicensed: true),
            Guid.NewGuid());
        var round2 = CrmBriefHeadSnapshot.Create(brief, 2);

        List<string> client = Fields(round2.DiffFrom(round1, BriefDiffAudience.Client));
        List<string> clientRoundOne = Fields(round1.DiffFrom(null, BriefDiffAudience.Client));
        List<string> office = Fields(round2.DiffFrom(round1, BriefDiffAudience.Office));

        Assert.Equal([nameof(CrmBriefHeadSnapshot.Address)], client);
        Assert.DoesNotContain(nameof(CrmBriefHeadSnapshot.ExpectedBudget), clientRoundOne);
        Assert.DoesNotContain(nameof(CrmBriefHeadSnapshot.LastMeetingId), clientRoundOne);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.ExpectedBudget), office);
        Assert.Contains(nameof(CrmBriefHeadSnapshot.LastMeetingId), office);
    }

    [Fact]
    public void RoundOne_WithNoPrevious_ReportsEveryFieldThatHasAValue()
    {
        var round1 = CrmBriefHeadSnapshot.Create(BriefWithBudget(1500.50m), 1);

        IReadOnlyList<BriefFieldChange> diff = round1.DiffFrom(null, BriefDiffAudience.Office);

        Assert.All(diff, c => Assert.Null(c.PreviousValue));
        Assert.Equal(
            [
                nameof(CrmBriefHeadSnapshot.UnitType), nameof(CrmBriefHeadSnapshot.UnitCondition),
                nameof(CrmBriefHeadSnapshot.Address), nameof(CrmBriefHeadSnapshot.IsLicensed),
                nameof(CrmBriefHeadSnapshot.HiringReasons), nameof(CrmBriefHeadSnapshot.ExpectedBudget),
            ],
            Fields(diff));
    }

    [Fact]
    public void Values_AreRawInvariantStrings()
    {
        var round1 = CrmBriefHeadSnapshot.Create(BriefWithBudget(1500.50m), 1);

        var values = round1.DiffFrom(null, BriefDiffAudience.Office)
            .ToDictionary(c => c.Field, c => c.CurrentValue);

        Assert.Equal("1", values[nameof(CrmBriefHeadSnapshot.UnitType)]);
        Assert.Equal("true", values[nameof(CrmBriefHeadSnapshot.IsLicensed)]);
        Assert.Equal("0", values[nameof(CrmBriefHeadSnapshot.HiringReasons)]);
        Assert.Equal("1500.50", values[nameof(CrmBriefHeadSnapshot.ExpectedBudget)]);
        Assert.Equal("Addr", values[nameof(CrmBriefHeadSnapshot.Address)]);
    }
}
