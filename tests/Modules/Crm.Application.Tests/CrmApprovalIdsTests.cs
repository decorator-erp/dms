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
}
