using BuildingBlocks.Messaging;
using Crm.Contracts.Portal;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>FINDING-P1-18: the portal decide runs inside CRM's <c>DomainEventDispatchDecorator</c> transaction
/// (on <see cref="CrmDbContext"/>'s connection), while Approvals decides in a transaction of its own on
/// <c>ApprovalsDbContext</c>'s connection and commits it before returning. A CRM rollback cannot undo that
/// commit, so the handler must write nothing to CRM after the decide call. This pins it: in the handler's
/// scope, no <see cref="CrmDbContext"/> save with pending changes follows the Approvals save.</summary>
public sealed partial class PortalApprovalTests
{
    [Fact]
    public async Task PortalDecide_MakesNoCrmWrite_AfterApprovalsCommitsTheDecision()
    {
        (BriefTestProject p, Guid approvalId) = await SentBriefAsync("portal-no-write-after-decide");

        await using AsyncServiceScope scope = fixture.CreateScope();
        List<string> saves = [];
        CrmDbContext crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        crm.SavingChanges += (_, _) =>
        {
            if (crm.ChangeTracker.HasChanges())
            {
                saves.Add("crm-write");
            }
        };

        // ApprovalsDbContext is Approvals-internal; the base DbContext's events are all this needs.
        Type approvalsContextType = Type.GetType(
            "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure", throwOnError: true)!;
        var approvals = (DbContext)scope.ServiceProvider.GetRequiredService(approvalsContextType);
        approvals.SavedChanges += (_, e) =>
        {
            if (e.EntitiesSavedCount > 0)
            {
                saves.Add("approvals-decide");
            }
        };

        Result result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideMyPortalApprovalCommand>>()
            .HandleAsync(new DecideMyPortalApprovalCommand(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved, null));

        Assert.True(result.IsSuccess, result.Error?.Description);
        int decided = saves.IndexOf("approvals-decide");
        Assert.True(decided >= 0, "the decision was not saved through the scope's ApprovalsDbContext");
        Assert.DoesNotContain("crm-write", saves.Skip(decided + 1));
    }
}
