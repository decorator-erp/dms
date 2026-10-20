using Approvals.Contracts;
using Crm.Application.Features.Briefs;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>Pure — no fixture. The deterministic round id behind the H3 retry story.</summary>
public sealed class CrmApprovalIdsTests
{
    [Fact]
    public void SameSubjectAndRound_GiveTheSameId()
    {
        var subjectId = Guid.NewGuid();

        Assert.Equal(
            CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, subjectId, 1),
            CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, subjectId, 1));
    }

    [Fact]
    public void RoundSubjectAndSubjectType_EachChangeTheId()
    {
        var subjectId = Guid.NewGuid();
        Guid baseline = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, subjectId, 1);

        Assert.NotEqual(baseline, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, subjectId, 2));
        Assert.NotEqual(baseline, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, Guid.NewGuid(), 1));
        Assert.NotEqual(baseline, CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, subjectId, 1));
    }

    [Fact]
    public void Id_CarriesNameBasedVersionAndRfcVariantBits()
    {
        Guid id = CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, Guid.NewGuid(), 3);
        byte[] bytes = id.ToByteArray(bigEndian: true);

        Assert.Equal(5, bytes[6] >> 4);
        Assert.Equal(0b10, bytes[8] >> 6);
        Assert.Equal(5, id.Version);
    }

    /// <summary>Pinned: these ids are already stored on live approvals. A change to the namespace, the
    /// byte layout or the <c>ApprovalSubjectType</c> values must break this test rather than silently
    /// re-key rounds in flight.</summary>
    [Fact]
    public void Id_IsPinned_ForAKnownSubjectAndRound()
    {
        var subjectId = new Guid("11111111-2222-3333-4444-555555555555");

        Assert.Equal(
            new Guid("dc9a47ab-5150-5e62-9d4c-517aaaa33e6b"),
            CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, subjectId, 1));
    }
}
