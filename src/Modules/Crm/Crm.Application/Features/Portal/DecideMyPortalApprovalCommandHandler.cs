using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

internal sealed class DecideMyPortalApprovalCommandValidator : AbstractValidator<DecideMyPortalApprovalCommand>
{
    public DecideMyPortalApprovalCommandValidator()
    {
        RuleFor(x => x.ApprovalId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

/// <summary>
/// B1.2 (Option A): the client decides a CRM approval in the portal. Order: the route's
/// <c>crm.approvals.decide_own</c> (403) → <c>CrmScopeDecorator</c> and this handler's ownership proof (404)
/// → the approval's own state (409).
///
/// <para><b>Ownership.</b> The approval's subject is mapped to its project — a brief round to the brief's
/// project, a brief-space entry to its brief's project, a requirement item to its own — and the project must
/// be the caller's client's and active. Anything else (another client's subject, a gate condition, a
/// document, an unknown approval) is the same <see cref="CrmPortalErrors.NotFound"/>. Only then is
/// <see cref="IApprovalDecider"/> called; it re-checks that the caller is a Client approver on the row and
/// locks it, so a parallel double decide is one success and one 409.</para>
///
/// <para>The subject reacts the way it does to an office decision: Approvals enqueues
/// <c>ApprovalDecidedIntegrationEvent</c> in the decision's own save and CRM's handler applies it from the
/// outbox.</para>
/// </summary>
internal sealed class DecideMyPortalApprovalCommandHandler(
    CrmDbContext db,
    ICrmAccessGuard guard,
    IProjectDirectory projectDirectory,
    IApprovalRequester approvals,
    IApprovalDecider decider)
    : ICommandHandler<DecideMyPortalApprovalCommand>
{
    public async Task<Result> HandleAsync(DecideMyPortalApprovalCommand command, CancellationToken ct = default)
    {
        ApprovalDecision? decision = command.Decision switch
        {
            PortalApprovalDecision.Approved => ApprovalDecision.Approved,
            PortalApprovalDecision.ChangesRequested => ApprovalDecision.ChangesRequested,
            _ => null,
        };
        if (decision is null)
        {
            return CrmPortalErrors.InvalidDecision;
        }

        Result<Guid> clientId = await PortalClients.ResolveClientIdAsync(guard, command.AppUserId, ct);
        if (clientId.IsFailure)
        {
            return clientId.Error;
        }

        ApprovalSnapshot? approval = await approvals.GetAsync(command.ApprovalId, ct);
        Guid? projectId = approval is null ? null : await PortalApprovalSubjects.ProjectOfAsync(db, approval, ct);
        ProjectSummary? project = projectId is { } id ? await projectDirectory.GetProjectAsync(id, ct) : null;
        if (project is null || project.ClientId != clientId.Value || !project.IsActive)
        {
            return CrmPortalErrors.NotFound;
        }

        // FINDING-P1-18: the decider commits in its own transaction on Approvals' connection, before this
        // command's CRM transaction (DomainEventDispatchDecorator) ends, and a CRM rollback cannot undo it.
        // Invariant: no CRM write after this call - the subject reacts from the outbox instead. Pinned by
        // PortalApprovalTests.PortalDecide_MakesNoCrmWrite_AfterApprovalsCommitsTheDecision.
        Result decided = await decider.DecideAsClientAsync(
            command.ApprovalId, command.AppUserId, decision.Value, command.Comment, ct);

        // A row that names the project's client but not this login (another contact of the same client) is
        // as absent as any other: one 404 code for the portal.
        return decided.IsFailure && decided.Error.Status == 404 ? CrmPortalErrors.NotFound : decided;
    }
}

/// <summary>Maps a CRM approval subject to its project — the one ownership rule the portal decide and the
/// portal approvals list share.</summary>
internal static class PortalApprovalSubjects
{
    public static async Task<Guid?> ProjectOfAsync(CrmDbContext db, ApprovalSnapshot approval, CancellationToken ct) =>
        approval.SubjectType switch
        {
            ApprovalSubjectType.DesignBrief => await db.CrmDesignBriefs.AsNoTracking()
                .Where(b => b.Id == approval.SubjectId).Select(b => (Guid?)b.ProjectId).FirstOrDefaultAsync(ct),
            ApprovalSubjectType.BriefSpace => await db.CrmBriefSpaces.AsNoTracking()
                .Where(e => e.Id == approval.SubjectId)
                .Join(db.CrmDesignBriefs.AsNoTracking(), e => e.BriefId, b => b.Id, (_, b) => (Guid?)b.ProjectId)
                .FirstOrDefaultAsync(ct),
            ApprovalSubjectType.RequirementItem => await db.CrmRequirementItems.AsNoTracking()
                .Where(i => i.Id == approval.SubjectId).Select(i => (Guid?)i.ProjectId).FirstOrDefaultAsync(ct),
            _ => null,
        };
}
