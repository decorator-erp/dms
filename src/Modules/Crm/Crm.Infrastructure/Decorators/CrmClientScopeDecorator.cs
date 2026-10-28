using BuildingBlocks;
using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Infrastructure.Decorators;

/// <summary>
/// C-1 (LC-M2): client scope for office client, contact and note messages (<see cref="ICrmClientScoped"/>),
/// shaped like <see cref="CrmProjectScopeDecorator"/>. A subject that does not resolve and a client the
/// caller cannot reach are the same 404, so a caller cannot probe for clients.
/// </summary>
internal static class CrmClientScopeDecorator
{
    internal sealed class CommandHandler<TCommand>(ICommandHandler<TCommand> innerHandler, CrmClientScope scope)
        : ICommandHandler<TCommand>
        where TCommand : ICommand, ICrmClientScoped
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result access = await scope.EnsureAsync(command, ct);
            return access.IsFailure ? access : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler, CrmClientScope scope)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>, ICrmClientScoped
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
        IQueryHandler<TQuery, TResponse> innerHandler, CrmClientScope scope)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>, ICrmClientScoped
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

/// <summary>The resolve-then-check step <see cref="CrmClientScopeDecorator"/> runs. Two depth-1 batch
/// calls into Projects: the client's projects, then which of them the caller can reach.</summary>
internal sealed class CrmClientScope(
    CrmDbContext db, ICurrentUser currentUser, IProjectDirectory projects, IProjectAccessGuard guard)
{
    public async Task<Result> EnsureAsync(ICrmClientScoped message, CancellationToken ct)
    {
        Error notFound = NotFoundFor(message);
        Guid? clientId = await ResolveClientIdAsync(message.ScopeTarget, ct);
        if (clientId is null)
        {
            return notFound;
        }

        return await CanSeeAsync(clientId.Value, message.UserId, ct) ? Result.Success() : notFound;
    }

    /// <summary>The C-1 rule for one client; <c>GetClientsQueryHandler</c> applies the same rule to a page.</summary>
    public async Task<bool> CanSeeAsync(Guid clientId, string userId, CancellationToken ct)
    {
        if (currentUser.HasPermission(CrmPermissions.Clients.ReadAll))
        {
            return true;
        }

        IReadOnlyList<ProjectSummary> owned = await projects.GetProjectsByClientAsync(clientId, ct);
        if (owned.Count == 0)
        {
            return true;
        }

        IReadOnlyList<Guid> reachable = await guard.FilterAccessibleProjectsAsync([.. owned.Select(p => p.Id)], userId, ct);
        return reachable.Count > 0;
    }

    /// <summary>The 404 names the area the caller asked about, as in <see cref="CrmProjectScope"/>.</summary>
    private static Error NotFoundFor(ICrmClientScoped message) => message.GetType().Namespace switch
    {
        "Crm.Contracts.Contacts" => CrmContactErrors.NotFound,
        "Crm.Contracts.Notes" => CrmClientNoteErrors.NotFound,
        _ => ClientErrors.NotFound,
    };

    private async Task<Guid?> ResolveClientIdAsync(CrmClientScopeTarget target, CancellationToken ct) => target.Kind switch
    {
        CrmClientScopeTargetKind.Client => await db.Clients.AsNoTracking()
            .Where(c => c.Id == target.Id)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct),
        CrmClientScopeTargetKind.Contact => await db.CrmContacts.AsNoTracking()
            .Where(c => c.Id == target.Id)
            .Select(c => (Guid?)c.ClientId)
            .FirstOrDefaultAsync(ct),
        CrmClientScopeTargetKind.Note => await db.CrmClientNotes.AsNoTracking()
            .Where(n => n.Id == target.Id)
            .Select(n => (Guid?)n.ClientId)
            .FirstOrDefaultAsync(ct),
        _ => null,
    };
}
