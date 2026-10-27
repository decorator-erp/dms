using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using ApprovalState = Crm.Domain.ApprovalState;

namespace Crm.Application.Tests;

/// <summary>A-11 (B-L1): a brief whose approval was decided while the decided event was lost is no longer
/// stuck — a resend applies a rejection, or voids an approval and sends the next round.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A11_AMissedRejection_IsAppliedByTheResend()
    {
        (BriefTestProject p, Guid briefId) = await SendBriefAsync("a11-rejected");
        await DecideAndLoseTheEventAsync(p, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1), ApprovalDecision.Rejected);

        Result resent = await Command(p.OwnerAppUserId, new SendCrmBriefCommand(briefId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        Assert.Equal(ApprovalState.Rejected, (await LoadBriefAsync(briefId)).Status);
    }

    [Fact]
    public async Task A11_AMissedApproval_IsVoided_AndTheNextRoundIsSent()
    {
        (BriefTestProject p, Guid briefId) = await SendBriefAsync("a11-approved");
        await DecideAndLoseTheEventAsync(p, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1), ApprovalDecision.Approved);

        Result resent = await Command(p.OwnerAppUserId, new SendCrmBriefCommand(briefId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        CrmDesignBrief brief = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.PendingApproval, brief.Status);
        Assert.Equal(CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 2), brief.PendingApprovalId);
    }

    private async Task<(BriefTestProject Project, Guid BriefId)> SendBriefAsync(string label)
    {
        BriefTestProject p = await SeedAsync(label);
        Result<Guid> brief = await CommandFor<CreateCrmDesignBriefCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId,
                new Contracts.Briefs.CrmBriefHeadData((Contracts.UnitType)1, (Contracts.UnitCondition)1), p.OwnerAppUserId));
        Assert.True(brief.IsSuccess, brief.Error?.Description);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmBriefCommand(brief.Value, p.OwnerAppUserId))).IsSuccess);
        return (p, brief.Value);
    }

    /// <summary>The client decides, but the ApprovalDecided outbox row is lost before it is delivered.</summary>
    private async Task DecideAndLoseTheEventAsync(BriefTestProject p, Guid approvalId, ApprovalDecision decision)
    {
        await using (AsyncServiceScope scope = ScopeFor(p.ClientAppUserId, readAll: false))
        {
            Result decided = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideApprovalCommand>>()
                .HandleAsync(new DecideApprovalCommand(approvalId, decision, decision == ApprovalDecision.Approved ? null : "No."));
            Assert.True(decided.IsSuccess, decided.Error?.Description);
        }

        await using AsyncServiceScope shared = fixture.CreateScope();
        SharedDbContext db = shared.ServiceProvider.GetRequiredService<SharedDbContext>();
        List<OutboxMessage> lost = [.. (await db.OutboxMessages.Where(m => m.Type.Contains("ApprovalDecided")).ToListAsync())
            .Where(m => m.Content.Contains(approvalId.ToString(), StringComparison.OrdinalIgnoreCase))];
        Assert.NotEmpty(lost);
        db.OutboxMessages.RemoveRange(lost);
        await db.SaveChangesAsync();
    }

    private async Task<CrmDesignBrief> LoadBriefAsync(Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>().CrmDesignBriefs.AsNoTracking().SingleAsync(b => b.Id == briefId);
    }
}
