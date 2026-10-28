using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Infrastructure.Decorators;

/// <summary>
/// Project scope for office brief, period, item and space messages (<see cref="ICrmProjectScoped"/>) — the CRM counterpart of
/// <c>Projects.Infrastructure.Decorators.ProjectScopeDecorator</c>, shaped like Sites'
/// <c>SiteScopeDecorator</c>: resolve the subject's <c>ProjectId</c> from this module's own tables, then
/// ask <see cref="IProjectAccessGuard"/>. A subject that does not resolve and a project the caller cannot
/// reach are the same 404 (<see cref="CrmBriefErrors.NotFound"/>), so a caller cannot probe for briefs on
/// projects they are not on.
///
/// <para>The guard passes on <c>project.read_all</c> (read off <c>ICurrentUser</c>) or stage-team
/// membership. That bypass is the guard's own; here it also covers the brief WRITE routes — recorded in
/// CRM-BRIEF-FIX-NOTES.md for the owner.</para>
/// </summary>
internal static class CrmProjectScopeDecorator
{
    internal sealed class CommandHandler<TCommand>(ICommandHandler<TCommand> innerHandler, CrmProjectScope scope)
        : ICommandHandler<TCommand>
        where TCommand : ICommand, ICrmProjectScoped
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result access = await scope.EnsureAsync(command, ct);
            return access.IsFailure ? access : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler, CrmProjectScope scope)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>, ICrmProjectScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result access = await scope.EnsureAsync(command, ct);
            return access.IsFailure
                ? Result.Failure<TResponse>(access.Error)
                : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler, CrmProjectScope scope)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>, ICrmProjectScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            Result access = await scope.EnsureAsync(query, ct);
            return access.IsFailure
                ? Result.Failure<TResponse>(access.Error)
                : await innerHandler.HandleAsync(query, ct);
        }
    }
}

/// <summary>The resolve-then-guard step <see cref="CrmProjectScopeDecorator"/> runs.</summary>
internal sealed class CrmProjectScope(CrmDbContext db, IProjectDirectory projects, IProjectAccessGuard guard)
{
    public async Task<Result> EnsureAsync(ICrmProjectScoped message, CancellationToken ct)
    {
        Error notFound = NotFoundFor(message);
        Guid? projectId = await ResolveProjectIdAsync(message.ScopeTarget, ct);
        if (projectId is null)
        {
            return notFound;
        }

        Result access = await guard.EnsureProjectAccessAsync(projectId.Value, message.UserId, ct);
        return access.IsFailure ? notFound : Result.Success();
    }

    /// <summary>A-1: the 404 names the area the caller asked about — an item route never answers "brief
    /// not found". Chosen by the message's contract namespace; every brief message keeps
    /// <see cref="CrmBriefErrors.NotFound"/>.</summary>
    private static Error NotFoundFor(ICrmProjectScoped message) => message.GetType().Namespace switch
    {
        "Crm.Contracts.Periods" => CrmPeriodErrors.NotFound,
        "Crm.Contracts.Items" => CrmItemErrors.NotFound,
        "Crm.Contracts.Spaces" => CrmSpaceErrors.SpaceNotFound,
        "Crm.Contracts.Meetings" => CrmMeetingErrors.NotFound,
        _ => CrmBriefErrors.NotFound,
    };

    private async Task<Guid?> ResolveProjectIdAsync(CrmScopeTarget target, CancellationToken ct) => target.Kind switch
    {
        CrmScopeTargetKind.Project => target.Id,
        CrmScopeTargetKind.Stage => (await projects.GetStageAsync(target.Id, ct))?.ProjectId,
        CrmScopeTargetKind.Brief => await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => b.Id == target.Id)
            .Select(b => (Guid?)b.ProjectId)
            .FirstOrDefaultAsync(ct),
        CrmScopeTargetKind.BriefSpaceEntry => await db.CrmBriefSpaces.AsNoTracking()
            .Where(e => e.Id == target.Id)
            .Join(db.CrmDesignBriefs, e => e.BriefId, b => b.Id, (_, b) => (Guid?)b.ProjectId)
            .FirstOrDefaultAsync(ct),
        CrmScopeTargetKind.Period => await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => p.Id == target.Id)
            .Select(p => (Guid?)p.ProjectId)
            .FirstOrDefaultAsync(ct),
        CrmScopeTargetKind.Item => await db.CrmRequirementItems.AsNoTracking()
            .Where(i => i.Id == target.Id)
            .Select(i => (Guid?)i.ProjectId)
            .FirstOrDefaultAsync(ct),
        CrmScopeTargetKind.Space => await db.CrmSpaces.AsNoTracking()
            .Where(s => s.Id == target.Id)
            .Select(s => (Guid?)s.ProjectId)
            .FirstOrDefaultAsync(ct),
        CrmScopeTargetKind.ProjectTask => (await projects.GetTaskAsync(target.Id, ct))?.ProjectId,
        CrmScopeTargetKind.Meeting => await db.CrmMeetings.AsNoTracking()
            .Where(m => m.Id == target.Id)
            .Select(m => (Guid?)m.ProjectId)
            .FirstOrDefaultAsync(ct),
        CrmScopeTargetKind.MeetingSlot => await db.CrmMeetingSlots.AsNoTracking()
            .Where(s => s.Id == target.Id)
            .Join(db.CrmMeetings, s => s.MeetingId, m => m.Id, (_, m) => (Guid?)m.ProjectId)
            .FirstOrDefaultAsync(ct),
        _ => null,
    };
}
